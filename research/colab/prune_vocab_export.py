"""PHASE 3 - shrink LayoutLMv3 below 100 MB by pruning the word-embedding vocabulary, then export to ONNX,
quantize (dynamic INT8 incl. Gather) and re-evaluate F1 against the original model.

Steps per --keep value (e.g. 8192 12000 16000):
  (a) token ids used by train/val/test/test_wildreceipt (tokenizer of the checkpoint) + all special tokens +
      the 256 byte-level base tokens + most frequent ids up to `keep` rows (fill with lowest ids if the corpus is small)
  (b) word_embeddings cut to those rows, ids remapped compactly (sorted by old id => order kept, <pad>=1 stays),
      id_map.json (old->new) + id_map_meta.json (unk_new_id); out-of-map tokens -> <unk>
  (c) ONNX fp32 export via export_onnx_direct.export_fp32
  (d) dynamic INT8 MatMul/Gemm/Gather in two variants: per_channel=True (falls back to False if ORT fails; reported)
      and per_channel=False
  (e) size vs 100 MB cap and F1 (eval_onnx, same rules as train) vs the original PyTorch model.

Colab:
  python prune_vocab_export.py --checkpoint /content/drive/MyDrive/pine/run1/final --data-dir /content/work/bundle \
      --out /content/drive/MyDrive/pine/onnx_pruned --keep 8192 12000 16000
NOT RUN LOCALLY (no torch here): only the pure logic (vocab_prune_utils) is unit-tested.
"""
from __future__ import annotations

import argparse
import copy
import json
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from vocab_prune_utils import (build_id_map, byte_level_ids, compute_keep_ids, count_token_freqs,  # noqa: E402
                               coverage)

CORPUS_SPLITS = ("train", "val", "test", "test_wildreceipt")


def log(msg):
    print(f"[{time.strftime('%H:%M:%S')}] {msg}", flush=True)


def file_mb(p: Path) -> float:
    """Size of the .onnx plus any external-data sidecar."""
    tot = p.stat().st_size
    for side in (p.with_name(p.name + ".data"), p.with_suffix(".onnx_data"), p.with_name(p.stem + ".onnx.data")):
        if side.exists():
            tot += side.stat().st_size
    return tot / 1e6


def corpus_frequencies(data_dir: Path, tokenizer) -> "Counter":  # noqa: F821
    from layout_utils import build_example
    seqs = []
    for s in CORPUS_SPLITS:
        f = data_dir / f"{s}.json"
        if not f.exists():
            log(f"corpus: {f} missing, skipped")
            continue
        docs = json.loads(f.read_text(encoding="utf-8"))
        for doc in docs:
            words, boxes, _ = build_example(doc, [])
            if not words:
                continue
            enc = tokenizer(words, boxes=boxes, add_special_tokens=False, truncation=False)
            seqs.append(enc["input_ids"])
        log(f"corpus: {s} tokenized ({len(docs)} docs)")
    return count_token_freqs(seqs)


def special_ids(tokenizer):
    ids = set(tokenizer.all_special_ids)
    for t in ("cls_token_id", "sep_token_id", "pad_token_id", "unk_token_id", "mask_token_id", "bos_token_id",
              "eos_token_id"):
        v = getattr(tokenizer, t, None)
        if v is not None:
            ids.add(int(v))
    return sorted(ids)


def prune_model(model, keep_ids, id_map, pad_old):
    """Deep-copied model with word_embeddings cut to keep_ids (row i of new table = old row keep_ids[i])."""
    import torch
    m = copy.deepcopy(model)
    emb = m.layoutlmv3.embeddings
    old = emb.word_embeddings
    new_pad = id_map[pad_old]
    new = torch.nn.Embedding(len(keep_ids), old.embedding_dim, padding_idx=new_pad)
    with torch.no_grad():
        new.weight.copy_(old.weight[torch.tensor(keep_ids, dtype=torch.long)])
    emb.word_embeddings = new
    m.config.vocab_size = len(keep_ids)
    if new_pad != pad_old:  # position ids are computed from padding_idx: keep it consistent
        m.config.pad_token_id = new_pad
        if hasattr(emb, "padding_idx"):
            emb.padding_idx = new_pad
    return m


def quantize_variant(src: Path, dst: Path, per_channel: bool):
    """Dynamic INT8 on MatMul/Gemm/Gather. per_channel=True falls back to False if ORT fails (at quantize time OR
    when the quantized model is loaded). Returns (per_channel_used, note)."""
    import onnxruntime as ort
    from onnxruntime.quantization import QuantType, quantize_dynamic
    ops = ["MatMul", "Gemm", "Gather"]
    note = ""
    for pc in ([True, False] if per_channel else [False]):
        try:
            quantize_dynamic(str(src), str(dst), weight_type=QuantType.QInt8, op_types_to_quantize=ops,
                             per_channel=pc)
            ort.InferenceSession(str(dst), providers=["CPUExecutionProvider"])  # does it actually load?
            return pc, note
        except Exception as e:  # noqa: BLE001
            note = f"per_channel={pc} failed: {type(e).__name__}: {str(e)[:200]}"
            log("WARNING " + note + (" -> falling back to per_channel=False" if pc else ""))
            if not pc:
                raise
    raise RuntimeError("unreachable")


