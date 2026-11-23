"""Minimal tests with tiny synthetic data (no network, no datasets lib)."""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "scripts"))

import numpy as np  # noqa: E402

import audit_labels  # noqa: E402
import convert_cord  # noqa: E402
import convert_sroie  # noqa: E402
import eval_fields  # noqa: E402
import make_splits  # noqa: E402
import quality_tags  # noqa: E402
from common import norm_amount, norm_date  # noqa: E402


def q(x0, y0, x1, y1):
    return {"x1": x0, "y1": y0, "x2": x1, "y2": y0, "x3": x1, "y3": y1, "x4": x0, "y4": y1}


CORD_GT = {
    "gt_parse": {"total": {"total_price": "1,591,600"}, "sub_total": {"tax_price": "144,691"}},
    "valid_line": [
        {"category": "menu.nm", "words": [{"text": "Nasi", "quad": q(0, 0, 10, 5)}]},
        {"category": "sub_total.tax_price", "words": [
            {"text": "Tax", "is_key": True, "quad": q(0, 10, 10, 15)},
            {"text": "144,691", "quad": q(20, 10, 40, 15)}]},
        {"category": "total.total_price", "words": [
            {"text": "1,591,600", "quad": q(20, 20, 60, 25)}]},
    ],
}


def test_cord_conversion():
    d = convert_cord.convert_record(CORD_GT, "c1", "images/c1.png", 100, 100)
    labels = {w["text"]: w["label"] for w in d["words"]}
    assert labels == {"Nasi": "O", "Tax": "O", "144,691": "B-TAX", "1,591,600": "B-TOTAL"}
    assert d["ground_truth"]["TOTAL"] == "1,591,600" and d["ground_truth"]["TAX"] == "144,691"
    assert d["ground_truth"]["MERCHANT"] is None and "MERCHANT" not in d["fields_available"]
    assert d["words"][3]["box"] == [20, 20, 60, 25]


def test_sroie_conversion_first_span():
    pts = lambda x0, y0, x1, y1: [[x0, y0], [x1, y0], [x1, y1], [x0, y1]]  # noqa: E731
    ocr = [{"text": "ACME", "label": "company", "box": pts(50, 100, 100, 150)},
           {"text": "SDN BHD", "label": "company", "box": pts(50, 150, 100, 170)},
           {"text": "JALAN 1", "label": "address", "box": pts(0, 0, 5, 5)},
           {"text": "9.50", "label": "total", "box": pts(0, 0, 5, 5)},
           {"text": "10/02/2018 8:13 PM", "label": "date", "box": pts(0, 0, 5, 5)},
           {"text": "9.50", "label": "total", "box": pts(0, 0, 5, 5)}]
    d = convert_sroie.convert_record(ocr, "s1", "images/s1.png", 500, 1000)
    assert [w["label"] for w in d["words"]] == [
        "B-MERCHANT", "I-MERCHANT", "O", "B-TOTAL", "B-DATE", "O"]
    assert d["ground_truth"] == {"MERCHANT": "ACME SDN BHD", "DATE": "10/02/2018 8:13 PM",
                                 "TOTAL": "9.50", "TAX": None}
    assert d["words"][0]["box"] == [50, 100, 100, 150]


def test_normalizers():
    assert norm_amount("1,591,600") == 1591600
    assert norm_amount("RM 9.50") == 9.5
    assert norm_amount("1.234,56") == 1234.56
    assert norm_amount("9,50") == 9.5
    assert norm_date("10/02/2018") == "2018-02-10"
    assert norm_date("2018-02-10") == "2018-02-10"
    assert norm_date("10 FEB 2018") == "2018-02-10"
    assert norm_date("garbage") is None


def _doc(i, gt):
    return {"id": i, "source": "x", "ground_truth": {"MERCHANT": None, "DATE": None, "TOTAL": None,
                                                     "TAX": None, **gt}}


