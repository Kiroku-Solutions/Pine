"""Download CORD-v2 and SROIE from the Hugging Face Hub into research/data/raw (git-ignored).

Idempotent: a dataset whose target folder already holds a .complete marker is skipped.
Usage: python scripts/download_datasets.py [--only cord|sroie] [--force]

Sources (public):
  cord  : naver-clova-ix/cord-v2                          (CC BY 4.0)
  sroie : arvindrajan92/sroie_document_understanding      (MIT per its card; underlying SROIE 2019
          images/annotations come from the ICDAR 2019 competition - check its terms before redistributing)
"""
from __future__ import annotations

import argparse
import shutil

from common import DATA

SOURCES = {
    "cord": ("naver-clova-ix/cord-v2", "cord-v2"),
    "sroie": ("arvindrajan92/sroie_document_understanding", "sroie"),
}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", choices=list(SOURCES))
    ap.add_argument("--force", action="store_true")
    args = ap.parse_args()
    from datasets import load_dataset

    for key, (repo, folder) in SOURCES.items():
        if args.only and args.only != key:
            continue
        target = DATA / "raw" / folder
        marker = target / ".complete"
        if marker.exists() and not args.force:
            print(f"[skip] {key}: already downloaded at {target}")
            continue
        if target.exists():
            shutil.rmtree(target)
        print(f"[get ] {key}: {repo}")
        ds = load_dataset(repo)
        target.parent.mkdir(parents=True, exist_ok=True)
        ds.save_to_disk(str(target))
        marker.write_text(repo, encoding="utf-8")
        print(f"[ok  ] {key}: {({k: len(v) for k, v in ds.items()})} -> {target}")


if __name__ == "__main__":
    main()
