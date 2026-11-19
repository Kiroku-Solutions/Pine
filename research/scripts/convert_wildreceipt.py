"""WildReceipt (Sun et al. 2021, OpenMMLab wildreceipt.tar) -> FUNSD-BIO, 4 fields.

Source format (train.txt / test.txt): one JSON per line
  {"file_name": "image_files/.../x.jpeg", "height": H, "width": W,
   "annotations": [{"box": [x1,y1,x2,y2,x3,y3,x4,y4], "text": "...", "label": int}, ...]}
class_list.txt maps ids to names ("<id> <name>"). Granularity = TEXT LINE (like SROIE).
Mapping: Store_name_value -> MERCHANT, Date_value -> DATE, Total_value -> TOTAL, Tax_value -> TAX.
All other classes (keys, address, product, phone, time, ...) -> 'O'. Only the FIRST contiguous span per
field is kept. Official split (train/test) is kept and written to splits_extra.json (never splits.json),
after dedup by image hash (a duplicate image keeps only its first occurrence, so no cross-split leakage).
"""
from __future__ import annotations

import argparse
import json
import tarfile
from pathlib import Path

from common import DATA, PROCESSED, RESEARCH, bio, make_doc, sha256_bytes, write_doc

NAME_MAP = {"Store_name_value": "MERCHANT", "Date_value": "DATE",
            "Total_value": "TOTAL", "Tax_value": "TAX"}
DEFAULT_CLASSES = {1: "Store_name_value", 5: "Date_value", 17: "Tax_value", 23: "Total_value"}  # fallback only


def parse_class_list(text: str) -> dict[int, str]:
    out = {}
    for line in text.splitlines():
        parts = line.split()
        if len(parts) >= 2 and parts[0].isdigit():
            out[int(parts[0])] = parts[1]
    return out


def label_to_field(class_names: dict[int, str]) -> dict[int, str]:
    return {i: NAME_MAP[n] for i, n in class_names.items() if n in NAME_MAP}


def poly_to_box(poly) -> list[int]:
    xs, ys = poly[0::2], poly[1::2]
    return [int(min(xs)), int(min(ys)), int(max(xs)), int(max(ys))]


def convert_record(rec: dict, doc_id: str, image_rel: str, id_to_field: dict[int, str],
                   image_sha256: str | None = None) -> dict:
    words, spans, closed, prev = [], {}, set(), None
    for a in rec["annotations"]:
        text = a.get("text", "")
        if not text.strip():
            continue
        field = id_to_field.get(a.get("label"))
        label = "O"
        if field:
            if prev != field and field in spans:
                closed.add(field)
            if field not in closed:
                cur = spans.setdefault(field, [])
                label = bio(field, len(cur))
                cur.append(text.strip())
        prev = field
        words.append({"text": text, "box": poly_to_box(a["box"]), "label": label})
    gt = {f: " ".join(v) for f, v in spans.items()}
    return make_doc(doc_id, "wildreceipt", image_rel, rec["width"], rec["height"], words, gt, image_sha256)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--tar", default=str(DATA / "raw" / "wildreceipt" / "wildreceipt.tar"))
    ap.add_argument("--out", default=str(PROCESSED / "wildreceipt"))
    ap.add_argument("--splits-extra", default=str(RESEARCH / "splits_extra.json"))
    ap.add_argument("--limit", type=int, default=0)
    args = ap.parse_args()
    out = Path(args.out)
    (out / "images").mkdir(parents=True, exist_ok=True)
    tf = tarfile.open(args.tar)
    names = {m.name.split("wildreceipt/", 1)[-1]: m for m in tf.getmembers() if m.isfile()}
    classes = parse_class_list(tf.extractfile(names["class_list.txt"]).read().decode("utf-8"))
    id_to_field = label_to_field(classes)
    seen: dict[str, str] = {}
    split_ids: dict[str, list[str]] = {"train": [], "test": []}
    dup = n = 0
    for split in ("train", "test"):
        for i, line in enumerate(tf.extractfile(names[f"{split}.txt"]).read().decode("utf-8").splitlines()):
            if not line.strip():
                continue
            if args.limit and n >= args.limit:
                break
            rec = json.loads(line)
            img = tf.extractfile(names[rec["file_name"]]).read()
            sha = sha256_bytes(img)
            if sha in seen:
                dup += 1
                continue
            doc_id = f"wildreceipt_{split}_{i:05d}"
            seen[sha] = doc_id
            ext = Path(rec["file_name"]).suffix.lower() or ".jpg"
            (out / "images" / f"{doc_id}{ext}").write_bytes(img)
            write_doc(out / "labels", convert_record(rec, doc_id, f"images/{doc_id}{ext}", id_to_field, sha))
            split_ids[split].append(doc_id)
            n += 1
    Path(args.splits_extra).write_text(json.dumps(
        {"note": "official WildReceipt train/test, deduped by image sha256; separate from splits.json",
         "sources": {"wildreceipt": split_ids}}, indent=1), encoding="utf-8")
    print(f"wildreceipt: {n} docs ({dup} duplicate images skipped) -> {out}; classes mapped: {id_to_field}")


if __name__ == "__main__":
    main()
