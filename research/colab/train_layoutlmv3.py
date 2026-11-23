"""Fine-tune LayoutLMv3 for token classification (MERCHANT / DATE / TOTAL / TAX) on FUNSD-BIO JSON.

Meant for Google Colab (T4). NOT run locally. Example:
  python train_layoutlmv3.py --bundle /content/drive/MyDrive/pine/pine_bundle.zip \
      --work-dir /content/work --output-dir /content/drive/MyDrive/pine/run1
Words+boxes are given (apply_ocr=False). Boxes normalized to 0-1000. Labels only on the first
subtoken of each word. Partial labels: B-/I- tags of fields a source does not annotate are
masked to -100 (see layout_utils.annotated_fields; --partial-mode source|available|none).
"""
from __future__ import annotations

import argparse
import inspect
import json
import os
import random
import sys
import time
import zipfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from layout_utils import (FIELDS, ID2LABEL, LABEL2ID, LABELS, align_labels, annotated_fields,  # noqa: E402
                          build_example, field_report, has_all_fields)


def parse_args(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--bundle", type=Path, help="zip made by prepare_colab_bundle.py")
    ap.add_argument("--data-dir", type=Path, help="already-extracted bundle (skips unzip)")
    ap.add_argument("--work-dir", type=Path, default=Path("/content/work"))
    ap.add_argument("--output-dir", type=Path, required=True, help="checkpoints + final model (put it on Drive)")
    ap.add_argument("--model", default="microsoft/layoutlmv3-base")
    ap.add_argument("--epochs", type=int, default=15)
    ap.add_argument("--batch-size", type=int, default=4)
    ap.add_argument("--grad-accum", type=int, default=4, help="effective batch = batch-size * grad-accum")
    ap.add_argument("--lr", type=float, default=5e-5)
    ap.add_argument("--weight-decay", type=float, default=0.01)
    ap.add_argument("--warmup-ratio", type=float, default=0.1)
    ap.add_argument("--max-length", type=int, default=512)
    ap.add_argument("--stride", type=int, default=128, help="token overlap between chunks of long docs")
    ap.add_argument("--label-all-tokens", action="store_true", help="label every subtoken (I- for continuations)")
    ap.add_argument("--partial-mode", choices=["source", "available", "none"], default="source")
    ap.add_argument("--patience", type=int, default=4, help="early-stopping patience (epochs)")
    ap.add_argument("--seed", type=int, default=42)
    ap.add_argument("--no-fp16", action="store_true")
    ap.add_argument("--limit", type=int, default=None, help="max docs per split (smoke test)")
    ap.add_argument("--resume", action="store_true", help="resume from last checkpoint in output-dir")
    ap.add_argument("--eval-test", action="store_true", help="also evaluate test and test_wildreceipt (held-out) at the end")
    ap.add_argument("--num-workers", type=int, default=2)
    return ap.parse_args(argv)


def log(msg):
    print(f"[{time.strftime('%H:%M:%S')}] {msg}", flush=True)


def gpu_mem():
    import torch
    if not torch.cuda.is_available():
        return "cpu"
    return (f"alloc={torch.cuda.memory_allocated() / 2**30:.2f}GiB "
            f"peak={torch.cuda.max_memory_allocated() / 2**30:.2f}GiB")


def seed_everything(seed):
    import numpy as np
    import torch
    random.seed(seed)
    np.random.seed(seed)
    torch.manual_seed(seed)
    torch.cuda.manual_seed_all(seed)
    os.environ["PYTHONHASHSEED"] = str(seed)


def extract_bundle(bundle: Path, dest: Path) -> Path:
    if not (dest / "train.json").exists():
        log(f"unzipping {bundle} -> {dest}")
        dest.mkdir(parents=True, exist_ok=True)
        with zipfile.ZipFile(bundle) as z:
            z.extractall(dest)
    return dest


def encode_split(docs, root: Path, processor, args):
    """Docs -> list of feature dicts (tensors), one per chunk (overflow for long docs)."""
    from PIL import Image
    import torch
    feats = []
    for n, doc in enumerate(docs):
        fields_ok = annotated_fields(doc, args.partial_mode)
        words, boxes, wlabels = build_example(doc, fields_ok)
        if not words:
            continue
        with Image.open(root / doc["image"]) as im:
            image = im.convert("RGB")
        enc = processor(image, words, boxes=boxes, truncation=True, padding="max_length",
                        max_length=args.max_length, stride=args.stride,
                        return_overflowing_tokens=True, return_tensors="pt")
        for c in range(enc["input_ids"].shape[0]):
            labels = align_labels(enc.word_ids(c), wlabels, args.label_all_tokens)
            feats.append({
                "input_ids": enc["input_ids"][c], "attention_mask": enc["attention_mask"][c],
                "bbox": enc["bbox"][c], "pixel_values": enc["pixel_values"][c],
                "labels": torch.tensor(labels, dtype=torch.long),
            })
        if (n + 1) % 200 == 0:
            log(f"  encoded {n + 1}/{len(docs)} docs")
    return feats


def encode_split_with_meta(docs, root, processor, args):
    """Like encode_split but also returns, per chunk, the index of its doc (for per-field eval)."""
    feats, metas = [], []
    for n, doc in enumerate(docs):
        f = encode_split([doc], root, processor, args)
        feats += f
        metas += [n] * len(f)
    return feats, metas


def evaluate_by_field(trainer, feats, metas, docs, mode):
    """Per-field entity F1 only over docs that annotate the field + the 4-field subset."""
    import numpy as np
    pred = trainer.predict(ListDataset(feats)).predictions
    preds = np.argmax(pred, axis=2)
    seqs = []
    for pr, ft, di in zip(preds, feats, metas):
        lb = ft["labels"].numpy()
        m = lb != -100
        seqs.append((docs[di], [ID2LABEL[int(x)] for x in lb[m]], [ID2LABEL[int(x)] for x in pr[m]]))
    rep = {"all_docs": field_report(seqs, mode=mode)}
    sub = [q for q in seqs if has_all_fields(q[0])]
    rep["subset_4fields"] = {"docs": len({id(q[0]) for q in sub}), "fields": field_report(sub, mode=mode)}
    return rep


class ListDataset:
    def __init__(self, feats):
        self.feats = feats

    def __len__(self):
        return len(self.feats)

    def __getitem__(self, i):
        return self.feats[i]


def compute_metrics(p):
    import numpy as np
    from seqeval.metrics import classification_report, f1_score, precision_score, recall_score
    preds = np.argmax(p.predictions, axis=2)
    y_true, y_pred = [], []
    for pr, lb in zip(preds, p.label_ids):
        m = lb != -100
        y_true.append([ID2LABEL[int(x)] for x in lb[m]])
        y_pred.append([ID2LABEL[int(x)] for x in pr[m]])
    out = {"precision": precision_score(y_true, y_pred), "recall": recall_score(y_true, y_pred),
           "f1": f1_score(y_true, y_pred)}
    for f, d in classification_report(y_true, y_pred, output_dict=True).items():
        if f in ("MERCHANT", "DATE", "TOTAL", "TAX"):
            out[f"f1_{f}"] = d["f1-score"]
            out[f"support_{f}"] = d["support"]
    return out


def main(argv=None):
    args = parse_args(argv)
    import torch
    from transformers import (EarlyStoppingCallback, LayoutLMv3ForTokenClassification,
                              LayoutLMv3Processor, Trainer, TrainerCallback, TrainingArguments,
                              default_data_collator)
    t0 = time.time()
    seed_everything(args.seed)
    log(f"torch {torch.__version__} cuda={torch.cuda.is_available()} "
        f"device={torch.cuda.get_device_name(0) if torch.cuda.is_available() else '-'}")
    root = args.data_dir or extract_bundle(args.bundle, args.work_dir / "bundle")
    names = ("train", "val", "test", "test_wildreceipt")
    splits = {s: json.loads((root / f"{s}.json").read_text(encoding="utf-8")) for s in names
              if (root / f"{s}.json").exists()}
    if args.limit:
        splits = {k: v[: args.limit] for k, v in splits.items()}
    log({k: len(v) for k, v in splits.items()})

    processor = LayoutLMv3Processor.from_pretrained(args.model, apply_ocr=False)
    data = {}
    metas = {}
    for s in ("train", "val") + (tuple(x for x in ("test", "test_wildreceipt") if x in splits) if args.eval_test else ()):
        t = time.time()
        feats, metas[s] = encode_split_with_meta(splits[s], root, processor, args)
        data[s] = ListDataset(feats)
        log(f"{s}: {len(splits[s])} docs -> {len(data[s])} chunks in {time.time() - t:.0f}s")

    model = LayoutLMv3ForTokenClassification.from_pretrained(
        args.model, num_labels=len(LABELS), id2label=ID2LABEL, label2id=LABEL2ID)

    class StatsCallback(TrainerCallback):
        def on_epoch_end(self, a, state, control, **kw):
            log(f"epoch {state.epoch:.1f} step {state.global_step} elapsed {time.time() - t0:.0f}s gpu {gpu_mem()}")

        def on_evaluate(self, a, state, control, metrics=None, **kw):
            log("eval " + json.dumps({k: round(v, 4) for k, v in (metrics or {}).items() if isinstance(v, float)}))

    ta_kwargs = dict(
        output_dir=str(args.output_dir), num_train_epochs=args.epochs,
        per_device_train_batch_size=args.batch_size, per_device_eval_batch_size=args.batch_size * 2,
        gradient_accumulation_steps=args.grad_accum, learning_rate=args.lr,
        weight_decay=args.weight_decay,
        fp16=torch.cuda.is_available() and not args.no_fp16, seed=args.seed, data_seed=args.seed,
        save_strategy="epoch", save_total_limit=2, load_best_model_at_end=True,
        metric_for_best_model="f1", greater_is_better=True, logging_steps=25, report_to="none",
        dataloader_num_workers=args.num_workers,
    )
    ta_params = inspect.signature(TrainingArguments.__init__).parameters
    ta_kwargs["eval_strategy" if "eval_strategy" in ta_params else "evaluation_strategy"] = "epoch"
    # transformers >= 5 removed warmup_ratio: warmup_steps < 1 is interpreted as a ratio of total steps.
    if "warmup_ratio" in ta_params:
        ta_kwargs["warmup_ratio"] = args.warmup_ratio
    else:
        ta_kwargs["warmup_steps"] = args.warmup_ratio
    unknown = sorted(k for k in ta_kwargs if k not in ta_params)
    if unknown:  # never die on a renamed/removed optional argument; say so in the log instead
        log(f"WARNING: TrainingArguments in this transformers version does not accept {unknown}; dropping them")
        for k in unknown:
            ta_kwargs.pop(k)
    trainer = Trainer(model=model, args=TrainingArguments(**ta_kwargs), train_dataset=data["train"],
                      eval_dataset=data["val"], data_collator=default_data_collator,
                      compute_metrics=compute_metrics,
                      callbacks=[EarlyStoppingCallback(early_stopping_patience=args.patience), StatsCallback()])
    resume = args.resume and any(Path(args.output_dir).glob("checkpoint-*"))
    trainer.train(resume_from_checkpoint=True if resume else None)

    final = Path(args.output_dir) / "final"
    trainer.save_model(str(final))
    processor.save_pretrained(str(final))
    (final / "id2label.json").write_text(json.dumps(ID2LABEL, indent=1), encoding="utf-8")
    val = trainer.evaluate(data["val"], metric_key_prefix="val")
    val["train_seconds_total"] = round(time.time() - t0)
    val["args"] = {k: str(v) for k, v in vars(args).items()}
    (final / "val_metrics.json").write_text(json.dumps(val, indent=1), encoding="utf-8")
    log("val metrics: " + json.dumps({k: round(v, 4) for k, v in val.items() if isinstance(v, float)}))
    reports = {"val": evaluate_by_field(trainer, data["val"].feats, metas["val"], splits["val"], args.partial_mode)}
    if args.eval_test:
        for name in ("test", "test_wildreceipt"):  # test_wildreceipt = held-out official WildReceipt test
            if name not in data:
                continue
            m = trainer.evaluate(data[name], metric_key_prefix=name)
            (final / f"{name}_metrics.json").write_text(json.dumps(m, indent=1), encoding="utf-8")
            log(f"{name} metrics: " + json.dumps({k: round(v, 4) for k, v in m.items() if isinstance(v, float)}))
            reports[name] = evaluate_by_field(trainer, data[name].feats, metas[name], splits[name], args.partial_mode)
    (final / "field_report.json").write_text(json.dumps(reports, indent=1), encoding="utf-8")
    for name, r in reports.items():
        log(f"[{name}] per-field F1 (docs annotating the field only): " + ", ".join(
            f"{f}={v['f1']:.3f}(n={v['docs']})" for f, v in r["all_docs"].items()))
        log(f"[{name}] subset 4 fields ({r['subset_4fields']['docs']} docs): " + ", ".join(
            f"{f}={v['f1']:.3f}" for f, v in r["subset_4fields"]["fields"].items()))
    log(f"done in {time.time() - t0:.0f}s, gpu {gpu_mem()}; model in {final}")


if __name__ == "__main__":
    main()
