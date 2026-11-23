"""Pure split logic for the Colab bundle (no model deps, unit-tested).

Training  = splits.json train (CORD+SROIE) + WildReceipt official train MINUS a deterministic carve-out.
Validation = splits.json val + the WildReceipt carve-out (~10% of the official train, by group).
Test       = splits.json test (main).  test_wildreceipt = OFFICIAL WildReceipt test (held out, never trained/validated).
Groups = docs sharing image_sha256 or an identical normalized word-text signature (near-identical receipts);
a group is never split across train/val.
"""
from __future__ import annotations

import hashlib
import random

CARVE_SEED = 42
CARVE_FRAC = 0.10


def text_signature(doc) -> str | None:
    t = " ".join(str(w.get("text", "")).strip().lower() for w in doc.get("words", []) if str(w.get("text", "")).strip())
    return hashlib.sha1(t.encode("utf-8")).hexdigest() if len(t) >= 20 else None


def group_docs(docs: dict[str, dict]) -> list[list[str]]:
    """docs: id -> doc. Union-find on image_sha256 and text signature. Groups sorted, ids sorted."""
    parent = {i: i for i in docs}

    def find(x):
        while parent[x] != x:
            parent[x] = parent[parent[x]]
            x = parent[x]
        return x

    seen: dict[str, str] = {}
    for i in sorted(docs):
        keys = [("s", docs[i].get("image_sha256")), ("t", text_signature(docs[i]))]
        for k in keys:
            if k[1] is None:
                continue
            if k in seen:
                parent[find(i)] = find(seen[k])
            else:
                seen[k] = i
    groups: dict[str, list[str]] = {}
    for i in sorted(docs):
        groups.setdefault(find(i), []).append(i)
    return sorted((sorted(g) for g in groups.values()), key=lambda g: g[0])


def carve_out(docs: dict[str, dict], frac: float = CARVE_FRAC, seed: int = CARVE_SEED):
    """-> (train_ids, val_ids), deterministic for a given (docs, frac, seed); group-atomic."""
    groups = group_docs(docs)
    target = round(frac * len(docs))
    order = list(range(len(groups)))
    random.Random(seed).shuffle(order)
    val: list[str] = []
    for gi in order:
        if len(val) >= target:
            break
        val.extend(groups[gi])
    vs = set(val)
    return sorted(i for i in docs if i not in vs), sorted(val)


def build_colab_splits(main_splits: dict, wild: dict, wild_docs: dict[str, dict],
                       frac: float = CARVE_FRAC, seed: int = CARVE_SEED) -> dict:
    """main_splits: splits.json['splits'] ; wild: splits_extra['sources']['wildreceipt'] ;
    wild_docs: id -> doc for the wildreceipt TRAIN ids (only train docs are needed)."""
    w_train, w_val = carve_out({i: wild_docs[i] for i in wild["train"]}, frac, seed)
    out = {
        "train": list(main_splits["train"]) + w_train,
        "val": list(main_splits["val"]) + w_val,
        "test": list(main_splits["test"]),
        "test_wildreceipt": list(wild["test"]),
    }
    assert_disjoint(out)
    return out


def assert_disjoint(splits: dict):
    names = list(splits)
    for a in range(len(names)):
        for b in range(a + 1, len(names)):
            both = set(splits[names[a]]) & set(splits[names[b]])
            if both:
                raise ValueError(f"leak between {names[a]} and {names[b]}: {sorted(both)[:5]}")
    for n, ids in splits.items():
        if len(ids) != len(set(ids)):
            raise ValueError(f"duplicate ids inside {n}")
