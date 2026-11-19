"""Image quality tags (SKELETON for Fase 5 slice analysis; thresholds are placeholders, NOT calibrated).

  blur         : variance of the Laplacian (low = blurry)
  illumination : mean luminance + p5/p95 spread (dark / bright / low_contrast / ok)
  layout       : naive geometry heuristic (long_narrow / wide / standard / small) from aspect ratio & size
Usage: python scripts/quality_tags.py --split test --out reports/quality_tags.json
"""
from __future__ import annotations

import argparse
import json

import numpy as np

BLUR_THRESHOLD = 100.0     # PLACEHOLDER: tune on the real eval set in Fase 5
DARK_MEAN = 80.0
BRIGHT_MEAN = 200.0
LOW_CONTRAST_SPREAD = 60.0


def _gray(img: np.ndarray) -> np.ndarray:
    import cv2
    if img.ndim == 2:
        return img
    return cv2.cvtColor(img, cv2.COLOR_BGR2GRAY)


def blur_score(img: np.ndarray) -> float:
    import cv2
    return float(cv2.Laplacian(_gray(img), cv2.CV_64F).var())


def illumination(img: np.ndarray) -> dict:
    g = _gray(img)
    mean = float(g.mean())
    p5, p95 = (float(v) for v in np.percentile(g, [5, 95]))
    tag = "dark" if mean < DARK_MEAN else "bright" if mean > BRIGHT_MEAN else \
        "low_contrast" if (p95 - p5) < LOW_CONTRAST_SPREAD else "ok"
    return {"mean": mean, "p5": p5, "p95": p95, "tag": tag}


def layout_tag(h: int, w: int) -> str:
    if max(h, w) < 600:
        return "small"
    ratio = h / w
    return "long_narrow" if ratio > 2.2 else "wide" if ratio < 0.9 else "standard"


def tag_image(img: np.ndarray) -> dict:
    h, w = img.shape[:2]
    b = blur_score(img)
    return {"blur_score": b, "blur_tag": "blurry" if b < BLUR_THRESHOLD else "sharp",
            "illumination": illumination(img), "layout": layout_tag(h, w), "width": w, "height": h}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--split", default="test", choices=["train", "val", "test", "all"])
    ap.add_argument("--source", default="all")
    ap.add_argument("--out", default="reports/quality_tags.json")
    args = ap.parse_args()
    import cv2
    from pathlib import Path
    from common import PROCESSED, SPLITS_PATH, load_docs
    ids = None
    if args.split != "all":
        ids = set(json.loads(SPLITS_PATH.read_text(encoding="utf-8"))["splits"][args.split])
    res = {}
    for d in load_docs(args.source):
        if ids is not None and d["id"] not in ids:
            continue
        img = cv2.imdecode(np.fromfile(str(PROCESSED / d["source"] / d["image"]), dtype=np.uint8),
                           cv2.IMREAD_COLOR)
        res[d["id"]] = tag_image(img)
    Path(args.out).parent.mkdir(parents=True, exist_ok=True)
    Path(args.out).write_text(json.dumps(res, indent=1), encoding="utf-8")
    print(f"tagged {len(res)} images -> {args.out}")


if __name__ == "__main__":
    main()