def suggest_next_lever(rows, cap_mb, hidden):
    best = min(rows, key=lambda r: r["size_mb"])
    lines = [f"No variant reached the {cap_mb:.0f} MB cap: smallest is {best['size_mb']:.1f} MB "
             f"(keep={best['keep']}, {best['variant']}), {best['size_mb'] - cap_mb:.1f} MB over."]
    emb_int8 = best["keep"] * hidden / 1e6
    lines.append(f"  The int8 embedding table is only ~{emb_int8:.1f} MB there; the remaining ~{best['size_mb'] - emb_int8:.1f} MB "
                 "is the encoder (about 85M params at 1 byte) plus small tensors, so more vocab pruning cannot "
                 "close the gap.")
    lines.append("  Suggested next levers (NOT implemented here), in order of expected payoff:")
    lines.append("   1. 4-bit weight-only quantization of the encoder MatMuls (ORT MatMulNBits / "
                 "MatMul4BitsQuantizer, block_size 32-128): ~halves the encoder, needs an F1 re-check.")
    lines.append("   2. Distill / drop encoder layers (e.g. 12 -> 8) and fine-tune again.")
    lines.append("   3. Minor: prune unused position embeddings (514 rows -> 512 or fewer, < 1 MB) and keep the "
                 "remaining fp32 tensors (LayerNorm, biases, visual patch embed) in fp16 (a few MB). "
                 "fp16 for the word-embedding table only helps if it is NOT already int8 (int8 is smaller).")
    return "\n".join(lines)


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--checkpoint", type=Path, required=True)
    ap.add_argument("--data-dir", type=Path, required=True)
    ap.add_argument("--out", type=Path, required=True)
    ap.add_argument("--keep", type=int, nargs="+", default=[16000], help="vocab rows to keep, e.g. 8192 12000 16000")
    ap.add_argument("--opset", type=int, default=17)
    ap.add_argument("--cap-mb", type=float, default=100.0)
    ap.add_argument("--splits", nargs="*", default=["test", "test_wildreceipt", "val"], help="splits to evaluate")
    ap.add_argument("--limit", type=int, default=None, help="max docs per split for eval (smoke test)")
    ap.add_argument("--batch-size", type=int, default=8)
    ap.add_argument("--primary-split", default="test", help="split used for the final size-vs-Macro-F1 table")
    ap.add_argument("--baseline-onnx", type=Path, default=None, help="extra original ONNX to evaluate (optional)")
    ap.add_argument("--no-torch-ref", action="store_true", help="skip the PyTorch reference evaluation")
    ap.add_argument("--eval-fp32", action="store_true", help="also evaluate the pruned fp32 model (slower)")
    ap.add_argument("--keep-fp32-files", action="store_true", help="do not delete pruned fp32 .onnx after quantizing")
    a = ap.parse_args(argv)
    a.out.mkdir(parents=True, exist_ok=True)

    import onnxruntime as ort
    from transformers import LayoutLMv3ForTokenClassification, LayoutLMv3Processor
    import eval_onnx as ev
    from export_onnx_direct import export_fp32

    processor = LayoutLMv3Processor.from_pretrained(str(a.checkpoint), apply_ocr=False)
    tok = processor.tokenizer
    model = LayoutLMv3ForTokenClassification.from_pretrained(str(a.checkpoint)).eval()
    vocab_size = model.layoutlmv3.embeddings.word_embeddings.num_embeddings
    hidden = model.config.hidden_size

    freqs = corpus_frequencies(a.data_dir, tok)
    specials = special_ids(tok)
    bytes_ids = byte_level_ids(tok.get_vocab())
    log(f"vocab {vocab_size}; corpus distinct ids {len(freqs)}; specials {len(specials)}; byte-level {len(bytes_ids)}")
    pad_old, unk_old = int(tok.pad_token_id), int(tok.unk_token_id)

    data = ev.encode_splits(a.data_dir, processor, a.splits, a.limit)
    results = {"cap_mb": a.cap_mb, "vocab_size": vocab_size, "corpus_distinct_ids": len(freqs), "runs": {}}
    ref_name = None
    ref = None
    if not a.no_torch_ref:
        ref_name, ref = "torch_original", ev.eval_torch(model, data, "source", a.batch_size)
        ev.summarize(ref_name, ref)
        results["reference"] = {"name": ref_name, "report": ref}
    if a.baseline_onnx:
        sess = ort.InferenceSession(str(a.baseline_onnx), providers=["CPUExecutionProvider"])
        rep = ev.eval_session(sess, data, None, None, "source", a.batch_size)
        rep["_size_mb"] = round(file_mb(a.baseline_onnx), 1)
        results["baseline_onnx"] = rep
        ev.summarize("baseline_onnx", rep)
        if ref is None:
            ref_name, ref = "baseline_onnx", rep

    rows = []
    for keep in a.keep:
        rdir = a.out / f"keep{keep}"
        rdir.mkdir(parents=True, exist_ok=True)
        keep_ids = compute_keep_ids(freqs, specials, bytes_ids, keep, vocab_size)
        id_map = build_id_map(keep_ids)
        unk_new = id_map[unk_old]
        cov = coverage(freqs, id_map)
        log(f"keep={keep}: {len(keep_ids)} rows, corpus occurrence coverage {cov['token_occurrences_covered']:.4%}, "
            f"unk_new_id={unk_new}")
        (rdir / "id_map.json").write_text(json.dumps({str(k): v for k, v in id_map.items()}), encoding="utf-8")
        (rdir / "id_map_meta.json").write_text(json.dumps(
            {"unk_new_id": unk_new, "pad_new_id": id_map[pad_old], "rows": len(keep_ids), "coverage": cov,
             "special_old_ids": specials}), encoding="utf-8")
        pm = prune_model(model, keep_ids, id_map, pad_old)
        fp32 = rdir / "model_fp32.onnx"
        export_fp32(pm, fp32, a.opset)
        del pm
        lut, unk = ev.load_id_map(rdir / "id_map.json", unk_new)
        run = {"rows": len(keep_ids), "coverage": cov, "fp32_mb": round(file_mb(fp32), 1), "variants": {}}
        variants = [("int8_emb_perchannel", True), ("int8_emb_pertensor", False)]
        to_eval = [(n, fp32) for n in ["fp32"] if a.eval_fp32]
        for vname, want_pc in variants:
            dst = rdir / f"model_{vname}.onnx"
            try:
                used_pc, note = quantize_variant(fp32, dst, want_pc)
            except Exception as e:  # noqa: BLE001
                run["variants"][vname] = {"error": f"{type(e).__name__}: {str(e)[:300]}"}
                log(f"keep={keep} {vname}: FAILED {e}")
                continue
            size = file_mb(dst)
            run["variants"][vname] = {"size_mb": round(size, 1), "vs_cap_mb": round(size - a.cap_mb, 1),
                                      "per_channel_requested": want_pc, "per_channel_used": used_pc,
                                      "note": note}
            log(f"keep={keep} {vname}: {size:.1f} MB ({size - a.cap_mb:+.1f} MB vs cap {a.cap_mb:.0f}), "
                f"per_channel used={used_pc}" + (f" [fell back: {note}]" if want_pc and not used_pc else ""))
            to_eval.append((vname, dst))
        for vname, path in to_eval:
            sess = ort.InferenceSession(str(path), providers=["CPUExecutionProvider"])
            rep = ev.eval_session(sess, data, lut, unk, "source", a.batch_size)
            name = f"keep{keep}_{vname}"
            ev.summarize(name, rep)
            entry = run["variants"].setdefault(vname, {"size_mb": round(file_mb(path), 1)})
            entry["report"] = rep
            if ref is not None:
                entry["diff_points_vs_reference"] = ev.diff_points(rep, ref)
            if "size_mb" in entry and "error" not in entry:
                rows.append({"keep": keep, "variant": vname, "size_mb": entry["size_mb"],
                             "macro": rep.get(a.primary_split, {}).get("macro_f1"),
                             "diff": entry.get("diff_points_vs_reference", {}).get(a.primary_split, {}).get("macro_f1")})
        if not a.keep_fp32_files and fp32.exists():
            fp32.unlink()
            for side in rdir.glob("model_fp32.onnx*"):
                side.unlink()
        results["runs"][str(keep)] = run
        (a.out / "prune_report.json").write_text(json.dumps(results, indent=1), encoding="utf-8")  # incremental

    # final table
    lines = [f"\nSize vs Macro-F1 on '{a.primary_split}' (reference: {ref_name or 'none'}"
             + (f", Macro-F1 {ref[a.primary_split]['macro_f1']:.4f}" if ref and a.primary_split in ref else "") + ")",
             f"{'keep':>6} {'variant':<22} {'MB':>7} {'vs cap':>8} {'MacroF1':>8} {'d pts':>7}"]
    for r in rows:
        mf = f"{r['macro']:.4f}" if r["macro"] is not None else "   n/a"
        df = f"{r['diff']:+.2f}" if r["diff"] is not None else "  n/a"
        lines.append(f"{r['keep']:>6} {r['variant']:<22} {r['size_mb']:>7.1f} {r['size_mb'] - a.cap_mb:>+8.1f} {mf:>8} {df:>7}")
    under = [r for r in rows if r["size_mb"] <= a.cap_mb]
    if under:
        b = max(under, key=lambda r: (r["macro"] or 0))
        lines.append(f"\nBest under {a.cap_mb:.0f} MB: keep={b['keep']} {b['variant']} ({b['size_mb']:.1f} MB).")
    elif rows:
        lines.append("\n" + suggest_next_lever(rows, a.cap_mb, hidden))
    table = "\n".join(lines)
    print(table)
    results["table"] = table
    (a.out / "prune_report.json").write_text(json.dumps(results, indent=1), encoding="utf-8")
    log(f"report -> {a.out / 'prune_report.json'}; per-run id_map.json in {a.out}/keepN/")


if __name__ == "__main__":
    main()
