"""Stratified sampling for manual labeling + frozen test, BEFORE any labeling (fixed seed).
Strata = blur tercile x luminance tercile (9 cells), from quality_tags.blur_score / illumination (imported, not edited).
One representative crop per dedup group is used so near-duplicates cannot straddle labeling/test.
Order: 1) test_n images frozen -> test_to_verify.json; 2) rounds 200/250/250 drawn from the remaining pool, each
stratified proportionally. Terciles are computed on the whole pool.
Usage: python scripts/sample_for_labeling.py [--manifest data/roboflow/manifest.json] [--out data/roboflow/labeling]
"""
from __future__ import annotations

import argparse
import json
import random
from pathlib import Path

import numpy as np

from quality_tags import blur_score, illumination
from roboflow_common import ROBOFLOW_DIR, imread


def tercile(v, edges):
    return int(v > edges[0]) + int(v > edges[1])


def deal(strata: dict, n: int, rng: random.Random) -> list:
    """Take n items proportionally across strata (largest remainder), shuffled within each stratum; removes them."""
    total = sum(len(v) for v in strata.values())
    n = min(n, total)
    if n == 0:
        return []
    quota = {k: len(v) * n / total for k, v in strata.items()}
    take = {k: int(q) for k, q in quota.items()}
    for k in sorted(quota, key=lambda k: (quota[k] - take[k], k), reverse=True)[: n - sum(take.values())]:
        take[k] += 1
    out = []
    for k in sorted(strata):
        rng.shuffle(strata[k])
        out += [strata[k].pop() for _ in range(min(take[k], len(strata[k])))]
    return out


def sample(items: list[dict], root: Path, rounds=(200, 250, 250), test_n=150, seed=42) -> dict:
    rng = random.Random(seed)
    reps: dict = {}
    for it in sorted(items, key=lambda i: i["id"]):
        reps.setdefault(it["group"], it)
    pool = list(reps.values())
    feats = []
    for it in pool:
        img = imread(root / it["crop"])
        feats.append((blur_score(img), illumination(img)["mean"]))
    be = np.percentile([f[0] for f in feats], [100 / 3, 200 / 3]) if feats else [0, 0]
    le = np.percentile([f[1] for f in feats], [100 / 3, 200 / 3]) if feats else [0, 0]
    strata: dict[str, list] = {}
    for it, (b, lum) in zip(pool, feats):
        it = dict(it, blur_score=b, lum_mean=lum, blur_bin=tercile(b, be), lum_bin=tercile(lum, le))
        strata.setdefault(f"b{it['blur_bin']}l{it['lum_bin']}", []).append(it)
    test = deal(strata, test_n, rng)
    return {"seed": seed, "pool_groups": len(pool), "test": test, "rounds": [deal(strata, n, rng) for n in rounds]}


def main(argv=None):
    ap = argparse.ArgumentParser()
    ap.add_argument("--manifest", type=Path, default=ROBOFLOW_DIR / "manifest.json")
    ap.add_argument("--out", type=Path, default=ROBOFLOW_DIR / "labeling")
    ap.add_argument("--seed", type=int, default=42)
    ap.add_argument("--test-n", type=int, default=150)
    ap.add_argument("--rounds", type=int, nargs="+", default=[200, 250, 250])
    a = ap.parse_args(argv)
    tp = a.out / "test_to_verify.json"
    if tp.exists():
        raise SystemExit(f"{tp} exists: the test set is frozen, refusing to resample. Delete it only on purpose.")
    items = json.loads(a.manifest.read_text(encoding="utf-8"))["items"]
    res = sample(items, a.manifest.parent, tuple(a.rounds), a.test_n, a.seed)
    a.out.mkdir(parents=True, exist_ok=True)
    keep = ("id", "crop", "group", "split", "blur_score", "lum_mean", "blur_bin", "lum_bin")

    def slim(lst):
        return [{k: i[k] for k in keep} for i in lst]
    tp.write_text(json.dumps({"seed": a.seed, "frozen": True, "verify": "full manual, NO prelabel",
                              "items": slim(res["test"])}, indent=1), encoding="utf-8")
    for n, r in enumerate(res["rounds"], 1):
        (a.out / f"round{n}.json").write_text(json.dumps({"seed": a.seed, "round": n, "items": slim(r)}, indent=1),
                                              encoding="utf-8")
    print(f"pool={res['pool_groups']} test={len(res['test'])} rounds={[len(r) for r in res['rounds']]} -> {a.out}")


if __name__ == "__main__":
    main()
