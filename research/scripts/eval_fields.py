"""Field-level P/R/F1 + Macro-F1 for MERCHANT/DATE/TOTAL/TAX.

Inputs: processed docs (ground_truth), predictions dir (<id>.json with merchant,total,date,tax),
and splits.json (--split). A prediction of null/missing = the system abstained.

Per field (over docs of the chosen split/source):
  TP = pred present and matches GT      FP = pred present and (GT absent or mismatch)
  FN = GT present and not matched (abstained OR wrong)
A field is only scored on docs whose source annotates it at all (CORD: TOTAL,TAX; SROIE: MERCHANT,DATE,TOTAL).
Matching: merchant = lowercase, punctuation stripped, spaces collapsed; total/tax = decimal within 0.01;
date = ISO yyyy-mm-dd.

Macro-F1 (both labelled in the output):
  macro_f1_4_fields    : mean over the 4 fields; an unsupported field (system emits no tax) counts as 0
                         if the data has GT for it. Fields with NO GT in the evaluated docs are excluded
                         (undefined) and listed in 'excluded_no_gt' -- never silently counted as 0 or 1.
  macro_f1_supported   : mean over the fields the system supports (merchant,date,total) with GT.
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path

from common import FIELDS, REPORTS, SPLITS_PATH, load_docs, norm_amount, norm_date, norm_text

PRED_KEY = {"MERCHANT": "merchant", "DATE": "date", "TOTAL": "total", "TAX": "tax"}
SUPPORTED_DEFAULT = ["MERCHANT", "DATE", "TOTAL"]


def match(field: str, pred, gt) -> bool:
    if field == "MERCHANT":
        return bool(norm_text(pred)) and norm_text(pred) == norm_text(gt)
    if field in ("TOTAL", "TAX"):
        a, b = norm_amount(pred), norm_amount(gt)
        return a is not None and b is not None and abs(a - b) <= 0.01 + 1e-9
    if field == "DATE":
        return norm_date(pred) is not None and norm_date(pred) == norm_date(gt)
    raise ValueError(field)


def _prf(tp, fp, fn):
    p = tp / (tp + fp) if tp + fp else 0.0
    r = tp / (tp + fn) if tp + fn else 0.0
    f = 2 * p * r / (p + r) if p + r else 0.0
    return p, r, f


def evaluate(docs: list[dict], preds: dict[str, dict], supported=SUPPORTED_DEFAULT) -> dict:
    counts = {f: {"tp": 0, "fp": 0, "fn": 0, "gt_docs": 0, "pred_docs": 0} for f in FIELDS}
    # A field is scored only on docs whose SOURCE annotates it at all (e.g. CORD has no merchant/date,
    # SROIE no tax); otherwise predictions would be counted as false positives against nothing.
    annotated = {(d["source"], f) for d in docs for f in FIELDS if d["ground_truth"].get(f) not in (None, "")}
    for d in docs:
        pred = preds.get(d["id"], {})
        for f in FIELDS:
            if (d["source"], f) not in annotated:
                continue
            gt = d["ground_truth"].get(f)
            gt = gt if gt not in (None, "") else None
            pv = pred.get(PRED_KEY[f])
            pv = pv if pv not in (None, "") else None
            c = counts[f]
            c["gt_docs"] += gt is not None
            c["pred_docs"] += pv is not None
            if pv is not None and gt is not None and match(f, pv, gt):
                c["tp"] += 1
            else:
                if pv is not None:
                    c["fp"] += 1
                if gt is not None:
                    c["fn"] += 1
    per_field, f1_4, f1_sup, excluded = {}, [], [], []
    for f in FIELDS:
        c = counts[f]
        p, r, f1 = _prf(c["tp"], c["fp"], c["fn"])
        status = "ok"
        if c["gt_docs"] == 0:
            status = "no_ground_truth"
            excluded.append(f)
        elif f not in supported:
            status = "unsupported_by_system"
        per_field[f] = {**c, "precision": round(p, 4), "recall": round(r, 4), "f1": round(f1, 4),
                        "status": status}
        if c["gt_docs"] > 0:
            f1_4.append(f1)  # unsupported field has no preds -> f1 = 0 -> counts as 0
            if f in supported:
                f1_sup.append(f1)
    mean = lambda xs: round(sum(xs) / len(xs), 4) if xs else None  # noqa: E731
    return {
        "n_docs": len(docs),
        "per_field": per_field,
        "macro_f1_4_fields (unsupported fields count as 0)": mean(f1_4),
        "macro_f1_supported_fields (" + ",".join(f for f in supported) + ")": mean(f1_sup),
        "excluded_no_gt": excluded,
    }


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--pred", required=True, help="directory with <id>.json predictions")
    ap.add_argument("--split", default="test", choices=["train", "val", "test", "all"])
    ap.add_argument("--source", default="all", choices=["all", "cord", "sroie"])
    ap.add_argument("--out", default=str(REPORTS / "baseline_eval.json"))
    args = ap.parse_args()
    ids = None
    if args.split != "all":
        ids = set(json.loads(SPLITS_PATH.read_text(encoding="utf-8"))["splits"][args.split])
    docs = [d for d in load_docs(args.source) if ids is None or d["id"] in ids]
    preds = {}
    for f in Path(args.pred).glob("*.json"):
        p = json.loads(f.read_text(encoding="utf-8"))
        preds[p["id"]] = p
    missing = [d["id"] for d in docs if d["id"] not in preds]
    rep = {"split": args.split, "source": args.source, "docs_without_prediction_file": len(missing),
           **evaluate(docs, preds)}
    # also per-source breakdown when evaluating everything
    if args.source == "all":
        rep["by_source"] = {s: evaluate([d for d in docs if d["source"] == s], preds)
                            for s in sorted({d["source"] for d in docs})}
    Path(args.out).parent.mkdir(parents=True, exist_ok=True)
    Path(args.out).write_text(json.dumps(rep, indent=1), encoding="utf-8")
    print(json.dumps(rep, indent=1))


if __name__ == "__main__":
    main()
