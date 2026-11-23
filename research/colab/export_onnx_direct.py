"""PHASE 3 - export a fine-tuned LayoutLMv3 token-classification checkpoint to ONNX (no `optimum`),
quantize it to INT8 (dynamic), and check how much the INT8 predictions drift from the original.

Run in Colab (GPU not needed):
  pip install -q onnx onnxruntime
  python export_onnx_direct.py --checkpoint /content/drive/MyDrive/pine/run1/final \
      --data-dir /content/work/bundle --out /content/drive/MyDrive/pine/onnx

Outputs in --out:  model_fp32.onnx, model_int8.onnx (MatMul/Gemm only),
                   model_int8_emb.onnx (MatMul/Gemm + embedding Gather), export_report.json
ONNX inputs : input_ids[B,S] int64, bbox[B,S,4] int64 (0..1000), attention_mask[B,S] int64,
              pixel_values[B,3,224,224] float32.   Output: logits[B,S,9].
NOT RUN LOCALLY (no torch here): the first Colab run is the real test.
"""
from __future__ import annotations

import argparse
import json
import time
from pathlib import Path

MAX_LEN = 512


def mb(p: Path) -> float:
    return p.stat().st_size / 1e6


def export_fp32(model, out: Path, opset: int):
    import torch
    model.eval().to("cpu").float()
    ids = torch.randint(5, 1000, (1, MAX_LEN), dtype=torch.long)
    bbox = torch.randint(0, 1000, (1, MAX_LEN, 4), dtype=torch.long)
    bbox[..., 2:] = torch.maximum(bbox[..., 2:], bbox[..., :2])  # x1>=x0, y1>=y0
    mask = torch.ones(1, MAX_LEN, dtype=torch.long)
    pix = torch.randn(1, 3, 224, 224)
    names_in = ["input_ids", "bbox", "attention_mask", "pixel_values"]
    dyn = {"input_ids": {0: "b", 1: "s"}, "bbox": {0: "b", 1: "s"}, "attention_mask": {0: "b", 1: "s"},
           "pixel_values": {0: "b"}, "logits": {0: "b", 1: "s"}}

    class Wrap(torch.nn.Module):
        def __init__(self, m):
            super().__init__()
            self.m = m

        def forward(self, input_ids, bbox, attention_mask, pixel_values):
            return self.m(input_ids=input_ids, bbox=bbox, attention_mask=attention_mask,
                          pixel_values=pixel_values).logits

    w = Wrap(model).eval()
    last = None
    for kw in ({"dynamo": False}, {}):  # legacy TorchScript exporter first, then the torch default
        try:
            with torch.no_grad():
                torch.onnx.export(w, (ids, bbox, mask, pix), str(out), input_names=names_in,
                                  output_names=["logits"], dynamic_axes=dyn, opset_version=opset, **kw)
            print(f"exported with {kw or 'default exporter'}")
            return
        except Exception as e:  # noqa: BLE001 - report and try the next exporter
            last = e
            print(f"export with {kw or 'default'} failed: {type(e).__name__}: {str(e)[:300]}")
    raise RuntimeError(f"all exporters failed; last error: {last}")


def quantize(src: Path, dst: Path, with_embeddings: bool):
    from onnxruntime.quantization import QuantType, quantize_dynamic
    ops = ["MatMul", "Gemm"] + (["Gather"] if with_embeddings else [])
    quantize_dynamic(str(src), str(dst), weight_type=QuantType.QInt8, op_types_to_quantize=ops)


def load_docs(data_dir: Path, limit: int):
    f = data_dir / "test.json"
    docs = json.loads(f.read_text(encoding="utf-8"))
    return docs[:limit]


