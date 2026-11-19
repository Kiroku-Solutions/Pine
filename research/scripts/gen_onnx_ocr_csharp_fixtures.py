"""Golden fixtures for the C# ports of the ONNX OCR post-processing (docs/65-Onnx-Ocr-Design.md, B1).

Runs research/scripts/onnx_ocr_reference.py (the Python reference) and writes, into
tests/Pine.Tests.Unit/Ai/Fixtures/:

  onnx_ocr_receipt_synthetic.png  a rendered receipt (no dataset image, so no licence question)
  onnx_ocr_db_fixtures.json       DB probability maps (uint8-quantised, base64) + expected boxes
  onnx_ocr_ctc_fixtures.json      sparse CTC probability matrices + expected text/confidence
  onnx_ocr_receipt_expected.json  the reference engine's lines for the synthetic receipt

Usage (from the repo root, with the research venv):
  research/.venv/Scripts/python.exe research/scripts/gen_onnx_ocr_csharp_fixtures.py <dir with det.onnx rec.onnx dict_latin_v5.txt>
"""
from __future__ import annotations

import base64
import json
import sys
from pathlib import Path

import numpy as np
import onnxruntime as ort
from PIL import Image, ImageDraw, ImageFont

sys.path.insert(0, str(Path(__file__).resolve().parent))
import onnx_ocr_reference as R  # noqa: E402

OUT = Path(__file__).resolve().parents[2] / "tests" / "Pine.Tests.Unit" / "Ai" / "Fixtures"
LINES = [
    ("PINE MARKET", 30, True),
    ("Calle Mayor 12, Madrid", 20, False),
    ("2024-03-14   18:42", 20, False),
    ("Leche entera 1L        1.29", 20, False),
    ("Pan de molde           2.10", 20, False),
    ("Manzanas 1.5kg         3.45", 20, False),
    ("TOTAL                  6.84", 26, True),
    ("Gracias por su compra", 20, False),
]


def render() -> Image.Image:
    img = Image.new("RGB", (520, 440), "white")
    d = ImageDraw.Draw(img)
    y = 20
    for text, size, bold in LINES:
        f = ImageFont.truetype("C:/Windows/Fonts/" + ("courbd.ttf" if bold else "cour.ttf"), size)
        d.text((24, y), text, fill=(15, 15, 15), font=f)
        y += size + 24
    return img


def quant(prob: np.ndarray) -> np.ndarray:
    return np.clip(np.round(prob * 255), 0, 255).astype(np.uint8)


def db_case(name, q: np.ndarray, orig_h, orig_w, **kw):
    prob = (q.astype(np.float32) / 255.0)
    rh, rw = prob.shape
    boxes, scores = R.det_postprocess(prob, (orig_h, orig_w, rh, rw), **kw)
    return {
        "name": name, "mapWidth": rw, "mapHeight": rh, "origWidth": orig_w, "origHeight": orig_h,
        "thresh": kw.get("thresh", 0.3), "boxThresh": kw.get("box_thresh", 0.6), "unclip": kw.get("unclip", 1.5),
        "map": base64.b64encode(q.tobytes()).decode("ascii"),
        "boxes": [b.tolist() for b in boxes], "scores": scores,
    }


def main(model_dir: str):
    md = Path(model_dir)
    OUT.mkdir(parents=True, exist_ok=True)
    img = render()
    img.save(OUT / "onnx_ocr_receipt_synthetic.png")
    bgr = np.asarray(img)[:, :, ::-1].copy()

    det = ort.InferenceSession(str(md / "det.onnx"), providers=["CPUExecutionProvider"])
    rec = ort.InferenceSession(str(md / "rec.onnx"), providers=["CPUExecutionProvider"])
    charset = R.load_charset(md / "dict_latin_v5.txt", True)

    # ---- DB: a real map (the synthetic receipt downscaled so the fixture stays small) + hand-made maps
    small = img.resize((192, 168), Image.BILINEAR)
    sb = np.asarray(small)[:, :, ::-1].copy()
    x, shape = R.det_preprocess(sb)
    prob = det.run(None, {det.get_inputs()[0].name: x})[0][0, 0]
    q = quant(prob)
    cases = [
        db_case("receipt_small_same_size", q, shape[2], shape[3], thresh=0.3, box_thresh=0.7, unclip=1.5),
        db_case("receipt_small_scaled_x3", q, shape[2] * 3, shape[3] * 3, thresh=0.3, box_thresh=0.7, unclip=1.5),
        db_case("receipt_small_low_box_thresh", q, shape[2], shape[3], thresh=0.3, box_thresh=0.3, unclip=1.5),
    ]
    # a tilted bar, a tall thin bar, a tiny blob (dropped) and a weak bar (below box thresh)
    m = np.zeros((96, 160), dtype=np.float32)
    yy, xx = np.mgrid[0:96, 0:160]
    ang = np.deg2rad(12)
    u = (xx - 80) * np.cos(ang) + (yy - 30) * np.sin(ang)
    v = -(xx - 80) * np.sin(ang) + (yy - 30) * np.cos(ang)
    m[(np.abs(u) < 50) & (np.abs(v) < 7)] = 0.95
    m[60:90, 20:26] = 0.9
    m[80:82, 100:102] = 0.95
    m[50:58, 110:150] = 0.45
    cases.append(db_case("synthetic_tilted_tall_tiny_weak", quant(m), 192, 320, thresh=0.3, box_thresh=0.6, unclip=1.5))
    (OUT / "onnx_ocr_db_fixtures.json").write_text(json.dumps({"cases": cases}), encoding="utf-8")

    # ---- reference engine on the full synthetic receipt (integration expectations + CTC cases)
    x, shape = R.det_preprocess(bgr)
    prob = det.run(None, {det.get_inputs()[0].name: x})[0][0, 0]
    boxes, dscores = R.det_postprocess(prob, shape, box_thresh=0.7)
    words, ctc = [], []
    for i, (qd, ds) in enumerate(zip(boxes, dscores)):
        crop = R.crop_line(bgr, qd)
        p = rec.run(None, {rec.get_inputs()[0].name: R.rec_preprocess(crop)})[0][0]
        text, conf = R.ctc_decode(p, charset)
        words.append({"text": text, "confidence": conf, "box": R.poly_to_box(qd)})
        if i < 4:
            sparse = []
            for t in range(p.shape[0]):
                nz = np.nonzero(p[t] > 1e-3)[0]
                sparse.append([[int(k), float(p[t, k])] for k in nz])
            ctc.append({"steps": int(p.shape[0]), "classes": int(p.shape[1]), "sparse": sparse, "text": text, "confidence": conf})
    (OUT / "onnx_ocr_ctc_fixtures.json").write_text(json.dumps({"cases": ctc}, ensure_ascii=False), encoding="utf-8")
    (OUT / "onnx_ocr_receipt_expected.json").write_text(
        json.dumps({"width": img.width, "height": img.height, "words": words}, ensure_ascii=False, indent=1), encoding="utf-8")
    print(len(cases), "db cases;", len(ctc), "ctc cases;", len(words), "lines")
    for w in words:
        print(w["text"])


if __name__ == "__main__":
    main(sys.argv[1])
