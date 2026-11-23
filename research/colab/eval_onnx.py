"""PHASE 3 - evaluate ONNX LayoutLMv3 models (onnxruntime CPU) with the SAME rules as train_layoutlmv3.py:
chunks with stride, label on the first subtoken of each word, partial-label masking per source, entity-level
F1 per field ONLY over docs that annotate the field (+ the 4-field subset). Optionally the PyTorch checkpoint as
reference. Reuses train_layoutlmv3.encode_split_with_meta and layout_utils (no copied logic).

Colab:
  python eval_onnx.py --data-dir /content/work/bundle --checkpoint /content/drive/MyDrive/pine/run1/final \
      --onnx /content/drive/MyDrive/pine/onnx/model_fp32.onnx /content/drive/MyDrive/pine/onnx/model_int8.onnx \
      --torch-checkpoint /content/drive/MyDrive/pine/run1/final --out eval_onnx_report.json
Per-model id map (pruned vocab):  --onnx pruned.onnx=id_map.json   (or global --id-map id_map.json)
NOT RUN LOCALLY (no torch/onnxruntime here).
"""
from __future__ import annotations

import argparse
import json
import sys
import time
from pathlib import Path
from types import SimpleNamespace

sys.path.insert(0, str(Path(__file__).resolve().parent))
from layout_utils import FIELDS, ID2LABEL, field_report, has_all_fields  # noqa: E402

SPLITS = ("test", "test_wildreceipt", "val")
ONNX_INPUTS = ("input_ids", "bbox", "attention_mask", "pixel_values")


def log(msg):
    print(f"[{time.strftime('%H:%M:%S')}] {msg}", flush=True)


# ---------------------------------------------------------------- id map -----------------------------------
def load_id_map(path, unk_new_id=None):
    """-> (lut ndarray old->new, unk_new_id). Accepts plain {old: new} json; unk_new_id comes from the argument,
    else from id_map_meta.json next to the map, else from the map entry of the RoBERTa <unk> (old id 3)."""
    import numpy as np
    path = Path(path)
    raw = json.loads(path.read_text(encoding="utf-8"))
    raw = raw.get("id_map", raw)
    m = {int(k): int(v) for k, v in raw.items()}
    if unk_new_id is None:
        meta = path.with_name("id_map_meta.json")
        if meta.exists():
            unk_new_id = int(json.loads(meta.read_text(encoding="utf-8"))["unk_new_id"])
        else:
            unk_new_id = m[3]
    lut = np.full(max(m) + 1, unk_new_id, dtype=np.int64)
    for k, v in m.items():
        lut[k] = v
    return lut, unk_new_id


def remap_array(ids, lut, unk_new_id):
    """ids int array of ORIGINAL ids -> new ids (ids beyond the table -> unk)."""
    import numpy as np
    out = np.full(ids.shape, unk_new_id, dtype=np.int64)
    ok = ids < len(lut)
    out[ok] = lut[ids[ok]]
    return out


# ---------------------------------------------------------------- data -------------------------------------
def encode_splits(data_dir: Path, processor, splits=SPLITS, limit=None, max_length=512, stride=128,
                  partial_mode="source", label_all_tokens=False):
    """-> {split: (docs, feats(numpy dicts), metas)} using train_layoutlmv3's own chunking/labeling."""
    from train_layoutlmv3 import encode_split_with_meta
    args = SimpleNamespace(max_length=max_length, stride=stride, partial_mode=partial_mode,
                           label_all_tokens=label_all_tokens)
    out = {}
    for s in splits:
        f = Path(data_dir) / f"{s}.json"
        if not f.exists():
            log(f"split {s}: {f} not found, skipped")
            continue
        docs = json.loads(f.read_text(encoding="utf-8"))
        if limit:
            docs = docs[:limit]
        t = time.time()
        feats, metas = encode_split_with_meta(docs, Path(data_dir), processor, args)
        np_feats = [{k: v.numpy() for k, v in ft.items()} for ft in feats]
        log(f"{s}: {len(docs)} docs -> {len(np_feats)} chunks in {time.time() - t:.0f}s")
        out[s] = (docs, np_feats, metas)
    return out