def build_batch(processor, data_dir: Path, doc: dict):
    from PIL import Image
    img = Image.open(data_dir / doc["image"]).convert("RGB")
    w, h = float(doc["width"]), float(doc["height"])
    words = [x["text"] for x in doc["words"]]
    boxes = []
    for x in doc["words"]:
        x0, y0, x1, y1 = x["box"]
        boxes.append([max(0, min(1000, int(1000 * x0 / w))), max(0, min(1000, int(1000 * y0 / h))),
                      max(0, min(1000, int(1000 * x1 / w))), max(0, min(1000, int(1000 * y1 / h)))])
    enc = processor(img, words, boxes=boxes, truncation=True, padding="max_length", max_length=MAX_LEN,
                    return_tensors="np")
    return {k: enc[k] for k in ("input_ids", "bbox", "attention_mask", "pixel_values")}


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--checkpoint", type=Path, required=True)
    ap.add_argument("--data-dir", type=Path, required=True, help="unzipped bundle (test.json + images/)")
    ap.add_argument("--out", type=Path, required=True)
    ap.add_argument("--opset", type=int, default=17)
    ap.add_argument("--parity-docs", type=int, default=40)
    ap.add_argument("--cap-mb", type=float, default=100.0)
    a = ap.parse_args()
    a.out.mkdir(parents=True, exist_ok=True)

    import numpy as np
    import onnxruntime as ort
    import torch
    from transformers import LayoutLMv3ForTokenClassification, LayoutLMv3Processor

    report: dict = {"cap_mb": a.cap_mb}
    model = LayoutLMv3ForTokenClassification.from_pretrained(str(a.checkpoint))
    processor = LayoutLMv3Processor.from_pretrained(str(a.checkpoint), apply_ocr=False)

    fp32, int8, int8e = a.out / "model_fp32.onnx", a.out / "model_int8.onnx", a.out / "model_int8_emb.onnx"
    export_fp32(model, fp32, a.opset)
    report["fp32_mb"] = round(mb(fp32), 1)
    quantize(fp32, int8, False)
    report["int8_mb"] = round(mb(int8), 1)
    try:
        quantize(fp32, int8e, True)
        report["int8_with_embeddings_mb"] = round(mb(int8e), 1)
    except Exception as e:  # noqa: BLE001
        report["int8_with_embeddings_error"] = f"{type(e).__name__}: {str(e)[:300]}"

    # Parity on real test docs: argmax agreement over real (non-padding) tokens, plus CPU latency.
    docs = load_docs(a.data_dir, a.parity_docs)
    sessions = {"fp32": ort.InferenceSession(str(fp32), providers=["CPUExecutionProvider"]),
                "int8": ort.InferenceSession(str(int8), providers=["CPUExecutionProvider"])}
    if int8e.exists():
        sessions["int8_emb"] = ort.InferenceSession(str(int8e), providers=["CPUExecutionProvider"])
    agree = {k: [0, 0] for k in sessions}
    ent_agree = {k: [0, 0] for k in sessions}  # agreement restricted to tokens torch labels as a field (!= O)
    lat = {k: [] for k in sessions}
    for d in docs:
        b = build_batch(processor, a.data_dir, d)
        with torch.no_grad():
            ref = model(**{k: torch.from_numpy(v) for k, v in b.items()}).logits.argmax(-1).numpy()[0]
        real = b["attention_mask"][0] == 1
        for name, s in sessions.items():
            t0 = time.perf_counter()
            lg = s.run(["logits"], b)[0]
            lat[name].append(time.perf_counter() - t0)
            pred = lg.argmax(-1)[0]
            agree[name][0] += int((pred[real] == ref[real]).sum()); agree[name][1] += int(real.sum())
            ent = real & (ref != 0)
            ent_agree[name][0] += int((pred[ent] == ref[ent]).sum()); ent_agree[name][1] += int(ent.sum())
    report["parity_docs"] = len(docs)
    report["token_agreement_vs_torch"] = {k: round(v[0] / max(1, v[1]), 4) for k, v in agree.items()}
    report["field_token_agreement_vs_torch"] = {k: round(v[0] / max(1, v[1]), 4) for k, v in ent_agree.items()}
    report["colab_cpu_latency_s"] = {k: {"median": round(float(np.median(v)), 3),
                                         "p95": round(float(np.percentile(v, 95)), 3)} for k, v in lat.items()}
    report["note"] = ("Colab CPU latency is NOT the target machine; Phase 3 benchmark runs locally. "
                      "Agreement is vs the PyTorch FP32 model, not an F1; re-evaluate F1 before shipping.")
    for k in ("int8", "int8_with_embeddings"):
        mbv = report.get(f"{k}_mb")
        if mbv is not None and mbv > a.cap_mb:
            report.setdefault("warnings", []).append(f"{k} is {mbv} MB > cap {a.cap_mb} MB")
    (a.out / "export_report.json").write_text(json.dumps(report, indent=1), encoding="utf-8")
    print(json.dumps(report, indent=1))


if __name__ == "__main__":
    main()
