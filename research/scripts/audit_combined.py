"""Audit CORD+SROIE+WildReceipt combined (imports audit_labels; writes reports/audit_combined.*)."""
from __future__ import annotations

import json
from collections import Counter

import audit_labels
from common import FIELDS, REPORTS, RESEARCH, SPLITS_PATH, load_docs


def main():
    split_of = {}
    for sp, ids in json.loads(SPLITS_PATH.read_text(encoding="utf-8"))["splits"].items():
        for i in ids:
            split_of[i] = sp
    extra = RESEARCH / "splits_extra.json"
    if extra.exists():
        for src, d in json.loads(extra.read_text(encoding="utf-8"))["sources"].items():
            for sp, ids in d.items():
                for i in ids:
                    split_of[i] = f"extra_{sp}"
    docs = list(load_docs())
    rep = audit_labels.audit(docs, split_of)
    combos = Counter()
    per_src = {}
    for d in docs:
        av = tuple(f for f in FIELDS if d["ground_truth"].get(f))
        combos[",".join(av) or "none"] += 1
        s = per_src.setdefault(d["source"], Counter())
        s["docs"] += 1
        s["all4"] += len(av) == 4
        s["merchant+date+total+tax>=3"] += len(av) >= 3
    rep["field_combinations"] = dict(combos.most_common())
    rep["all4_docs"] = sum(1 for d in docs if len(d["fields_available"]) == 4)
    rep["per_source_all4"] = {k: dict(v) for k, v in per_src.items()}
    REPORTS.mkdir(parents=True, exist_ok=True)
    (REPORTS / "audit_combined.json").write_text(json.dumps(rep, indent=1), encoding="utf-8")
    md = audit_labels.to_markdown(rep) + "\n## Field combinations (docs)\n\n" + "\n".join(
        f"- {k}: {v}" for k, v in rep["field_combinations"].items()) + f"\n\nDocs with all 4 fields: {rep['all4_docs']}\n"
    (REPORTS / "audit_combined.md").write_text(md, encoding="utf-8")
    print(md)
    print(json.dumps(rep["per_source_all4"]))


if __name__ == "__main__":
    main()
