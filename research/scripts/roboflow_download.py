"""Get the user's Roboflow export onto disk. Two modes:
  --from-dir <folder>   validate (and optionally copy) an export already downloaded by hand (COCO or YOLOv8)
  --api                 download with the roboflow SDK; key ONLY from env ROBOFLOW_API_KEY (never an argument, never logged)
Usage:
  python scripts/roboflow_download.py --from-dir D:\\export [--dest data/roboflow/raw] [--copy]
  python scripts/roboflow_download.py --api --workspace W --project P --version 3 [--format coco]
"""
from __future__ import annotations

import argparse
import os
import shutil
from pathlib import Path

from roboflow_common import ROBOFLOW_DIR, detect_format, read_export


def summarize(root: Path, fmt: str = "auto") -> str:
    by: dict = {}
    for r in read_export(root, fmt):
        c = by.setdefault(r["split"] or "(no split)", [0, 0])
        c[0] += 1
        c[1] += 0 if r["boxes"] else 1
    return "; ".join(f"{k}: {a} images ({b} without boxes)" for k, (a, b) in sorted(by.items())) or "no images found"


def from_dir(src: Path, dest: Path, copy: bool) -> Path:
    if not src.is_dir():
        raise SystemExit(f"--from-dir: not a folder: {src}")
    fmt = detect_format(src)  # exits with a clear message if unrecognized
    if copy:
        if dest.exists() and any(dest.iterdir()):
            raise SystemExit(f"{dest} already has files; refusing to overwrite. Remove it or omit --copy.")
        shutil.copytree(src, dest, dirs_exist_ok=True)
        src = dest
    print(f"format={fmt} root={src}")
    print(summarize(src, fmt))
    return src


def from_api(workspace: str, project: str, version: int, fmt: str, dest: Path) -> Path:
    key = os.environ.get("ROBOFLOW_API_KEY", "").strip()
    if not key:
        raise SystemExit("ROBOFLOW_API_KEY is not set. Set it in your shell (see research/docs/roboflow_ingest.md); "
                         "it is never read from arguments or files, and never printed.")
    try:
        from roboflow import Roboflow
    except ImportError:
        raise SystemExit("roboflow SDK missing: pip install -r research/requirements-roboflow.txt")
    dest.mkdir(parents=True, exist_ok=True)
    print(f"downloading {workspace}/{project} v{version} ({fmt}) -> {dest}  (API key taken from env, not shown)")
    try:
        ds = Roboflow(api_key=key).workspace(workspace).project(project).version(version).download(
            "coco" if fmt == "coco" else "yolov8", location=str(dest), overwrite=False)
    except Exception as e:  # never echo anything that could contain the key
        raise SystemExit(f"Roboflow download failed: {type(e).__name__}: {str(e).replace(key, '***')}")
    root = Path(getattr(ds, "location", dest))
    print(summarize(root))
    return root


def main(argv=None):
    ap = argparse.ArgumentParser()
    g = ap.add_mutually_exclusive_group(required=True)
    g.add_argument("--from-dir", type=Path)
    g.add_argument("--api", action="store_true")
    ap.add_argument("--dest", type=Path, default=ROBOFLOW_DIR / "raw")
    ap.add_argument("--copy", action="store_true", help="--from-dir: copy into --dest instead of using in place")
    ap.add_argument("--workspace")
    ap.add_argument("--project")
    ap.add_argument("--version", type=int)
    ap.add_argument("--format", choices=["coco", "yolov8"], default="coco")
    a = ap.parse_args(argv)
    if a.api:
        if not (a.workspace and a.project and a.version):
            ap.error("--api needs --workspace, --project and --version")
        from_api(a.workspace, a.project, a.version, a.format, a.dest)
    else:
        from_dir(a.from_dir, a.dest, a.copy)


if __name__ == "__main__":
    main()
