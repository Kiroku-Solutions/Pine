"""Synthetic tests for convert_wildreceipt (no network)."""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "scripts"))

import convert_wildreceipt as cw  # noqa: E402

CLASSES = "0 Ignore\n1 Store_name_value\n2 Store_name_key\n3 Date_value\n4 Tax_value\n5 Total_value\n6 Product_item_value\n"


def poly(x0, y0, x1, y1):
    return [x0, y0, x1, y0, x1, y1, x0, y1]


def test_class_parse_and_map():
    m = cw.label_to_field(cw.parse_class_list(CLASSES))
    assert m == {1: "MERCHANT", 3: "DATE", 4: "TAX", 5: "TOTAL"}


def test_convert_record():
    m = cw.label_to_field(cw.parse_class_list(CLASSES))
    rec = {"width": 100, "height": 200, "annotations": [
        {"box": poly(0, 0, 50, 10), "text": "ACME", "label": 1},
        {"box": poly(0, 0, 50, 10), "text": "STORE", "label": 1},
        {"box": poly(0, 20, 50, 30), "text": "milk", "label": 6},
        {"box": poly(0, 40, 50, 50), "text": "01/02/2020", "label": 3},
        {"box": poly(0, 60, 50, 70), "text": "Tax", "label": 2},
        {"box": poly(0, 60, 50, 70), "text": "1.20", "label": 4},
        {"box": poly(0, 80, 50, 90), "text": "  ", "label": 5},
        {"box": poly(0, 80, 50, 90), "text": "12.00", "label": 5},
        {"box": poly(0, 95, 50, 99), "text": "thanks", "label": 6},
        {"box": poly(0, 100, 50, 110), "text": "9.99", "label": 5},  # second span ignored
    ]}
    d = cw.convert_record(rec, "w1", "images/w1.jpg", m, "abc")
    labs = [w["label"] for w in d["words"]]
    assert labs == ["B-MERCHANT", "I-MERCHANT", "O", "B-DATE", "O", "B-TAX", "B-TOTAL", "O", "O"]
    assert d["ground_truth"] == {"MERCHANT": "ACME STORE", "DATE": "01/02/2020", "TOTAL": "12.00", "TAX": "1.20"}
    assert d["fields_available"] == ["MERCHANT", "DATE", "TOTAL", "TAX"]
    assert d["words"][0]["box"] == [0, 0, 50, 10]
