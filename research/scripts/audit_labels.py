"""KIE label audit -> research/reports/audit.json + audit.md.

Checks: docs per field and split, coverage per field (docs with GT / docs), GT present but no labeled
words (and the opposite), words without label (missing/empty label or empty text), boxes out of
range / degenerate, duplicate image hashes (also across splits = leakage).
"""
from __future__ import annotations

import argparse
import json
from collections import Counter, defaultdict

from common import FIELDS, LABELS, REPORTS, SPLITS_PATH, load_docs


def audit(docs: list[dict], split_of: dict[str, str]) -> dict:
    per_split = defaultdict(lambda: Counter())
    per_source = defaultdict(lambda: Counter())
    problems = defaultdict(list)
    hashes = defaultdict(list)
    for d in docs:
        sp = split_of.get(d["id"], "unassigned")
        per_split[sp]["docs"] += 1
        per_source[d["source"]]["docs"] += 1
        labels_in_doc = {w.get("label") for w in d["words"]}
        for f in FIELDS:
            has_gt = d["ground_truth"].get(f) not in (None, "")
            has_words = f"B-{f}" in labels_in_doc
            for ctr in (per_split[sp], per_source[d["source"]]):
                ctr[f"gt_{f}"] += has_gt
                ctr[f"words_{f}"] += has_words
            if has_gt and not has_words:
                problems["gt_without_labeled_words"].append(f"{d['id']}:{f}")
            if has_words and not has_gt:
                problems["labeled_words_without_gt"].append(f"{d['id']}:{f}")
        w_, h_ = d["width"], d["height"]
        for i, w in enumerate(d["words"]):
            lab = w.get("label")
            if lab is None or lab == "":
                problems["word_without_label"].append(f"{d['id']}#{i}")
            elif lab not in LABELS:
                problems["unknown_label"].append(f"{d['id']}#{i}:{lab}")
            if not str(w.get("text", "")).strip():
                problems["empty_text"].append(f"{d['id']}#{i}")
            x0, y0, x1, y1 = w["box"]
            if x0 < 0 or y0 < 0 or x1 > w_ or y1 > h_:
                problems["box_out_of_range"].append(f"{d['id']}#{i}")
            if x1 <= x0 or y1 <= y0:
                problems["box_degenerate"].append(f"{d['id']}#{i}")
        if d.get("image_sha256"):
            hashes[d["image_sha256"]].append(d["id"])
    dups = {h: ids for h, ids in hashes.items() if len(ids) > 1}
    leaks = {h: ids for h, ids in dups.items() if len({split_of.get(i) for i in ids}) > 1}
    n = len(docs) or 1
    coverage = {f: round(sum(1 for d in docs if d["ground_truth"].get(f) not in (None, "")) / n, 4)
                for f in FIELDS}
    return {
        "total_docs": len(docs),
        "per_split": {k: dict(v) for k, v in per_split.items()},
        "per_source": {k: dict(v) for k, v in per_source.items()},
        "coverage_per_field": coverage,
        "problem_counts": {k: len(v) for k, v in problems.items()},
        "problem_examples": {k: v[:10] for k, v in problems.items()},
        "duplicate_hash_groups": len(dups),
        "duplicate_examples": dict(list(dups.items())[:5]),
        "duplicates_crossing_splits": len(leaks),
    }


def to_markdown(rep: dict) -> str:
    L = ["# Label audit", "", f"Total docs: **{rep['total_docs']}**", "",
         "## Docs with ground truth per field, by split", "",
         "| split | docs | " + " | ".join(FIELDS) + " |", "|---|---|" + "---|" * len(FIELDS)]
    for sp, c in sorted(rep["per_split"].items()):
        L.append(f"| {sp} | {c.get('docs', 0)} | " + " | ".join(str(c.get(f'gt_{f}', 0)) for f in FIELDS) + " |")
    L += ["", "## By source", "", "| source | docs | " + " | ".join(FIELDS) + " |", "|---|---|" + "---|" * len(FIELDS)]
    for sp, c in sorted(rep["per_source"].items()):
        L.append(f"| {sp} | {c.get('docs', 0)} | " + " | ".join(str(c.get(f'gt_{f}', 0)) for f in FIELDS) + " |")
    L += ["", "## Coverage per field (docs with GT / all docs)", ""]
    L += [f"- {f}: {v:.1%}" for f, v in rep["coverage_per_field"].items()]
    L += ["", "## Problems", ""]
    if not rep["problem_counts"]:
        L.append("None found.")
    for k, v in rep["problem_counts"].items():
        L.append(f"- {k}: {v} (e.g. {', '.join(rep['problem_examples'][k][:3])})")
    L += ["", f"Duplicate image-hash groups: {rep['duplicate_hash_groups']}; "
              f"crossing splits (leakage): {rep['duplicates_crossing_splits']}"]
    return "\n".join(L) + "\n"


def main():
    argparse.ArgumentParser().parse_args()
    split_of = {}
    if SPLITS_PATH.exists():
        for sp, ids in json.loads(SPLITS_PATH.read_text(encoding="utf-8"))["splits"].items():
            for i in ids:
                split_of[i] = sp
    rep = audit(list(load_docs()), split_of)
    REPORTS.mkdir(parents=True, exist_ok=True)
    (REPORTS / "audit.json").write_text(json.dumps(rep, indent=1), encoding="utf-8")
    (REPORTS / "audit.md").write_text(to_markdown(rep), encoding="utf-8")
    print(to_markdown(rep))


if __name__ == "__main__":
    main()
