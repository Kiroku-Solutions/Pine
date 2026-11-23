"""Frozen, deterministic train/val/test split by UNIQUE DOCUMENT (image sha256), manifest splits.json.

Documents with the same image hash form one group and always land in the same split (no leakage).
Split is stratified per source (cord / sroie) so each source is represented in every split.
NEVER regenerated if research/splits.json exists (pass --force only to deliberately re-freeze).
"""
from __future__ import annotations

import argparse
import hashlib
import json

from common import SPLITS_PATH, load_docs

SEED = 20260101
RATIOS = {"train": 0.8, "val": 0.1, "test": 0.1}


def build_splits(docs: list[dict], seed: int = SEED, ratios: dict = RATIOS) -> dict:
    by_source: dict[str, dict[str, list[str]]] = {}
    for d in docs:
        key = d.get("image_sha256") or d["id"]
        by_source.setdefault(d["source"], {}).setdefault(key, []).append(d["id"])
    splits = {"train": [], "val": [], "test": []}
    for source in sorted(by_source):
        groups = sorted(by_source[source].items(),
                        key=lambda kv: hashlib.sha256(f"{seed}:{kv[0]}".encode()).hexdigest())
        n = len(groups)
        n_val = round(n * ratios["val"])
        n_test = round(n * ratios["test"])
        for i, (_, ids) in enumerate(groups):
            name = "val" if i < n_val else "test" if i < n_val + n_test else "train"
            splits[name].extend(ids)
    for k in splits:
        splits[k].sort()
    return {"seed": seed, "ratios": ratios, "unit": "image_sha256 group", "splits": splits}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--force", action="store_true")
    args = ap.parse_args()
    if SPLITS_PATH.exists() and not args.force:
        print(f"[skip] {SPLITS_PATH} exists (frozen)")
        return
    manifest = build_splits(list(load_docs()))
    SPLITS_PATH.write_text(json.dumps(manifest, indent=1), encoding="utf-8")
    print({k: len(v) for k, v in manifest["splits"].items()}, "->", SPLITS_PATH)


if __name__ == "__main__":
    main()
