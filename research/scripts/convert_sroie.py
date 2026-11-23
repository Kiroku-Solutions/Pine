"""SROIE (HF mirror 'arvindrajan92/sroie_document_understanding') -> FUNSD-BIO, 3 fields.

Mirror schema: image + ocr[ {box: 4 corner points in PIXELS, label, text} ] -- one entry per TEXT LINE
(not per word), same granularity as PaddleOCR output. Labels: company, date, total, address,
line_description, line_total, other.

Mapping: company -> MERCHANT, date -> DATE, total -> TOTAL. Everything else (address, line_*, other) -> 'O'.
SROIE has NO tax. Ground truth = text of the labelled line(s), joined by space. Only the FIRST contiguous
span per field is kept; later spans are relabelled O. DATE ground truth may include the time of day
("25/12/2018 8:13:39 PM"); the evaluator reduces dates to ISO yyyy-mm-dd.
"""
from __future__ import annotations

import argparse
import io
from pathlib import Path

from common import DATA, PROCESSED, bio, make_doc, sha256_bytes, write_doc

LABEL_MAP = {"company": "MERCHANT", "date": "DATE", "total": "TOTAL"}


def points_to_box(points) -> list[int]:
    xs = [p[0] for p in points]
    ys = [p[1] for p in points]
    return [int(min(xs)), int(min(ys)), int(max(xs)), int(max(ys))]


def convert_record(ocr: list[dict], doc_id: str, image_rel: str, width: int, height: int,
                   image_sha256: str | None = None) -> dict:
    out_words = []
    spans: dict[str, list[str]] = {}
    closed: set[str] = set()
    prev_field = None
    for line in ocr:
        text = line.get("text", "")
        if not text.strip():
            continue
        field = LABEL_MAP.get(line.get("label"))
        label = "O"
        if field:
            if prev_field != field and field in spans:
                closed.add(field)  # second, separate span of the same field: ignore it
            if field not in closed:
                cur = spans.setdefault(field, [])
                label = bio(field, len(cur))
                cur.append(text.strip())
        prev_field = field
        out_words.append({"text": text, "box": points_to_box(line["box"]), "label": label})
    gt = {f: " ".join(v) for f, v in spans.items()}
    return make_doc(doc_id, "sroie", image_rel, width, height, out_words, gt, image_sha256)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--raw", default=str(DATA / "raw" / "sroie"))
    ap.add_argument("--out", default=str(PROCESSED / "sroie"))
    ap.add_argument("--limit", type=int, default=0)
    args = ap.parse_args()
    from datasets import load_from_disk

    ds = load_from_disk(args.raw)
    out = Path(args.out)
    (out / "images").mkdir(parents=True, exist_ok=True)
    n = 0
    for split in ds:
        for i, ex in enumerate(ds[split]):
            if args.limit and n >= args.limit:
                break
            doc_id = f"sroie_{split}_{i:05d}"
            n += 1
            if (out / "labels" / f"{doc_id}.json").exists():
                continue
            img = ex["image"].convert("RGB")
            buf = io.BytesIO()
            img.save(buf, format="PNG")
            (out / "images" / f"{doc_id}.png").write_bytes(buf.getvalue())
            write_doc(out / "labels", convert_record(
                ex["ocr"], doc_id, f"images/{doc_id}.png", img.width, img.height,
                sha256_bytes(buf.getvalue())))
    print(f"sroie: {n} docs -> {out}")


if __name__ == "__main__":
    main()
