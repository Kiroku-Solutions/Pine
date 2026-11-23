"""Build Label Studio import tasks (with pre-annotations) from OCR words+boxes (ocr_dump.py shape / Pine OcrWord).

Per image <ocr_dir>/<id>.json: {"words":[{"text","confidence","box":{"x","y","width","height"}}], "width","height"}.
Heuristic pre-labels (cheap, deliberately simple; the human corrects them):
  DATE: regex match; TOTAL: amount on/after a 'total' line (else largest amount); TAX: amount on/after tax/vat/iva line;
  MERCHANT: top-most non-numeric line.
Items of the frozen test (--test-list test_to_verify.json) get NO predictions: they are verified fully by hand.
BIAS (document in the thesis): pre-labeled train rounds are anchored on the OCR/heuristic (annotators tend to accept
suggestions), so a model trained on them can inherit that bias; the test is therefore labeled blind, from scratch.
Quality attributes blur/iluminacion/layout are prefilled from quality_tags (editable Choices).
Usage: python scripts/prelabel_for_labelstudio.py --round data/roboflow/labeling/round1.json --ocr-dir data/ocr
          [--test-list ...test_to_verify.json] --out tasks_round1.json [--config-out labelstudio_config.xml]
"""
from __future__ import annotations

import argparse
import json
import re
from pathlib import Path

from roboflow_common import ROBOFLOW_DIR, imread

LABELS = ["MERCHANT", "DATE", "TOTAL", "TAX"]
AMOUNT = re.compile(r"(?<![\d.,])\d{1,3}(?:[.,]\d{3})*[.,]\d{2}(?![\d])|(?<![\d.,])\d+[.,]\d{2}(?![\d])")
DATE = re.compile(r"\b(\d{1,2}[/.-]\d{1,2}[/.-]\d{2,4}|\d{4}[/.-]\d{1,2}[/.-]\d{1,2}|"
                  r"\d{1,2}\s+(?:jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)[a-z]*\.?\s+\d{2,4}|"
                  r"(?:jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)[a-z]*\.?\s+\d{1,2},?\s+\d{2,4})\b", re.I)

CONFIG_XML = """<View>
  <Image name="image" value="$image" zoom="true"/>
  <RectangleLabels name="label" toName="image">
    <Label value="MERCHANT" background="#1f77b4"/>
    <Label value="DATE" background="#2ca02c"/>
    <Label value="TOTAL" background="#d62728"/>
    <Label value="TAX" background="#ff7f0e"/>
  </RectangleLabels>
  <Header value="Image attributes (prefilled, correct if wrong)"/>
  <Choices name="blur" toName="image" choice="single-radio" perRegion="false">
    <Choice value="sharp"/><Choice value="blurry"/>
  </Choices>
  <Choices name="iluminacion" toName="image" choice="single-radio" perRegion="false">
    <Choice value="ok"/><Choice value="dark"/><Choice value="bright"/><Choice value="low_contrast"/>
  </Choices>
  <Choices name="layout" toName="image" choice="single-radio" perRegion="false">
    <Choice value="standard"/><Choice value="long_narrow"/><Choice value="wide"/><Choice value="small"/>
  </Choices>
</View>
"""


def _num(s: str) -> float:
    s = s.replace(" ", "")
    sep = max(s.rfind("."), s.rfind(","))
    return float(re.sub(r"[.,]", "", s[:sep]) + "." + s[sep + 1:])


def _rect(w, label, W, H, score=None):
    b = w["box"]
    r = {"from_name": "label", "to_name": "image", "type": "rectanglelabels", "original_width": W,
         "original_height": H, "image_rotation": 0,
         "value": {"x": 100 * b["x"] / W, "y": 100 * b["y"] / H, "width": 100 * b["width"] / W,
                   "height": 100 * b["height"] / H, "rotation": 0, "rectanglelabels": [label]}}
    if score is not None:
        r["score"] = score
    return r


