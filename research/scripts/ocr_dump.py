"""Run PaddleOCR over document images and dump words+boxes per image, same shape as Pine.Core.Ai.OcrWord.

Output <out>/<id>.json:
  {"id", "image", "width", "height",
   "words": [{"text", "confidence", "box": {"x","y","width","height"}}]}
Needs: pip install paddleocr paddlepaddle   (not required for the rest of Fase 1)
Note: PaddleOCR returns TEXT LINES (detector boxes), like Pine's PaddleOcrEngine, not single words;
the OcrWord contract in Pine is the same granularity.
Usage: python scripts/ocr_dump.py --split test [--source all] [--out data/ocr] [--limit N] [--lang en]
"""
from __future__ import annotations

import argparse
import json
import time
from pathlib import Path

from common import DATA, PROCESSED, SPLITS_PATH, load_docs


def poly_to_box(poly) -> dict:
    xs = [float(p[0]) for p in poly]
    ys = [float(p[1]) for p in poly]
    return {"x": min(xs), "y": min(ys), "width": max(xs) - min(xs), "height": max(ys) - min(ys)}


def make_engine(lang: str):
    from paddleocr import PaddleOCR
    try:  # PaddleOCR 3.x
        return PaddleOCR(lang=lang, use_doc_orientation_classify=False, use_doc_unwarping=False,
                         use_textline_orientation=False, enable_mkldnn=False)  # oneDNN PIR crashes on CPU/Windows with paddle 3.3.1
    except TypeError:  # 2.x
        return PaddleOCR(lang=lang, use_angle_cls=False, show_log=False)


def run_ocr(engine, image_path: str) -> list[dict]:
    words = []
    if hasattr(engine, "predict"):  # 3.x
        for res in engine.predict(image_path):
            r = res.json["res"] if hasattr(res, "json") else res
            for text, score, poly in zip(r["rec_texts"], r["rec_scores"], r["rec_polys"]):
                words.append({"text": text, "confidence": float(score), "box": poly_to_box(poly)})
    else:  # 2.x
        out = engine.ocr(image_path, cls=False)
        for line in (out[0] or []):
            poly, (text, score) = line
            words.append({"text": text, "confidence": float(score), "box": poly_to_box(poly)})
    return words


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--split", default="test", choices=["train", "val", "test", "all"])
    ap.add_argument("--source", default="all", choices=["all", "cord", "sroie"])
    ap.add_argument("--out", default=str(DATA / "ocr"))
    ap.add_argument("--limit", type=int, default=0)
    ap.add_argument("--lang", default="en")
    args = ap.parse_args()
    ids = None
    if args.split != "all":
        ids = set(json.loads(SPLITS_PATH.read_text(encoding="utf-8"))["splits"][args.split])
    out = Path(args.out)
    out.mkdir(parents=True, exist_ok=True)
    engine = make_engine(args.lang)
    n = 0
    for d in load_docs(args.source):
        if ids is not None and d["id"] not in ids:
            continue
        target = out / f"{d['id']}.json"
        if target.exists():  # idempotent: existing outputs do not count against --limit
            continue
        if args.limit and n >= args.limit:
            break
        img = PROCESSED / d["source"] / d["image"]
        t0 = time.perf_counter()
        words = run_ocr(engine, str(img))
        dt = time.perf_counter() - t0
        target.write_text(json.dumps({"id": d["id"], "image": str(img), "width": d["width"],
                                      "height": d["height"], "ocr_seconds": round(dt, 3), "words": words}), encoding="utf-8")
        n += 1
    print(f"ocr_dump: {n} images -> {out}")


if __name__ == "__main__":
    main()
