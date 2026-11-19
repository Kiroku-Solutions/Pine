import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "scripts"))

import ocr_roboflow  # noqa: E402


def test_items_for_all_counts_unique_ids():
    items = ocr_roboflow.items_for("all")
    ids = [i["id"] for i in items]
    assert len(ids) == len(set(ids))
    assert all("crop" in i for i in items)


def test_items_for_single_round():
    assert len(ocr_roboflow.items_for("test")) == 150
