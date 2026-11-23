import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "colab"))
import colab_splits as cs  # noqa: E402
import layout_utils as lu  # noqa: E402


def _wild(n, dup_pairs=()):
    docs = {}
    for i in range(n):
        i_id = f"wildreceipt_train_{i:05d}"
        docs[i_id] = {"id": i_id, "source": "wildreceipt", "image_sha256": f"h{i}",
                      "words": [{"text": f"unique receipt text number {i} lorem", "label": "O"}],
                      "fields_available": list(lu.FIELDS)}
    for a, b in dup_pairs:  # near-identical: same text, different image hash
        ka, kb = f"wildreceipt_train_{a:05d}", f"wildreceipt_train_{b:05d}"
        docs[kb]["words"] = docs[ka]["words"]
    return docs


def test_carve_out_deterministic_and_sized():
    docs = _wild(100)
    t1, v1 = cs.carve_out(docs)
    t2, v2 = cs.carve_out(docs)
    assert (t1, v1) == (t2, v2)
    assert len(v1) == 10 and len(t1) == 90 and not set(t1) & set(v1)
    assert cs.carve_out(docs, seed=7)[1] != v1


def test_carve_out_keeps_groups_together():
    docs = _wild(60, dup_pairs=[(i, i + 1) for i in range(0, 60, 2)])
    docs["wildreceipt_train_00059"]["image_sha256"] = docs["wildreceipt_train_00058"]["image_sha256"]
    t, v = cs.carve_out(docs, frac=0.2)
    for g in cs.group_docs(docs):
        assert set(g) <= set(t) or set(g) <= set(v)
    assert len(v) >= 12


def test_heldout_never_in_train_or_val():
    main = {"train": ["cord_train_1", "sroie_train_1"], "val": ["cord_val_1"], "test": ["sroie_test_1"]}
    docs = _wild(40)
    wild = {"train": sorted(docs), "test": [f"wildreceipt_test_{i:05d}" for i in range(15)]}
    out = cs.build_colab_splits(main, wild, docs)
    assert set(out["test_wildreceipt"]) == set(wild["test"])
    assert not set(out["test_wildreceipt"]) & (set(out["train"]) | set(out["val"]) | set(out["test"]))
    assert sorted(out["train"] + out["val"]) == sorted(main["train"] + main["val"] + wild["train"])
    assert out["test"] == main["test"]


def test_disjointness_check_raises():
    import pytest
    with pytest.raises(ValueError):
        cs.assert_disjoint({"train": ["a", "b"], "test_wildreceipt": ["b"]})


def test_subset_4_fields():
    full = {"source": "wildreceipt", "fields_available": list(lu.FIELDS)}
    assert lu.has_all_fields(full)
    assert not lu.has_all_fields({"source": "wildreceipt", "fields_available": ["TOTAL", "DATE", "TAX"]})
    assert not lu.has_all_fields({"source": "sroie", "fields_available": ["MERCHANT", "DATE", "TOTAL"]})
    assert not lu.has_all_fields({"source": "cord", "fields_available": list(lu.FIELDS)})  # cord does not annotate all


def test_wildreceipt_mask_by_source():
    d = {"source": "wildreceipt", "fields_available": ["TOTAL"], "width": 10, "height": 10,
         "words": [{"text": "a", "box": [0, 0, 1, 1], "label": "B-TAX"}]}
    assert lu.annotated_fields(d) == lu.FIELDS  # per-source, not per-doc availability
    assert lu.build_example(d, lu.annotated_fields(d))[2] == [lu.LABEL2ID["B-TAX"]]
    d["source"] = "cord"
    d["fields_available"] = ["TOTAL", "TAX"]
    assert lu.build_example({**d, "words": [{"text": "a", "box": [0, 0, 1, 1], "label": "B-MERCHANT"}]},
                            lu.annotated_fields(d))[2] == [-100]


def test_field_report_only_counts_annotating_docs():
    cord = {"source": "cord", "fields_available": ["TOTAL", "TAX"]}
    sroie = {"source": "sroie", "fields_available": ["MERCHANT", "DATE", "TOTAL"]}
    gold = ["B-MERCHANT", "I-MERCHANT", "O", "B-TOTAL"]
    pred = ["B-MERCHANT", "I-MERCHANT", "O", "O"]
    rep = lu.field_report([(cord, gold, pred), (sroie, gold, pred)])
    assert rep["MERCHANT"]["docs"] == 1 and rep["MERCHANT"]["f1"] == 1.0  # cord doc ignored for MERCHANT
    assert rep["TOTAL"]["docs"] == 2 and rep["TOTAL"]["recall"] == 0.0
    assert rep["TAX"]["docs"] == 1 and rep["TAX"]["support"] == 0
    assert lu.extract_spans(["I-DATE", "I-DATE", "B-DATE"]) == {("DATE", 0, 2), ("DATE", 2, 3)}
