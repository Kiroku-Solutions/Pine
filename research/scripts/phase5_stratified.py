"""PHASE 5 - stratified error analysis (blur / illumination / layout) of the three systems on the test split.
Systems: heuristics (Phase 1 baseline), layoutlm (model only), hybrid (heuristics overlaid by the model).
Slices come from quality_tags.py, whose thresholds are UNCALIBRATED placeholders: read the slices as
indicative, not as a calibrated difficulty scale. Slices with < MIN_DOCS docs are flagged, not hidden.
Usage: python scripts/phase5_stratified.py --pred heuristics=data/pred_paddle layoutlm=data/pred_layoutlm \
         hybrid=data/pred_layoutlm-hybrid --out reports/phase5_stratified.json
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path

from common import FIELDS, REPORTS, SPLITS_PATH, load_docs
from eval_fields import evaluate

MIN_DOCS = 15


def load_preds(d):
    return {p["id"]: p for p in (json.loads(f.read_text(encoding="utf-8")) for f in Path(d).glob("*.json"))}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--pred", nargs="+", required=True, help="name=dir")
    ap.add_argument("--tags", default=str(REPORTS / "quality_tags.json"))
    ap.add_argument("--out", default=str(REPORTS / "phase5_stratified.json"))
    a = ap.parse_args()
    ids = set(json.loads(SPLITS_PATH.read_text(encoding="utf-8"))["splits"]["test"])
    docs = [d for d in load_docs("all") if d["id"] in ids]
    tags = json.loads(Path(a.tags).read_text(encoding="utf-8"))
    systems = {n: load_preds(p) for n, p in (s.split("=", 1) for s in a.pred)}
    dims = {"blur": lambda t: t["blur_tag"], "illumination": lambda t: t["illumination"]["tag"],
            "layout": lambda t: t["layout"], "source": None}
    out = {"min_docs_flag": MIN_DOCS, "dimensions": {}}
    for dim, fn in dims.items():
        groups = {}
        for d in docs:
            key = d["source"] if fn is None else fn(tags[d["id"]])
            groups.setdefault(key, []).append(d)
        out["dimensions"][dim] = {}
        for key, ds in sorted(groups.items()):
            row = {"docs": len(ds), "low_n": len(ds) < MIN_DOCS, "systems": {}}
            for n, preds in systems.items():
                r = evaluate(ds, preds, supported=FIELDS if n != "heuristics" else ["MERCHANT", "DATE", "TOTAL"])
                m = [v for k, v in r.items() if k.startswith("macro_f1_4")][0]
                row["systems"][n] = {"macro_f1_4": m, **{f: r["per_field"][f]["f1"] for f in FIELDS}}
            out["dimensions"][dim][key] = row
    Path(a.out).write_text(json.dumps(out, indent=1), encoding="utf-8")
    for dim, g in out["dimensions"].items():
        print(f"\n[{dim}]")
        for k, row in g.items():
            print(f"  {k:13s} n={row['docs']:3d}{' (low n)' if row['low_n'] else ''} " + " | ".join(
                f"{n}={s['macro_f1_4']}" for n, s in row["systems"].items()))


if __name__ == "__main__":
    main()
