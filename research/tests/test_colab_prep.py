import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "colab"))
import layout_utils as lu  # noqa: E402


class FakeTok:
    """Splits each word into subtokens of 2 chars; word_ids like a fast tokenizer (+ special tokens)."""
    def word_ids(self, words):
        ids = [None]
        for i, w in enumerate(words):
            ids += [i] * max(1, -(-len(w) // 2))
        return ids + [None]


def test_normalize_box():
    assert lu.normalize_box([0, 0, 100, 50], 200, 100) == [0, 0, 500, 500]
    assert lu.normalize_box([-5, 0, 300, 120], 200, 100) == [0, 0, 1000, 1000]
    assert lu.normalize_box([100, 50, 0, 0], 200, 100) == [0, 0, 500, 500]


def test_align_first_subtoken_only():
    wid = FakeTok().word_ids(["abcd", "e"])
    out = lu.align_labels(wid, [lu.LABEL2ID["B-TOTAL"], lu.LABEL2ID["I-TOTAL"]])
    assert out == [-100, lu.LABEL2ID["B-TOTAL"], -100, lu.LABEL2ID["I-TOTAL"], -100]


def test_align_label_all_tokens_turns_b_into_i():
    wid = FakeTok().word_ids(["abcd"])
    out = lu.align_labels(wid, [lu.LABEL2ID["B-DATE"]], label_all_tokens=True)
    assert out == [-100, lu.LABEL2ID["B-DATE"], lu.LABEL2ID["I-DATE"], -100]


def _doc(source):
    words = [{"text": "ACME", "box": [0, 0, 10, 10], "label": "B-MERCHANT"},
             {"text": "5.00", "box": [0, 20, 10, 30], "label": "B-TOTAL"},
             {"text": "x", "box": [0, 40, 10, 50], "label": "O"},
             {"text": " ", "box": [0, 0, 1, 1], "label": "O"}]
    return {"source": source, "width": 100, "height": 100, "words": words, "fields_available": ["TOTAL"]}


def test_partial_masking_by_source():
    for src, merchant_masked in (("cord", True), ("sroie", False)):
        d = _doc(src)
        words, boxes, labs = lu.build_example(d, lu.annotated_fields(d))
        assert words == ["ACME", "5.00", "x"]  # blank word dropped
        assert boxes[1] == [0, 200, 100, 300]
        assert (labs[0] == -100) is merchant_masked
        assert labs[1] == lu.LABEL2ID["B-TOTAL"] and labs[2] == 0


def test_partial_modes():
    d = _doc("cord")
    assert lu.annotated_fields(d, "none") == lu.FIELDS
    assert lu.annotated_fields(d, "available") == ["TOTAL"]
    assert lu.annotated_fields({"source": "other", "fields_available": ["DATE"]}) == ["DATE"]