def heuristic_labels(words: list[dict]) -> list[tuple[dict, str]]:
    out: list[tuple[dict, str]] = []
    ws = sorted(words, key=lambda w: (w["box"]["y"], w["box"]["x"]))
    used = set()

    def pick(i, label):
        if i not in used:
            used.add(i)
            out.append((ws[i], label))
    for i, w in enumerate(ws):  # MERCHANT: first line with letters and no amount/date
        t = w["text"]
        if re.search(r"[A-Za-z]{3}", t) and not AMOUNT.search(t) and not DATE.search(t):
            pick(i, "MERCHANT")
            break
    for i, w in enumerate(ws):
        if DATE.search(w["text"]):
            pick(i, "DATE")
            break

    def amount_near(keys, exclude=()):
        for i, w in enumerate(ws):
            tl = w["text"].lower()
            if any(k in tl for k in keys) and not any(e in tl for e in exclude):
                for j in range(i, min(i + 4, len(ws))):
                    if AMOUNT.search(ws[j]["text"]):
                        return j
        return None
    j = amount_near(["total", "amount due", "balance"], exclude=["subtotal", "sub total", "sub-total"])
    if j is None:
        amts = [(_num(AMOUNT.search(w['text']).group()), i) for i, w in enumerate(ws) if AMOUNT.search(w['text'])]
        j = max(amts)[1] if amts else None
    if j is not None:
        pick(j, "TOTAL")
    t = amount_near(["tax", "vat", "iva", "gst"])
    if t is not None and t != j:
        pick(t, "TAX")
    return out


def make_task(item: dict, ocr: dict | None, image_url: str, blind: bool, crop_path: Path | None) -> dict:
    data = {"image": image_url, "id": item["id"], "group": item.get("group"), "blind_test": blind}
    task = {"data": data, "meta": {"id": item["id"], "blind_test": blind}}
    W = (ocr or {}).get("width")
    H = (ocr or {}).get("height")
    choices = []
    if crop_path is not None and crop_path.exists():
        from quality_tags import tag_image
        q = tag_image(imread(crop_path))
        W, H = W or q["width"], H or q["height"]
        choices = [("blur", q["blur_tag"]), ("iluminacion", q["illumination"]["tag"]), ("layout", q["layout"])]
    if blind or not ocr or not W:
        return task  # test / no OCR: nothing prefilled, human labels from scratch
    result = [_rect(w, lab, W, H, round(float(w.get("confidence", 1.0)), 3)) for w, lab in heuristic_labels(ocr["words"])]
    result += [{"from_name": n, "to_name": "image", "type": "choices", "value": {"choices": [v]}} for n, v in choices]
    task["predictions"] = [{"model_version": "ocr-heuristic-v1", "result": result}]
    return task


def main(argv=None):
    ap = argparse.ArgumentParser()
    ap.add_argument("--round", type=Path, required=True, help="roundN.json or test_to_verify.json")
    ap.add_argument("--ocr-dir", type=Path, default=Path("data/ocr"))
    ap.add_argument("--crops-root", type=Path, default=ROBOFLOW_DIR)
    ap.add_argument("--test-list", type=Path, help="test_to_verify.json: its ids are forced blind (no predictions)")
    ap.add_argument("--url-prefix", default="/data/local-files/?d=", help="Label Studio local-files URL prefix")
    ap.add_argument("--out", type=Path, required=True)
    ap.add_argument("--config-out", type=Path)
    a = ap.parse_args(argv)
    spec = json.loads(a.round.read_text(encoding="utf-8"))
    test_ids = {i["id"] for i in json.loads(a.test_list.read_text(encoding="utf-8"))["items"]} if a.test_list else set()
    is_test = bool(spec.get("frozen"))
    tasks = []
    for it in spec["items"]:
        op = a.ocr_dir / f"{it['id']}.json"
        ocr = json.loads(op.read_text(encoding="utf-8")) if op.exists() else None
        blind = is_test or it["id"] in test_ids
        tasks.append(make_task(it, ocr, a.url_prefix + it["crop"], blind, a.crops_root / it["crop"]))
    a.out.write_text(json.dumps(tasks, indent=1), encoding="utf-8")
    if a.config_out:
        a.config_out.write_text(CONFIG_XML, encoding="utf-8")
    print(f"{len(tasks)} tasks ({sum(1 for t in tasks if 'predictions' in t)} with predictions) -> {a.out}")


if __name__ == "__main__":
    main()
