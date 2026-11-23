"""Pack images + FUNSD-BIO JSON of the train/val/test splits into one small zip for Google Drive.

Layout inside the zip:  train/val/test/test_wildreceipt.json + manifest.json + subset_4fields.json (list of docs, 'image' -> images/<id>.jpg)
                        images/<id>.jpg  (downscaled, JPEG)  and the colab scripts (code/).
Images are downscaled (LayoutLMv3 resizes to 224x224 anyway; boxes are normalized by the ORIGINAL
width/height stored in the JSON, so aspect-preserving downscale keeps boxes valid).

Usage:  python prepare_colab_bundle.py --out pine_bundle.zip [--max-side 1024 --quality 85]
        python prepare_colab_bundle.py --limit 20 --out /tmp/x.zip      # dry run, few docs/split
"""
from __future__ import annotations

import argparse
import io
import json
import sys
import zipfile
from collections import Counter
from pathlib import Path

RESEARCH = Path(__file__).resolve().parent.parent
PROCESSED = RESEARCH / "data" / "processed"
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from colab_splits import CARVE_FRAC, CARVE_SEED, build_colab_splits  # noqa: E402
from layout_utils import SOURCE_FIELDS, annotated_fields, has_all_fields  # noqa: E402
CODE_FILES = ["layout_utils.py", "colab_splits.py", "train_layoutlmv3.py", "export_onnx_int8.py", "train_layoutlmv3.ipynb"]


def index_docs(processed: Path) -> dict[str, Path]:
    idx = {}
    for f in sorted(processed.glob("*/labels/*.json")):
        idx[f.stem] = f
    return idx


def compress_image(path: Path, max_side: int, quality: int) -> bytes:
    from PIL import Image
    with Image.open(path) as im:
        im = im.convert("RGB")
        scale = max_side / max(im.size)
        if scale < 1:
            im = im.resize((round(im.width * scale), round(im.height * scale)), Image.LANCZOS)
        buf = io.BytesIO()
        im.save(buf, "JPEG", quality=quality, optimize=True)
        return buf.getvalue()


def load_wild_train_docs(idx, ids):
    return {i: json.loads(idx[i].read_text(encoding="utf-8")) for i in ids if i in idx}


def build(out: Path, splits_path: Path, processed: Path, max_side: int, quality: int, limit: int | None,
          extra_path: Path | None = None, splits_out: Path | None = None):
    main_splits = json.loads(splits_path.read_text(encoding="utf-8"))["splits"]
    wild = json.loads(extra_path.read_text(encoding="utf-8"))["sources"]["wildreceipt"] if extra_path else None
    idx = index_docs(processed)
    if wild:
        wd = load_wild_train_docs(idx, wild["train"])
        splits = build_colab_splits(main_splits, wild, wd)
        if splits_out:
            splits_out.write_text(json.dumps({"seed": CARVE_SEED, "carve_frac": CARVE_FRAC, "splits": splits},
                                             indent=1), encoding="utf-8")
    else:
        splits = dict(main_splits)
    full = sum(len(v) for v in splits.values())
    total_docs, total_img, missing = 0, 0, []
    manifest = {"seed": CARVE_SEED, "carve_frac": CARVE_FRAC, "max_side": max_side, "quality": quality,
                "limit": limit, "splits": {}, "counts": {}, "annotated_fields_by_source": SOURCE_FIELDS}
    subset4: dict[str, list[str]] = {}
    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
        for split, all_ids in splits.items():
            ids = _sample_across_sources(all_ids, limit) if limit else all_ids  # dry run: every source visible
            docs = []
            for i, did in enumerate(ids):
                jf = idx.get(did)
                if jf is None:
                    missing.append(did)
                    continue
                doc = json.loads(jf.read_text(encoding="utf-8"))
                img = jf.parent.parent / doc["image"]
                if not img.exists():
                    missing.append(did)
                    continue
                data = compress_image(img, max_side, quality)
                name = f"images/{did}.jpg"
                z.writestr(name, data, compress_type=zipfile.ZIP_STORED)  # JPEG already compressed
                doc["image"] = name
                doc["annotated_fields"] = annotated_fields(doc)
                docs.append(doc)
                total_img += len(data)
                if (i + 1) % 200 == 0:
                    print(f"  {split}: {i + 1}/{len(ids)}", file=sys.stderr)
            z.writestr(f"{split}.json", json.dumps(docs, ensure_ascii=False))
            subset4[split] = [d["id"] for d in docs if has_all_fields(d)]
            by_src, by_field = Counter(d["source"] for d in docs), {}
            for d in docs:
                for f in d["annotated_fields"]:
                    by_field.setdefault(f, Counter())[d["source"]] += 1
            manifest["splits"][split] = [d["id"] for d in docs]
            manifest["counts"][split] = {
                "docs": len(docs), "by_source": dict(by_src),
                "docs_annotating_field": {f: sum(c.values()) for f, c in by_field.items()},
                "docs_annotating_field_by_source": {f: dict(c) for f, c in by_field.items()},
                "docs_with_all_4_fields": len(subset4[split])}
            total_docs += len(docs)
        manifest["subset_4fields"] = subset4
        z.writestr("subset_4fields.json", json.dumps(subset4))
        manifest["missing"] = missing
        z.writestr("manifest.json", json.dumps(manifest, indent=1, ensure_ascii=False))
        for name in CODE_FILES:
            p = HERE / name
            if p.exists():
                z.write(p, f"code/{name}")
    size = out.stat().st_size
    print(json.dumps(manifest["counts"], indent=1))
    print(f"missing: {len(missing)}")
    print(f"bundle: {out} = {size / 1e6:.1f} MB for {total_docs} docs ({size / max(total_docs, 1) / 1e3:.0f} KB/doc)")
    if limit:
        print(f"estimated full bundle ({full} docs): ~{size / max(total_docs, 1) * full / 1e6:.0f} MB")
    return size


def _sample_across_sources(ids, limit):
    """Dry-run helper: up to `limit` ids split evenly across id prefixes (source)."""
    by = {}
    for i in ids:
        by.setdefault(i.split("_", 1)[0], []).append(i)
    per = max(1, limit // max(len(by), 1))
    return [i for v in by.values() for i in v[:per]]


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--out", type=Path, default=HERE / "pine_bundle.zip")
    ap.add_argument("--splits", type=Path, default=RESEARCH / "splits.json")
    ap.add_argument("--splits-extra", type=Path, default=RESEARCH / "splits_extra.json",
                    help="WildReceipt official split; pass '' to build the old CORD+SROIE-only bundle")
    ap.add_argument("--splits-out", type=Path, default=RESEARCH / "splits_colab.json",
                    help="where the final split ids are also written (traceability)")
    ap.add_argument("--processed", type=Path, default=PROCESSED)
    ap.add_argument("--max-side", type=int, default=1024)
    ap.add_argument("--quality", type=int, default=85)
    ap.add_argument("--limit", type=int, default=None, help="max docs per split (dry run)")
    a = ap.parse_args()
    build(a.out, a.splits, a.processed, a.max_side, a.quality, a.limit,
          a.splits_extra or None, None if a.limit else a.splits_out)


if __name__ == "__main__":
    main()
