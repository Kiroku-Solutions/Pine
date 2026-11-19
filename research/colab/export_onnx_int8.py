"""PHASE 3 ONLY - prepared, NOT executed. Export a fine-tuned LayoutLMv3 checkpoint to ONNX and
apply dynamic INT8 quantization (onnxruntime.quantization), then report sizes.

  pip install "optimum[onnxruntime]" onnx onnxruntime
  python export_onnx_int8.py --checkpoint run1/final --out onnx_out [--quantize-embeddings] [--per-channel]

SIZE RISK (100 MB cap): layoutlmv3-base is ~125M params; FP32 ~500 MB, dynamic INT8 ~125-130 MB,
i.e. ABOVE the 100 MB cap. About 38M params are the word-embedding table (50265x768) plus
position/visual embeddings. quantize_dynamic only quantizes MatMul/Gemm weights by default; the
embedding tables (Gather) stay FP32, so the real INT8 file can be even larger than 125 MB.
Options:
  --quantize-embeddings : also quantize Gather (embedding tables) to INT8. Lossy; re-check F1.
  --per-channel         : better INT8 weight accuracy.
  Shared/tied weights   : check the exported graph for duplicated initializers (the script prints
                          the count of initializers and the largest ones).
  Further fallbacks if still >100 MB: prune the vocabulary to tokens seen in receipts (shrinks the
  embedding table a lot), distill to a smaller model, or use a smaller layout model.
Re-validate F1 of the INT8 model on the test split before shipping.
"""
from __future__ import annotations

import argparse
from pathlib import Path


def mb(p: Path) -> float:
    return sum(f.stat().st_size for f in ([p] if p.is_file() else p.glob("*"))) / 1e6


def largest_initializers(path: Path, n=5):
    import onnx
    m = onnx.load(str(path), load_external_data=False)
    sizes = []
    for t in m.graph.initializer:
        count = 1
        for d in t.dims:
            count *= d
        sizes.append((count, t.name, list(t.dims)))
    sizes.sort(reverse=True)
    print(f"{len(sizes)} initializers; largest:")
    for c, name, dims in sizes[:n]:
        print(f"  {c / 1e6:.1f}M params  {name} {dims}")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--checkpoint", type=Path, required=True)
    ap.add_argument("--out", type=Path, required=True)
    ap.add_argument("--opset", type=int, default=14)
    ap.add_argument("--quantize-embeddings", action="store_true")
    ap.add_argument("--per-channel", action="store_true")
    ap.add_argument("--cap-mb", type=float, default=100.0)
    a = ap.parse_args()

    from onnxruntime.quantization import QuantType, quantize_dynamic
    from optimum.exporters.onnx import main_export

    a.out.mkdir(parents=True, exist_ok=True)
    main_export(str(a.checkpoint), output=a.out / "fp32", task="token-classification", opset=a.opset)
    src = a.out / "fp32" / "model.onnx"
    dst = a.out / "model_int8.onnx"
    ops = ["MatMul", "Gemm"] + (["Gather"] if a.quantize_embeddings else [])
    quantize_dynamic(str(src), str(dst), weight_type=QuantType.QInt8,
                     per_channel=a.per_channel, op_types_to_quantize=ops)
    print(f"fp32: {mb(src):.1f} MB | int8: {mb(dst):.1f} MB | cap {a.cap_mb} MB")
    largest_initializers(dst)
    if mb(dst) > a.cap_mb:
        print("WARNING: INT8 model exceeds the cap; see the module docstring for mitigations.")


if __name__ == "__main__":
    main()
