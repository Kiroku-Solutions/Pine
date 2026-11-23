"""Roboflow ingest tests on tiny synthetic COCO / YOLO exports (generated images, 1 negative, 1 duplicate). No network."""
import json
import sys
from pathlib import Path

import numpy as np
import pytest

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "scripts"))

import prelabel_for_labelstudio as pl  # noqa: E402
import roboflow_download as rd  # noqa: E402
import roboflow_prepare as rp  # noqa: E402
import sample_for_labeling as sfl  # noqa: E402
from roboflow_common import hamming, imread, imwrite, phash  # noqa: E402

W, H = 200, 160
BOX = [40, 30, 100, 90]  # x, y, w, h


def make_img(seed: int) -> np.ndarray:
    rng = np.random.default_rng(seed)
    coarse = rng.integers(0, 256, (4, 4), dtype=np.uint8)
    img = np.kron(coarse, np.ones((H // 4, W // 4), dtype=np.uint8))
    img = np.stack([img] * 3, axis=-1)
    x, y, w, h = BOX
    img[y:y + h, x:x + w] = (rng.integers(150, 255, 3)).astype(np.uint8)
    return img


# names: a,b (train), dup of a (valid in COCO, to test group->one split), neg (no boxes), c (test)
def build_coco(root: Path):
    spec = {"train": ["a", "b", "neg"], "valid": ["a_dup"], "test": ["c"]}
    seeds = {"a": 1, "a_dup": 1, "b": 2, "neg": 3, "c": 4}
    for split, names in spec.items():
        anns, imgs = [], []
        for i, n in enumerate(names):
            imwrite(root / split / f"{n}.jpg", make_img(seeds[n]))
            imgs.append({"id": i, "file_name": f"{n}.jpg", "width": W, "height": H})
            if n != "neg":
                anns.append({"id": i, "image_id": i, "category_id": 1, "bbox": BOX})
        (root / split / "_annotations.coco.json").write_text(json.dumps(
            {"images": imgs, "annotations": anns, "categories": [{"id": 1, "name": "document"}]}), encoding="utf-8")


def build_yolo(root: Path):
    spec = {"train": ["a", "b", "neg"], "valid": ["a_dup"], "test": ["c"]}
    seeds = {"a": 1, "a_dup": 1, "b": 2, "neg": 3, "c": 4}
    x, y, w, h = BOX
    line = f"0 {(x + w / 2) / W} {(y + h / 2) / H} {w / W} {h / H}\n"
    for split, names in spec.items():
        for n in names:
            imwrite(root / split / "images" / f"{n}.jpg", make_img(seeds[n]))
            (root / split / "labels").mkdir(parents=True, exist_ok=True)
            (root / split / "labels" / f"{n}.txt").write_text("" if n == "neg" else line, encoding="utf-8")


@pytest.fixture(params=["coco", "yolo"])
def export(request, tmp_path):
    root = tmp_path / "export"
    (build_coco if request.param == "coco" else build_yolo)(root)
    return root


def test_phash_duplicate_close_distinct_far():
    a, dup, b = make_img(1), make_img(1), make_img(2)
    noisy = np.clip(a.astype(int) + np.random.default_rng(0).integers(-3, 4, a.shape), 0, 255).astype(np.uint8)
    assert phash(a) == phash(dup)
    assert hamming(phash(a), phash(noisy)) <= 6
    assert hamming(phash(a), phash(b)) > 6


def test_download_from_dir_summary(export, capsys):
    rd.from_dir(export, export, copy=False)
    out = capsys.readouterr().out
    assert "train: 3 images (1 without boxes)" in out


def test_api_mode_requires_env_key(monkeypatch):
    monkeypatch.delenv("ROBOFLOW_API_KEY", raising=False)
    with pytest.raises(SystemExit) as e:
        rd.from_api("w", "p", 1, "coco", Path("x"))
    assert "ROBOFLOW_API_KEY" in str(e.value)


def test_prepare_drops_negative_dedups_crops_and_freezes(export, tmp_path):
    out = tmp_path / "rf"
    res = rp.prepare(export, out, margin=0.1, thr=6)
    assert [d["name"] for d in res["dropped"]] == ["neg.jpg"]
    man = json.loads((out / "manifest.json").read_text())
    assert man["n_input"] == 5 and man["n_kept"] == 4 and man["n_groups"] == 3
    by_src = {m["source_image"]: m for m in man["items"]}
    # duplicate a / a_dup: same group and SAME split even though Roboflow put them in train vs valid
    assert by_src["a.jpg"]["group"] == by_src["a_dup.jpg"]["group"]
    assert by_src["a.jpg"]["split"] == by_src["a_dup.jpg"]["split"] == "valid"  # priority valid > train
    assert by_src["c.jpg"]["split"] == "test" and by_src["b.jpg"]["split"] == "train"
    crop = imread(out / by_src["b.jpg"]["crop"])
    assert crop.shape[:2] == (108, 120)  # 90*1.2 x 100*1.2 (margin 10% each side)
    # frozen: second run with other margin/seed does not touch splits.json
    before = (out / "splits.json").read_text()
    rp.prepare(export, out, margin=0.0, thr=6, seed=999)
    assert (out / "splits.json").read_text() == before


def test_prepare_no_split_info_groups_stay_together(tmp_path):
    root = tmp_path / "flat"
    for n, s in [("a", 1), ("a2", 1), ("b", 2)]:
        imwrite(root / "images" / f"{n}.jpg", make_img(s))
        x, y, w, h = BOX
        (root / "labels").mkdir(exist_ok=True)
        (root / "labels" / f"{n}.txt").write_text(f"0 {(x + w / 2) / W} {(y + h / 2) / H} {w / W} {h / H}\n")
    res = rp.prepare(root, tmp_path / "o", seed=1)
    m = {i["source_image"]: i for i in res["manifest"]}
    assert m["a.jpg"]["split"] == m["a2.jpg"]["split"]


def test_negative_tag_dropped(tmp_path):
    root = tmp_path / "e"
    imwrite(root / "train" / "t.jpg", make_img(7))
    (root / "train" / "_annotations.coco.json").write_text(json.dumps({
        "images": [{"id": 0, "file_name": "t.jpg", "width": W, "height": H, "tags": ["Negative-Sample"]}],
        "annotations": [{"id": 0, "image_id": 0, "category_id": 1, "bbox": BOX}]}))
    res = rp.prepare(root, tmp_path / "o")
    assert res["dropped"][0]["reason"].endswith("tag") and not res["manifest"]


def test_sampling_stratified_frozen_and_disjoint(tmp_path):
    root = tmp_path
    items = []
    rng = np.random.default_rng(0)
    for i in range(60):
        img = (rng.random((40, 40, 3)) * (30 + 3 * i)).astype(np.uint8)  # varied lum/contrast
        imwrite(root / "crops" / f"{i}.jpg", img)
        items.append({"id": f"i{i}", "crop": f"crops/{i}.jpg", "group": f"g{i}", "split": "train"})
    r1 = sfl.sample(items, root, rounds=(10, 12, 12), test_n=8, seed=5)
    r2 = sfl.sample(items, root, rounds=(10, 12, 12), test_n=8, seed=5)
    ids = lambda r: ([i["id"] for i in r["test"]], [[i["id"] for i in x] for x in r["rounds"]])
    assert ids(r1) == ids(r2)  # fixed seed -> deterministic
    assert [len(x) for x in r1["rounds"]] == [10, 12, 12] and len(r1["test"]) == 8
    allids = [i for i in ids(r1)[0]] + [i for x in ids(r1)[1] for i in x]
    assert len(allids) == len(set(allids))  # test and rounds disjoint
    assert len({i["blur_bin"] * 3 + i["lum_bin"] for i in r1["rounds"][1]}) > 1  # several strata used
    # CLI refuses to overwrite the frozen test
    man = tmp_path / "manifest.json"
    man.write_text(json.dumps({"items": items}))
    sfl.main(["--manifest", str(man), "--out", str(tmp_path / "lab"), "--test-n", "8", "--rounds", "10", "12", "12"])
    with pytest.raises(SystemExit):
        sfl.main(["--manifest", str(man), "--out", str(tmp_path / "lab")])


def test_prelabel_tasks_and_blind_test(tmp_path):
    words = [
        {"text": "ACME STORE", "confidence": 0.9, "box": {"x": 10, "y": 5, "width": 80, "height": 10}},
        {"text": "12/03/2024", "confidence": 0.9, "box": {"x": 10, "y": 20, "width": 60, "height": 10}},
        {"text": "Tax", "confidence": 0.9, "box": {"x": 10, "y": 60, "width": 20, "height": 10}},
        {"text": "1.50", "confidence": 0.9, "box": {"x": 60, "y": 60, "width": 20, "height": 10}},
        {"text": "Total", "confidence": 0.9, "box": {"x": 10, "y": 80, "width": 30, "height": 10}},
        {"text": "11.50", "confidence": 0.9, "box": {"x": 60, "y": 80, "width": 30, "height": 10}},
    ]
    ocr = {"width": 100, "height": 100, "words": words}
    lab = {r["value"]["rectanglelabels"][0]: r for r in
           pl.make_task({"id": "x", "crop": "c.jpg"}, ocr, "/u/c.jpg", False, None)["predictions"][0]["result"]}
    assert set(lab) == {"MERCHANT", "DATE", "TOTAL", "TAX"}
    assert lab["TOTAL"]["value"]["x"] == 60.0 and lab["TOTAL"]["value"]["y"] == 80.0
    assert lab["TAX"]["value"]["y"] == 60.0
    blind = pl.make_task({"id": "x", "crop": "c.jpg"}, ocr, "/u/c.jpg", True, None)
    assert "predictions" not in blind and blind["data"]["blind_test"] is True
    for tag in ("MERCHANT", "DATE", "TOTAL", "TAX", 'name="blur"', 'name="iluminacion"', 'name="layout"'):
        assert tag in pl.CONFIG_XML
    import xml.dom.minidom as md
    md.parseString(pl.CONFIG_XML)  # well-formed
