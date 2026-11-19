"""Prepare a Roboflow export: drop negatives, dedup by perceptual hash, crop documents, write manifest + frozen splits.

Negatives: an image is dropped if it has tag 'Negative-Sample' (only when the export carries tags; Roboflow COCO
exports usually do NOT) OR has no boxes. Absence of boxes is the reliable criterion; the reason is kept in `dropped`.
Dedup: 64-bit DCT phash; images within --hamming bits form one group (union-find). A whole group lives in ONE split
(priority test > valid > train if Roboflow put members in different splits) so near-duplicates never leak across splits.
Splits: Roboflow train/valid/test are respected; with no split info, groups go 80/10/10 by seeded hash.
data/roboflow/splits.json is FROZEN: if it exists it is reused and never regenerated.
Usage: python scripts/roboflow_prepare.py --export data/roboflow/raw [--margin 0.03] [--hamming 6] [--out data/roboflow]
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path

from roboflow_common import NEG_TAG, ROBOFLOW_DIR, hamming, imread, imwrite, is_negative_tags, phash, read_export

PRIORITY = {"test": 0, "valid": 1, "train": 2}


def crop_box(img, box, margin):
    h, w = img.shape[:2]
    x, y, bw, bh = box
    mx, my = bw * margin, bh * margin
    x0, y0 = max(0, int(round(x - mx))), max(0, int(round(y - my)))
    x1, y1 = min(w, int(round(x + bw + mx))), min(h, int(round(y + bh + my)))
    if x1 - x0 < 2 or y1 - y0 < 2:
        return None
    return img[y0:y1, x0:x1], [x0, y0, x1 - x0, y1 - y0]


def group_by_hash(hashes: list[int], thr: int) -> list[int]:
    parent = list(range(len(hashes)))

    def find(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i
    for i in range(len(hashes)):
        for j in range(i):
            if hamming(hashes[i], hashes[j]) <= thr:
                parent[find(i)] = find(j)
    return [find(i) for i in range(len(hashes))]


def prepare(export: Path, out: Path, margin=0.03, thr=6, seed=42, fmt="auto") -> dict:
    recs = read_export(export, fmt)
    kept, dropped = [], []
    for r in recs:
        if is_negative_tags(r["tags"]):
            dropped.append({"name": r["name"], "reason": NEG_TAG + " tag"})
        elif not r["boxes"]:
            dropped.append({"name": r["name"], "reason": "no boxes"})
        else:
            kept.append(r)
    for r in kept:
        r["img"] = imread(r["path"])
        r["hash"] = phash(r["img"])
    gid: dict = {}
    for r, rt in zip(kept, group_by_hash([r["hash"] for r in kept], thr)):
        r["group"] = gid.setdefault(rt, f"g{len(gid):05d}")
        r["id"] = hashlib.sha1(((r["split"] or "") + "/" + r["name"]).encode()).hexdigest()[:10]

    sp_path = out / "splits.json"
    frozen = None
    id2split: dict = {}
    grp_split: dict = {}
    if sp_path.exists():
        frozen = json.loads(sp_path.read_text(encoding="utf-8"))
        print(f"splits.json exists: reusing frozen splits ({sp_path})")
        id2split = {i: s for s, ids in frozen["splits"].items() for i in ids}
        grp_split = frozen.get("group_split", {})
    else:
        members: dict[str, list] = {}
        for r in kept:
            members.setdefault(r["group"], []).append(r)
        for g, rs in members.items():
            known = [r["split"] for r in rs if r["split"]]
            if known:
                grp_split[g] = min(known, key=PRIORITY.get)
            else:
                v = int(hashlib.sha1(f"{seed}:{g}".encode()).hexdigest(), 16) % 100
                grp_split[g] = "train" if v < 80 else "valid" if v < 90 else "test"
    manifest = []
    splits: dict = {"train": [], "valid": [], "test": []}
    for r in kept:
        for k, box in enumerate(r["boxes"]):
            cid = f"{r['id']}_{k}"
            sp = id2split.get(cid) or grp_split.get(r["group"]) or "train"
            c = crop_box(r["img"], box, margin)
            if c is None:
                continue
            crop, abs_box = c
            rel = f"crops/{sp}/{cid}.jpg"
            imwrite(out / rel, crop)
            manifest.append({"id": cid, "source_image": r["name"], "source_id": r["id"], "group": r["group"],
                             "split": sp, "roboflow_split": r["split"], "crop": rel, "box_xywh": abs_box,
                             "phash": f"{r['hash']:016x}"})
            splits[sp].append(cid)
        r.pop("img")
    if frozen is None:
        sp_path.parent.mkdir(parents=True, exist_ok=True)
        sp_path.write_text(json.dumps({"seed": seed, "hamming": thr, "splits": splits, "group_split": grp_split},
                                      indent=1), encoding="utf-8")
        print(f"froze splits -> {sp_path}")
    n_groups = len({r["group"] for r in kept})
    (out / "manifest.json").write_text(json.dumps(
        {"margin": margin, "hamming": thr, "n_input": len(recs), "n_kept": len(kept), "n_groups": n_groups,
         "dropped": dropped, "items": manifest}, indent=1), encoding="utf-8")
    print(f"input={len(recs)} dropped_negatives={len(dropped)} kept={len(kept)} crops={len(manifest)} "
          f"groups={n_groups} -> {out / 'manifest.json'}")
    return {"manifest": manifest, "splits": splits, "dropped": dropped}


def main(argv=None):
    ap = argparse.ArgumentParser()
    ap.add_argument("--export", type=Path, required=True)
    ap.add_argument("--out", type=Path, default=ROBOFLOW_DIR)
    ap.add_argument("--format", choices=["auto", "coco", "yolo"], default="auto")
    ap.add_argument("--margin", type=float, default=0.03, help="fraction of box w/h added on each side")
    ap.add_argument("--hamming", type=int, default=6, help="max phash Hamming distance (of 64) for same group")
    ap.add_argument("--seed", type=int, default=42)
    a = ap.parse_args(argv)
    prepare(a.export, a.out, a.margin, a.hamming, a.seed, a.format)


if __name__ == "__main__":
    main()
