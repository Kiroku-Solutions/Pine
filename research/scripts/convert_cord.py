"""CORD-v2 (naver-clova-ix/cord-v2) -> FUNSD-BIO with 4 fields. CC BY 4.0.

Mapping (only the fields CORD really has):
  total.total_price -> TOTAL
  sub_total.tax_price (and total.tax_price if present) -> TAX
MERCHANT and DATE do NOT exist in CORD annotations: left null, words stay 'O'.
Ground truth value comes from gt_parse (not re-assembled from words).
"""
from __future__ import annotations

import argparse
import io
import json
from pathlib import Path

from common import DATA, PROCESSED, bio, make_doc, quad_to_box, sha256_bytes, write_doc

CATEGORY_MAP = {
    "total.total_price": "TOTAL",
    "total.tax_price": "TAX",
    "sub_total.tax_price": "TAX",
}


def _gt_value(gt_parse: dict, *paths):
    for path in paths:
        cur = gt_parse
        for p in path.split("."):
            cur = cur.get(p) if isinstance(cur, dict) else None
        if isinstance(cur, list):
            cur = cur[0] if cur else None
        if isinstance(cur, str) and cur.strip():
            return cur.strip()
    return None


def convert_record(ground_truth: dict, doc_id: str, image_rel: str, width: int, height: int,
                   image_sha256: str | None = None) -> dict:
    words = []
    for line in ground_truth.get("valid_line", []):
        field = CATEGORY_MAP.get(line.get("category"))
        n_in_field = 0
        for w in line.get("words", []):
            text = w.get("text", "")
            if not text.strip():
                continue
            label = "O"
            if field and not w.get("is_key", False):
                label = bio(field, n_in_field)
                n_in_field += 1
            words.append({"text": text, "box": quad_to_box(w["quad"]), "label": label})
    parse = ground_truth.get("gt_parse", {})
    gt = {
        "TOTAL": _gt_value(parse, "total.total_price"),
        "TAX": _gt_value(parse, "total.tax_price", "sub_total.tax_price"),
        "MERCHANT": None,
        "DATE": None,
    }
    return make_doc(doc_id, "cord", image_rel, width, height, words, gt, image_sha256)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--raw", default=str(DATA / "raw" / "cord-v2"))
    ap.add_argument("--out", default=str(PROCESSED / "cord"))
    ap.add_argument("--limit", type=int, default=0)
    args = ap.parse_args()
    from datasets import load_from_disk  # lazy: unit tests do not need it

    ds = load_from_disk(args.raw)
    out = Path(args.out)
    (out / "images").mkdir(parents=True, exist_ok=True)
    n = 0
    for split in ds:  # upstream splits are merged; our own frozen split is made by make_splits.py
        for i, ex in enumerate(ds[split]):
            if args.limit and n >= args.limit:
                break
            doc_id = f"cord_{split}_{i:05d}"
            if (out / "labels" / f"{doc_id}.json").exists():
                n += 1
                continue
            img = ex["image"].convert("RGB")
            buf = io.BytesIO()
            img.save(buf, format="PNG")
            (out / "images" / f"{doc_id}.png").write_bytes(buf.getvalue())
            gt = json.loads(ex["ground_truth"])
            write_doc(out / "labels", convert_record(
                gt, doc_id, f"images/{doc_id}.png", img.width, img.height, sha256_bytes(buf.getvalue())))
            n += 1
    print(f"cord: {n} docs -> {out}")


if __name__ == "__main__":
    main()
