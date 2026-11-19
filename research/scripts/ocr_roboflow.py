"""Run PaddleOCR over the Roboflow crops listed in labeling/round{1,2,3}.json and test_to_verify.json.

Output <out>/<id>.json, same schema as ocr_dump.py:
  {"id", "image", "width", "height", "ocr_seconds", "words": [{"text","confidence","box":{x,y,width,height}}]}
Idempotent: existing outputs are skipped and do not count against --limit.
Usage: python scripts/ocr_roboflow.py --round round1|round2|round3|test|all [--limit 40] [--out data/roboflow/ocr]
Run in batches (fresh process per batch) to keep memory bounded.
"""
from __future__ import annotations

import argparse
import json
import time
from pathlib import Path

from ocr_dump import make_engine, run_ocr
from roboflow_common import ROBOFLOW_DIR, imread

ROUND_FILES = {"round1": "round1.json", "round2": "round2.json", "round3": "round3.json",
               "test": "test_to_verify.json"}


def items_for(round_name: str, root: Path = ROBOFLOW_DIR) -> list[dict]:
    names = list(ROUND_FILES) if round_name == "all" else [round_name]
    items = []
    for n in names:
        items += json.loads((root / "labeling" / ROUND_FILES[n]).read_text(encoding="utf-8"))["items"]
    return items


def main(argv=None):
    ap = argparse.ArgumentParser()
    ap.add_argument("--round", default="all", choices=["all", *ROUND_FILES])
    ap.add_argument("--out", type=Path, default=ROBOFLOW_DIR / "ocr")
    ap.add_argument("--limit", type=int, default=0, help="max NEW images this run (0 = no limit)")
    ap.add_argument("--lang", default="en")
    a = ap.parse_args(argv)
    a.out.mkdir(parents=True, exist_ok=True)
    todo = [it for it in items_for(a.round) if not (a.out / f"{it['id']}.json").exists()]
    if a.limit:
        todo = todo[: a.limit]
    if not todo:
        print("ocr_roboflow: 0 images (nothing pending)")
        return
    engine = make_engine(a.lang)
    n = 0
    for it in todo:
        img = ROBOFLOW_DIR / it["crop"]
        h, w = imread(img).shape[:2]
        t0 = time.perf_counter()
        words = run_ocr(engine, str(img))
        dt = time.perf_counter() - t0
        (a.out / f"{it['id']}.json").write_text(json.dumps(
            {"id": it["id"], "image": str(img), "width": w, "height": h,
             "ocr_seconds": round(dt, 3), "words": words}), encoding="utf-8")
        n += 1
    print(f"ocr_roboflow: {n} images -> {a.out}")


if __name__ == "__main__":
    main()