def test_eval_f1_and_macro():
    docs = [_doc("a", {"MERCHANT": "ACME Ltd.", "TOTAL": "10.00", "TAX": "1.00", "DATE": "2020-01-02"}),
            _doc("b", {"MERCHANT": "Foo", "TOTAL": "5.00", "TAX": "0.50", "DATE": "2020-01-03"})]
    preds = {"a": {"merchant": "acme  ltd", "total": "10.01", "date": "2020-01-02", "tax": None},
             "b": {"merchant": "Bar", "total": None, "date": None, "tax": None}}
    r = eval_fields.evaluate(docs, preds)
    pf = r["per_field"]
    assert pf["MERCHANT"]["tp"] == 1 and pf["MERCHANT"]["fp"] == 1 and pf["MERCHANT"]["fn"] == 1
    assert pf["TOTAL"]["precision"] == 1.0 and pf["TOTAL"]["recall"] == 0.5
    assert pf["TAX"]["f1"] == 0.0 and pf["TAX"]["status"] == "unsupported_by_system"
    f1s = {k: pf[k]["f1"] for k in pf}
    four = [v for k, v in r.items() if k.startswith("macro_f1_4")][0]
    sup = [v for k, v in r.items() if k.startswith("macro_f1_supported")][0]
    assert abs(four - sum(f1s.values()) / 4) < 1e-3
    assert abs(sup - (f1s["MERCHANT"] + f1s["DATE"] + f1s["TOTAL"]) / 3) < 1e-3


def test_eval_excludes_fields_without_gt():
    r = eval_fields.evaluate([_doc("a", {"TOTAL": "1.00"})], {"a": {"total": "1.00"}})
    assert set(r["excluded_no_gt"]) == {"MERCHANT", "DATE", "TAX"}
    assert [v for k, v in r.items() if k.startswith("macro_f1_4")][0] == 1.0


def _split_docs():
    docs = [{"id": f"d{i}", "source": "cord" if i % 2 else "sroie", "image_sha256": f"h{i % 40}"}
            for i in range(100)]
    return docs


def test_splits_deterministic_disjoint_no_leak():
    a = make_splits.build_splits(_split_docs())
    b = make_splits.build_splits(list(reversed(_split_docs())))
    assert a == b
    sp = a["splits"]
    all_ids = sp["train"] + sp["val"] + sp["test"]
    assert len(all_ids) == len(set(all_ids)) == 100
    where = {i: k for k, v in sp.items() for i in v}
    for d in _split_docs():  # same hash -> same split
        same = [x["id"] for x in _split_docs() if x["image_sha256"] == d["image_sha256"]]
        assert len({where[i] for i in same}) == 1


def test_audit_detects_problems():
    good = {"id": "g", "source": "cord", "width": 100, "height": 100, "image_sha256": "h",
            "ground_truth": {"MERCHANT": None, "DATE": None, "TOTAL": "1", "TAX": None},
            "words": [{"text": "1", "box": [0, 0, 10, 10], "label": "B-TOTAL"}]}
    bad = {"id": "b", "source": "cord", "width": 100, "height": 100, "image_sha256": "h",
           "ground_truth": {"MERCHANT": None, "DATE": None, "TOTAL": "2", "TAX": None},
           "words": [{"text": "2", "box": [0, 0, 500, 10], "label": ""}]}
    rep = audit_labels.audit([good, bad], {"g": "train", "b": "test"})
    pc = rep["problem_counts"]
    assert pc["box_out_of_range"] == 1 and pc["word_without_label"] == 1
    assert pc["gt_without_labeled_words"] == 1
    assert rep["duplicate_hash_groups"] == 1 and rep["duplicates_crossing_splits"] == 1
    assert rep["coverage_per_field"]["TOTAL"] == 1.0


def test_quality_tags():
    rng = np.random.default_rng(0)
    sharp = (rng.integers(0, 255, (1000, 500, 3))).astype(np.uint8)
    flat = np.full((1000, 500, 3), 30, np.uint8)
    assert quality_tags.tag_image(sharp)["blur_tag"] == "sharp"
    t = quality_tags.tag_image(flat)
    assert t["blur_tag"] == "blurry" and t["illumination"]["tag"] == "dark"
    assert quality_tags.layout_tag(1000, 300) == "long_narrow"