# ---------------------------------------------------------------- metrics ----------------------------------
def macro_f1(fields_rep: dict) -> float:
    """Average of the 4 field F1s (fields with no supporting doc count as 0)."""
    return sum(fields_rep[f]["f1"] for f in FIELDS) / len(FIELDS)


def report_from_preds(preds, feats, metas, docs, mode="source"):
    """preds: list of int arrays [S] (one per chunk). Same aggregation as train.evaluate_by_field."""
    seqs = []
    for pr, ft, di in zip(preds, feats, metas):
        lb = ft["labels"]
        m = lb != -100
        seqs.append((docs[di], [ID2LABEL[int(x)] for x in lb[m]], [ID2LABEL[int(x)] for x in pr[m]]))
    rep = {"all_docs": field_report(seqs, mode=mode)}
    rep["macro_f1"] = macro_f1(rep["all_docs"])
    sub = [q for q in seqs if has_all_fields(q[0])]
    sub_fields = field_report(sub, mode=mode)
    rep["subset_4fields"] = {"docs": len({id(q[0]) for q in sub}), "fields": sub_fields,
                             "macro_f1": macro_f1(sub_fields)}
    return rep


def _batches(feats, bs):
    for i in range(0, len(feats), bs):
        chunk = feats[i:i + bs]
        yield {k: __import__("numpy").stack([c[k] for c in chunk]) for k in chunk[0] if k != "labels"}


def predict_onnx(session, feats, lut=None, unk_new_id=None, batch_size=8):
    import numpy as np
    names = {i.name for i in session.get_inputs()}
    preds, lat = [], []
    for b in _batches(feats, batch_size):
        if lut is not None:
            b["input_ids"] = remap_array(b["input_ids"], lut, unk_new_id)
        feed = {k: (v.astype(np.int64) if k != "pixel_values" else v.astype(np.float32))
                for k, v in b.items() if k in names}
        t = time.perf_counter()
        lg = session.run(["logits"], feed)[0]
        lat.append((time.perf_counter() - t) / len(b["input_ids"]))
        preds += list(lg.argmax(-1))
    return preds, lat


def predict_torch(model, feats, batch_size=8):
    import torch
    model.eval()
    preds = []
    with torch.no_grad():
        for b in _batches(feats, batch_size):
            lg = model(**{k: torch.from_numpy(v) for k, v in b.items()}).logits
            preds += list(lg.argmax(-1).numpy())
    return preds


def eval_session(session, data, lut=None, unk_new_id=None, mode="source", batch_size=8):
    """data = encode_splits() output. -> {split: report, ..., '_latency_s_per_chunk': median}"""
    import numpy as np
    res, lats = {}, []
    for s, (docs, feats, metas) in data.items():
        preds, lat = predict_onnx(session, feats, lut, unk_new_id, batch_size)
        res[s] = report_from_preds(preds, feats, metas, docs, mode)
        lats += lat
    res["_latency_s_per_chunk_median"] = float(np.median(lats)) if lats else None
    return res


def eval_torch(model, data, mode="source", batch_size=8):
    res = {}
    for s, (docs, feats, metas) in data.items():
        res[s] = report_from_preds(predict_torch(model, feats, batch_size), feats, metas, docs, mode)
    return res


def diff_points(rep, ref):
    """Difference in F1 points (x100) of rep vs ref, per split: macro + per field (all_docs)."""
    out = {}
    for s, r in rep.items():
        if s.startswith("_") or s not in ref:
            continue
        out[s] = {"macro_f1": round(100 * (r["macro_f1"] - ref[s]["macro_f1"]), 2),
                  "subset4_macro_f1": round(100 * (r["subset_4fields"]["macro_f1"]
                                                   - ref[s]["subset_4fields"]["macro_f1"]), 2),
                  **{f: round(100 * (r["all_docs"][f]["f1"] - ref[s]["all_docs"][f]["f1"]), 2) for f in FIELDS}}
    return out


