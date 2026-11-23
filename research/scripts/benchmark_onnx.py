"""PHASE 3 benchmark for the LayoutLMv3 ONNX models, on THIS machine's CPU.

Measures exactly what the paper asks: per-document CPU latency (median and P95), peak RAM, model size.
Each model runs in a FRESH child process so the peak RSS is that model's alone and the numbers are
not contaminated by a previous model (peak RSS is sampled by a thread at ~20 ms; it is an
approximation of the peak working set, not a hardware counter).

Input shapes are the real ones (ids/bbox/mask [1,512], pixel_values [1,3,224,224]); the graph is
exported with a padded length of 512, so latency does not depend on document content. This measures the
MODEL only: image decode, OCR, tokenisation and decoding are NOT included (they are measured in Phase 4,
inside Pine). Say so in the paper.

Usage: python scripts/benchmark_onnx.py models/onnx/model_fp32.onnx models/onnx/model_int8_emb.onnx
       [--runs 50] [--warmup 5] [--threads 0] [--out reports/benchmark_onnx.json]
"""
from __future__ import annotations

import argparse
import json
import os
import platform
import statistics
import subprocess
import sys
import threading
import time
from pathlib import Path


def child(path: str, runs: int, warmup: int, threads: int) -> None:
    import numpy as np
    import onnxruntime as ort
    import psutil

    proc = psutil.Process()
    peak = [proc.memory_info().rss]
    stop = threading.Event()

    def sample():
        while not stop.is_set():
            peak[0] = max(peak[0], proc.memory_info().rss)
            time.sleep(0.02)

    t = threading.Thread(target=sample, daemon=True)
    t.start()
    so = ort.SessionOptions()
    if threads > 0:
        so.intra_op_num_threads = threads
    t0 = time.perf_counter()
    s = ort.InferenceSession(path, so, providers=["CPUExecutionProvider"])
    load_s = time.perf_counter() - t0
    rng = np.random.default_rng(0)
    feed = {"input_ids": rng.integers(5, 1000, (1, 512), dtype=np.int64),
            "bbox": np.sort(rng.integers(0, 1000, (1, 512, 4), dtype=np.int64).reshape(1, 512, 2, 2), axis=2).reshape(1, 512, 4),
            "attention_mask": np.ones((1, 512), dtype=np.int64),
            "pixel_values": rng.standard_normal((1, 3, 224, 224)).astype(np.float32)}
    for _ in range(warmup):
        s.run(["logits"], feed)
    lat = []
    for _ in range(runs):
        a = time.perf_counter()
        s.run(["logits"], feed)
        lat.append(time.perf_counter() - a)
    stop.set()
    t.join()
    peak[0] = max(peak[0], proc.memory_info().rss)
    lat.sort()
    p95 = lat[min(len(lat) - 1, int(round(0.95 * (len(lat) - 1))))]
    print(json.dumps({"load_s": round(load_s, 3), "median_s": round(statistics.median(lat), 4),
                      "p95_s": round(p95, 4), "min_s": round(lat[0], 4), "max_s": round(lat[-1], 4),
                      "peak_rss_mb": round(peak[0] / 1e6, 1), "runs": runs, "ort": ort.__version__,
                      "threads": threads or os.cpu_count()}))


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("models", nargs="*")
    ap.add_argument("--runs", type=int, default=50)
    ap.add_argument("--warmup", type=int, default=5)
    ap.add_argument("--threads", type=int, default=0, help="0 = onnxruntime default (all cores)")
    ap.add_argument("--out", default="reports/benchmark_onnx.json")
    ap.add_argument("--child", help=argparse.SUPPRESS)
    a = ap.parse_args()
    if a.child:
        child(a.child, a.runs, a.warmup, a.threads)
        return
    if not a.models:
        ap.error("give at least one .onnx")
    res = {"machine": {"platform": platform.platform(), "processor": platform.processor(),
                       "cpu_count": os.cpu_count(), "python": platform.python_version()}, "models": {}}
    for m in a.models:
        p = Path(m)
        print(f"== {p.name}", flush=True)
        r = subprocess.run([sys.executable, "-I", __file__, "--child", str(p), "--runs", str(a.runs),
                            "--warmup", str(a.warmup), "--threads", str(a.threads)],
                           capture_output=True, text=True)
        if r.returncode != 0:
            res["models"][p.name] = {"error": r.stderr[-500:]}
            print("FAILED:", r.stderr[-300:])
            continue
        d = json.loads(r.stdout.strip().splitlines()[-1])
        d["size_mb"] = round(p.stat().st_size / 1e6, 1)
        res["models"][p.name] = d
        print(f"  size {d['size_mb']} MB | median {d['median_s']*1000:.0f} ms | p95 {d['p95_s']*1000:.0f} ms | "
              f"peak RSS {d['peak_rss_mb']} MB", flush=True)
    out = Path(a.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(res, indent=1), encoding="utf-8")
    print("saved", out)


if __name__ == "__main__":
    main()
