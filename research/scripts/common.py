"""Shared helpers: FUNSD-BIO schema, normalization, paths. Stdlib only."""
from __future__ import annotations

import hashlib
import json
import re
from pathlib import Path

RESEARCH = Path(__file__).resolve().parent.parent
DATA = RESEARCH / "data"
PROCESSED = DATA / "processed"
SPLITS_PATH = RESEARCH / "splits.json"
REPORTS = RESEARCH / "reports"
FIELDS = ["MERCHANT", "DATE", "TOTAL", "TAX"]
LABELS = ["O"] + [f"{p}-{f}" for f in FIELDS for p in ("B", "I")]


def sha256_bytes(b: bytes) -> str:
    return hashlib.sha256(b).hexdigest()


def quad_to_box(quad: dict) -> list[int]:
    xs = [quad[k] for k in ("x1", "x2", "x3", "x4")]
    ys = [quad[k] for k in ("y1", "y2", "y3", "y4")]
    return [int(min(xs)), int(min(ys)), int(max(xs)), int(max(ys))]


def bio(field: str, index: int) -> str:
    return f"{'B' if index == 0 else 'I'}-{field}"


def make_doc(doc_id, source, image, width, height, words, ground_truth, image_sha256=None):
    gt = {f: ground_truth.get(f) for f in FIELDS}
    return {
        "id": doc_id,
        "source": source,
        "image": image,
        "image_sha256": image_sha256,
        "width": width,
        "height": height,
        "words": words,
        "ground_truth": gt,
        "fields_available": [f for f in FIELDS if gt[f] not in (None, "")],
    }


def write_doc(out_dir: Path, doc: dict) -> None:
    out_dir.mkdir(parents=True, exist_ok=True)
    (out_dir / f"{doc['id']}.json").write_text(json.dumps(doc, ensure_ascii=False), encoding="utf-8")


def load_docs(source: str | None = None):
    """Yield processed docs (sorted, deterministic)."""
    for src_dir in sorted(PROCESSED.glob("*")):
        if source and source != "all" and src_dir.name != source:
            continue
        for f in sorted((src_dir / "labels").glob("*.json")):
            yield json.loads(f.read_text(encoding="utf-8"))


# ---------- normalization (shared by eval) ----------
def norm_text(s: str | None) -> str:
    if s is None:
        return ""
    s = re.sub(r"[^\w\s]", " ", s.lower(), flags=re.UNICODE)
    return re.sub(r"\s+", " ", s).strip()


def norm_amount(s) -> float | None:
    """Parse '1,591,600' / '12.50' / 'RM 9,00' / '$1.234,56' -> float.
    Heuristic: if both separators appear the LAST one is the decimal mark; a single separator
    followed by exactly 3 digits (or repeated) is a thousands mark; otherwise it is decimal."""
    if s is None:
        return None
    if isinstance(s, (int, float)):
        return float(s)
    t = re.sub(r"[^\d.,]", "", str(s))
    if not re.search(r"\d", t):
        return None
    t = t.strip(".,")
    if "," in t and "." in t:
        dec = "," if t.rfind(",") > t.rfind(".") else "."
        thou = "." if dec == "," else ","
        t = t.replace(thou, "").replace(dec, ".")
    elif "," in t or "." in t:
        sep = "," if "," in t else "."
        parts = t.split(sep)
        if len(parts) > 2 or (len(parts) == 2 and len(parts[1]) == 3):
            t = "".join(parts)
        else:
            t = parts[0] + "." + parts[1]
    try:
        return float(t)
    except ValueError:
        return None


_MONTHS = {m: i + 1 for i, m in enumerate(
    ["jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"])}


def norm_date(s) -> str | None:
    """Best-effort -> ISO yyyy-mm-dd. Day-first for ambiguous numeric dates (SROIE is Malaysian)."""
    if not s:
        return None
    s = str(s).strip().lower()
    m = re.search(r"(\d{4})[-/.](\d{1,2})[-/.](\d{1,2})", s)
    if m:
        y, mo, d = map(int, m.groups())
        return _iso(y, mo, d)
    m = re.search(r"(\d{1,2})[-/.](\d{1,2})[-/.](\d{2,4})", s)
    if m:
        d, mo, y = map(int, m.groups())
        if y < 100:
            y += 2000
        return _iso(y, mo, d)
    m = re.search(r"(\d{1,2})[\s-]*([a-z]{3})[a-z]*[\s,.-]*(\d{2,4})", s)
    if m and m.group(2) in _MONTHS:
        d, y = int(m.group(1)), int(m.group(3))
        if y < 100:
            y += 2000
        return _iso(y, _MONTHS[m.group(2)], d)
    return None


def _iso(y, mo, d):
    if 1 <= mo <= 12 and 1 <= d <= 31 and 1900 <= y <= 2100:
        return f"{y:04d}-{mo:02d}-{d:02d}"
    return None