def summarize(name, rep):
    for s, r in rep.items():
        if s.startswith("_"):
            continue
        log(f"[{name}][{s}] Macro-F1={r['macro_f1']:.4f} | " + ", ".join(
            f"{f}={r['all_docs'][f]['f1']:.3f}(n={r['all_docs'][f]['docs']})" for f in FIELDS)
            + f" | subset4({r['subset_4fields']['docs']} docs) Macro-F1={r['subset_4fields']['macro_f1']:.4f}")


def parse_onnx_arg(spec, default_map):
    p, _, m = spec.partition("=")
    return Path(p), (m or default_map)


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--onnx", nargs="*", default=[], help="model.onnx or model.onnx=id_map.json (per model)")
    ap.add_argument("--data-dir", type=Path, required=True)
    ap.add_argument("--checkpoint", type=Path, required=True, help="dir with the tokenizer/processor (original ids)")
    ap.add_argument("--torch-checkpoint", type=Path, default=None, help="PyTorch reference model (optional)")
    ap.add_argument("--id-map", default=None, help="id_map.json (old->new) applied to every --onnx without its own")
    ap.add_argument("--unk-new-id", type=int, default=None)
    ap.add_argument("--splits", nargs="*", default=list(SPLITS))
    ap.add_argument("--limit", type=int, default=None, help="max docs per split (smoke test)")
    ap.add_argument("--batch-size", type=int, default=8)
    ap.add_argument("--max-length", type=int, default=512)
    ap.add_argument("--stride", type=int, default=128)
    ap.add_argument("--partial-mode", choices=["source", "available", "none"], default="source")
    ap.add_argument("--out", type=Path, default=Path("eval_onnx_report.json"))
    a = ap.parse_args(argv)
    if not a.onnx and not a.torch_checkpoint:
        ap.error("give at least one --onnx or --torch-checkpoint")

    import onnxruntime as ort
    from transformers import LayoutLMv3Processor
    processor = LayoutLMv3Processor.from_pretrained(str(a.checkpoint), apply_ocr=False)
    data = encode_splits(a.data_dir, processor, a.splits, a.limit, a.max_length, a.stride, a.partial_mode)

    models, ref_name, ref = {}, None, None
    if a.torch_checkpoint:
        from transformers import LayoutLMv3ForTokenClassification
        tm = LayoutLMv3ForTokenClassification.from_pretrained(str(a.torch_checkpoint))
        ref_name, ref = "torch_reference", eval_torch(tm, data, a.partial_mode, a.batch_size)
        models[ref_name] = ref
        summarize(ref_name, ref)
        del tm
    for spec in a.onnx:
        path, idm = parse_onnx_arg(spec, a.id_map)
        lut, unk = (load_id_map(idm, a.unk_new_id) if idm else (None, None))
        sess = ort.InferenceSession(str(path), providers=["CPUExecutionProvider"])
        rep = eval_session(sess, data, lut, unk, a.partial_mode, a.batch_size)
        rep["_size_mb"] = round(path.stat().st_size / 1e6, 1)
        rep["_id_map"] = idm
        name = path.stem
        models[name] = rep
        if ref is None:
            ref_name, ref = name, rep  # no torch reference: first ONNX model is the reference
        summarize(name, rep)
    report = {"reference": ref_name, "models": models,
              "diff_points_vs_reference": {n: diff_points(r, ref) for n, r in models.items() if n != ref_name}}
    a.out.write_text(json.dumps(report, indent=1), encoding="utf-8")
    log(f"reference = {ref_name}")
    for n, d in report["diff_points_vs_reference"].items():
        log(f"{n} vs {ref_name} (F1 points): " + ", ".join(f"{s}: macro {v['macro_f1']:+.2f}" for s, v in d.items()))
    log(f"report -> {a.out}")


if __name__ == "__main__":
    main()
