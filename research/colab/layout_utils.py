"""Pure, dependency-free helpers for the LayoutLMv3 NER pipeline (unit-testable without a model)."""
from __future__ import annotations

FIELDS = ["MERCHANT", "DATE", "TOTAL", "TAX"]
LABELS = ["O"] + [f"{p}-{f}" for f in FIELDS for p in ("B", "I")]
LABEL2ID = {l: i for i, l in enumerate(LABELS)}
ID2LABEL = {i: l for l, i in LABEL2ID.items()}

# Fields each source actually annotates (others are unknown, NOT negative).
SOURCE_FIELDS = {
    "cord": ["TOTAL", "TAX"],
    "sroie": ["MERCHANT", "DATE", "TOTAL"],
    # WildReceipt annotates all 4 (a missing field in a doc is a real absence). Known noise: the converter
    # keeps only the FIRST contiguous span per field, so later spans of the same field remain 'O'.
    "wildreceipt": ["MERCHANT", "DATE", "TOTAL", "TAX"],
}


def normalize_box(box, width, height):
    """Pixel [x0,y0,x1,y1] -> ints in 0..1000, clamped, ordered."""
    if width <= 0 or height <= 0:
        raise ValueError("width/height must be positive")
    x0, y0, x1, y1 = box
    def sc(v, d):
        return max(0, min(1000, int(round(1000 * v / d))))
    nx0, nx1 = sorted((sc(x0, width), sc(x1, width)))
    ny0, ny1 = sorted((sc(y0, height), sc(y1, height)))
    return [nx0, ny0, nx1, ny1]


def annotated_fields(doc, mode="source"):
    """Fields whose labels are trustworthy for this doc.
    mode: 'source' (per-source map, falls back to fields_available), 'available'
    (fields_available of the doc), 'none' (all fields, no masking)."""
    if mode == "none":
        return list(FIELDS)
    if mode == "available":
        return list(doc.get("fields_available") or [])
    src = doc.get("source")
    if src in SOURCE_FIELDS:
        return list(SOURCE_FIELDS[src])
    return list(doc.get("fields_available") or [])


def label_field(label):
    return None if label == "O" else label.split("-", 1)[1]


def word_label_ids(words, fields_ok):
    """Per-word label id (-100 for tags of non-annotated fields).
    An 'O' word is only a reliable negative if it could belong to an annotated field; since
    unannotated fields' words are labelled 'O' in the file, we cannot tell them apart. So
    'O' stays 'O' (0) and only explicit B-/I- tags of non-annotated fields are masked (-100)."""
    ids = []
    for w in words:
        lab = w["label"]
        f = label_field(lab)
        ids.append(0 if f is None else (LABEL2ID[lab] if f in fields_ok else -100))
    return ids


def align_labels(word_ids, word_labels, label_all_tokens=False):
    """word_ids: tokenizer.word_ids() (None for special tokens). First subtoken of each word
    gets the label; the rest -100 (or I- version when label_all_tokens)."""
    out, prev = [], None
    for wid in word_ids:
        if wid is None:
            out.append(-100)
        elif wid != prev:
            out.append(word_labels[wid])
        elif label_all_tokens:
            l = word_labels[wid]
            if l > 0 and ID2LABEL[l].startswith("B-"):
                l = LABEL2ID["I-" + ID2LABEL[l][2:]]
            out.append(l)
        else:
            out.append(-100)
        prev = wid
    return out


def build_example(doc, fields_ok):
    """-> (words, normalized boxes, label ids). Drops empty-text words."""
    words, boxes, labs = [], [], []
    ids = word_label_ids(doc["words"], fields_ok)
    for w, lid in zip(doc["words"], ids):
        t = str(w["text"]).strip()
        if not t:
            continue
        words.append(t)
        boxes.append(normalize_box(w["box"], doc["width"], doc["height"]))
        labs.append(lid)
    return words, boxes, labs


def has_all_fields(doc):
    """True when the doc carries all 4 fields (MERCHANT, DATE, TOTAL, TAX) (the 'subset4' reporting set)."""
    return set(FIELDS) <= set(doc.get("fields_available") or []) and set(FIELDS) <= set(annotated_fields(doc))


def extract_spans(tags):
    """BIO tag list -> set of (field, start, end_exclusive). Tolerates I- without B- (starts a span)."""
    spans, cur = set(), None
    for i, t in enumerate(list(tags) + ["O"]):
        f = label_field(t)
        if cur and (f != cur[0] or t.startswith("B-")):
            spans.add((cur[0], cur[1], i))
            cur = None
        if f and cur is None:
            cur = (f, i)
    return spans


def field_report(seqs, fields=FIELDS, mode="source"):
    """seqs: iterable of (doc, gold_tags, pred_tags) with tags aligned per word/first-subtoken (gold
    already without masked positions). Entity-level exact-match P/R/F1 PER FIELD computed ONLY over docs
    that annotate that field. Returns {field: {docs, support, tp, precision, recall, f1}}."""
    rep = {f: {"docs": 0, "support": 0, "tp": 0, "pred": 0} for f in fields}
    for doc, gold, pred in seqs:
        ok = annotated_fields(doc, mode)
        for f in fields:
            if f not in ok:
                continue
            g = {s for s in extract_spans(gold) if s[0] == f}
            p = {s for s in extract_spans(pred) if s[0] == f}
            r = rep[f]
            r["docs"] += 1
            r["support"] += len(g)
            r["pred"] += len(p)
            r["tp"] += len(g & p)
    for f, r in rep.items():
        r["precision"] = r["tp"] / r["pred"] if r["pred"] else 0.0
        r["recall"] = r["tp"] / r["support"] if r["support"] else 0.0
        pr = r["precision"] + r["recall"]
        r["f1"] = 2 * r["precision"] * r["recall"] / pr if pr else 0.0
    return rep
