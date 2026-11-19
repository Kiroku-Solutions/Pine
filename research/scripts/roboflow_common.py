"""Shared helpers for the Roboflow ingest: export readers (COCO / YOLOv8), perceptual hash, I/O. No network."""
from __future__ import annotations

import json
from pathlib import Path

import cv2
import numpy as np

IMG_EXT = {".jpg", ".jpeg", ".png", ".bmp", ".webp"}
NEG_TAG = "negative-sample"
SPLIT_ALIASES = {"train": "train", "valid": "valid", "val": "valid", "validation": "valid", "test": "test"}
ROBOFLOW_DIR = Path(__file__).resolve().parent.parent / "data" / "roboflow"


def imread(path) -> np.ndarray:
    img = cv2.imdecode(np.fromfile(str(path), dtype=np.uint8), cv2.IMREAD_COLOR)
    if img is None:
        raise ValueError(f"cannot decode image: {path}")
    return img


def imwrite(path, img) -> None:
    Path(path).parent.mkdir(parents=True, exist_ok=True)
    ok, buf = cv2.imencode(Path(path).suffix or ".jpg", img)
    if not ok:
        raise ValueError(f"cannot encode {path}")
    buf.tofile(str(path))


def phash(img: np.ndarray) -> int:
    """64-bit DCT perceptual hash (own implementation, no imagehash dependency)."""
    g = img if img.ndim == 2 else cv2.cvtColor(img, cv2.COLOR_BGR2GRAY)
    g = cv2.resize(g, (32, 32), interpolation=cv2.INTER_AREA).astype(np.float32)
    low = cv2.dct(g)[:8, :8].flatten()
    bits = low > np.median(low[1:])
    return int("".join("1" if b else "0" for b in bits), 2)


def hamming(a: int, b: int) -> int:
    return bin(a ^ b).count("1")


def norm_split(name: str | None) -> str | None:
    return SPLIT_ALIASES.get((name or "").lower())


def is_negative_tags(tags) -> bool:
    return any(str(t).strip().lower() == NEG_TAG for t in (tags or []))


def read_coco(root: Path) -> list[dict]:
    """Roboflow COCO export: <root>/<split>/_annotations.coco.json (images beside it), or one json at root."""
    recs = []
    jsons = sorted(root.glob("*/_annotations.coco.json")) or sorted(root.glob("_annotations.coco.json"))
    for jp in jsons:
        split = norm_split(jp.parent.name) if jp.parent != root else None
        data = json.loads(jp.read_text(encoding="utf-8"))
        boxes: dict[int, list] = {}
        for a in data.get("annotations", []):
            x, y, w, h = a["bbox"]
            boxes.setdefault(a["image_id"], []).append([float(x), float(y), float(w), float(h)])
        for im in data.get("images", []):
            extra = im.get("extra") or {}
            tags = im.get("tags") or extra.get("tags") or []
            recs.append({"name": Path(im["file_name"]).name, "path": jp.parent / im["file_name"], "split": split,
                         "width": im.get("width"), "height": im.get("height"),
                         "boxes": boxes.get(im["id"], []), "tags": tags})
    return recs


def read_yolo(root: Path) -> list[dict]:
    """YOLOv8 export: <root>/<split>/images + labels ('cls cx cy w h' normalized); or <root>/images + labels."""
    recs = []
    layouts = [(norm_split(d.name), d) for d in sorted(root.iterdir()) if d.is_dir() and (d / "images").is_dir()]
    if (root / "images").is_dir():
        layouts.append((None, root))
    for split, d in layouts:
        for ip in sorted((d / "images").iterdir()):
            if ip.suffix.lower() not in IMG_EXT:
                continue
            h, w = imread(ip).shape[:2]
            lp = d / "labels" / (ip.stem + ".txt")
            boxes = []
            if lp.exists():
                for line in lp.read_text(encoding="utf-8").splitlines():
                    p = line.split()
                    if len(p) >= 5:
                        cx, cy, bw, bh = (float(v) for v in p[1:5])
                        boxes.append([(cx - bw / 2) * w, (cy - bh / 2) * h, bw * w, bh * h])
            recs.append({"name": ip.name, "path": ip, "split": split, "width": w, "height": h,
                         "boxes": boxes, "tags": []})
    return recs


def detect_format(root: Path) -> str:
    if any(root.glob("*/_annotations.coco.json")) or (root / "_annotations.coco.json").exists():
        return "coco"
    if (root / "images").is_dir() or any((d / "images").is_dir() for d in root.iterdir() if d.is_dir()):
        return "yolo"
    raise SystemExit(f"{root}: neither a COCO (_annotations.coco.json) nor a YOLOv8 (<split>/images + labels) export")


def read_export(root: Path, fmt: str = "auto") -> list[dict]:
    fmt = detect_format(root) if fmt == "auto" else fmt
    return read_coco(root) if fmt == "coco" else read_yolo(root)
