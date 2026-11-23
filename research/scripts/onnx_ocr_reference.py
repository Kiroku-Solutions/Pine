"""Reference implementation of the ONNX OCR pipeline planned for Pine (docs/65-Onnx-Ocr-Design.md).

PP-OCRv5 detection (DB) + recognition (CTC) as ONNX models, with ALL pre/post-processing written
so that it ports 1:1 to managed C# on SkiaSharp (no OpenCV, no pyclipper, no shapely):

  det  : resize (long side <= 960, multiples of 32) -> BGR, (x/255 - mean)/std -> NCHW float
         -> DB probability map -> threshold 0.3 -> 8-connected components
         -> convex hull -> minimum-area rectangle (rotating calipers)
         -> mean probability inside the rectangle >= box_thresh -> "unclip" -> sort
  rec  : crop the (possibly rotated) rectangle with a perspective/affine warp, height 48,
         width = ceil(48*w/h) (min 320 like PaddleOCR), (x/255 - .5)/.5 -> CTC greedy decode

Managed-friendly replacements for the OpenCV / Clipper calls PaddleOCR uses
  cv2.findContours        -> connected components (holes are ignored; they only produce tiny boxes)
  cv2.minAreaRect         -> convex hull + rotating calipers (exact)
  pyclipper unclip(1.5)   -> for a RECTANGLE the round-join offset by d = area*ratio/perimeter has the
                             minimum-area rectangle (w+2d) x (h+2d): closed form, exact
  cv2.resize(linear)      -> half-pixel bilinear (numpy here; SKSamplingOptions(Linear) in C#)
  cv2.warpPerspective     -> inverse-mapped bilinear sampling (homography from 4 points)

Only numpy + pillow + onnxruntime are needed.  Channel order matters: PaddleOCR feeds BGR arrays
(cv2 decode) to BOTH models, with the ImageNet mean/std applied to the BGR channels as-is.

Usage:
  python onnx_ocr_reference.py run  --det det.onnx --rec rec.onnx --dict dict.txt --ids a,b,c --out out.json
  python onnx_ocr_reference.py cmp  --a baseline.json --b out.json
Dictionary file: one character per line; index 0 (CTC blank) is implicit, a trailing " " is appended
when --space is given (PaddleOCR's use_space_char).
"""
from __future__ import annotations

import argparse
import json
import math
import sys
import time
from pathlib import Path

import numpy as np
from PIL import Image

PROCESSED = Path(__file__).resolve().parents[1] / "data" / "processed"

# ---------------------------------------------------------------- resize / warp

def resize_bilinear(img: np.ndarray, new_w: int, new_h: int) -> np.ndarray:
    """Half-pixel-centre bilinear, same sampling as cv2.INTER_LINEAR (no antialias)."""
    h, w = img.shape[:2]
    if (h, w) == (new_h, new_w):
        return img.astype(np.float32)
    xs = (np.arange(new_w) + 0.5) * (w / new_w) - 0.5
    ys = (np.arange(new_h) + 0.5) * (h / new_h) - 0.5
    x0 = np.clip(np.floor(xs).astype(int), 0, w - 1)
    y0 = np.clip(np.floor(ys).astype(int), 0, h - 1)
    x1 = np.clip(x0 + 1, 0, w - 1)
    y1 = np.clip(y0 + 1, 0, h - 1)
    fx = np.clip(xs - x0, 0, 1)[None, :, None]
    fy = np.clip(ys - y0, 0, 1)[:, None, None]
    f = img.astype(np.float32)
    top = f[y0][:, x0] * (1 - fx) + f[y0][:, x1] * fx
    bot = f[y1][:, x0] * (1 - fx) + f[y1][:, x1] * fx
    return top * (1 - fy) + bot * fy


def homography(src: np.ndarray, dst: np.ndarray) -> np.ndarray:
    a = []
    for (x, y), (u, v) in zip(src, dst):
        a.append([x, y, 1, 0, 0, 0, -u * x, -u * y, -u])
        a.append([0, 0, 0, x, y, 1, -v * x, -v * y, -v])
    _, _, vt = np.linalg.svd(np.array(a, dtype=np.float64))
    h = vt[-1].reshape(3, 3)
    return h / h[2, 2]


def warp_rect(img: np.ndarray, quad: np.ndarray, out_w: int, out_h: int) -> np.ndarray:
    """Map the output rectangle back into `img` through the homography quad->rect (bilinear, edge clamp)."""
    dst = np.array([[0, 0], [out_w, 0], [out_w, out_h], [0, out_h]], dtype=np.float64)
    m = homography(dst, quad.astype(np.float64))  # output -> source
    ys, xs = np.mgrid[0:out_h, 0:out_w]
    pts = np.stack([xs + 0.5, ys + 0.5, np.ones_like(xs)], -1).reshape(-1, 3) @ m.T
    sx = pts[:, 0] / pts[:, 2] - 0.5
    sy = pts[:, 1] / pts[:, 2] - 0.5
    h, w = img.shape[:2]
    x0 = np.clip(np.floor(sx).astype(int), 0, w - 1)
    y0 = np.clip(np.floor(sy).astype(int), 0, h - 1)
    x1 = np.clip(x0 + 1, 0, w - 1)
    y1 = np.clip(y0 + 1, 0, h - 1)
    fx = np.clip(sx - x0, 0, 1)[:, None]
    fy = np.clip(sy - y0, 0, 1)[:, None]
    f = img.astype(np.float32)
    out = (f[y0, x0] * (1 - fx) + f[y0, x1] * fx) * (1 - fy) + (f[y1, x0] * (1 - fx) + f[y1, x1] * fx) * fy
    return out.reshape(out_h, out_w, -1)

# ---------------------------------------------------------------- geometry

def convex_hull(pts: np.ndarray) -> np.ndarray:
    p = sorted(set(map(tuple, pts.tolist())))
    if len(p) <= 2:
        return np.array(p, dtype=np.float64)

    def cross(o, a, b):
        return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0])

    lo = []
    for q in p:
        while len(lo) >= 2 and cross(lo[-2], lo[-1], q) <= 0:
            lo.pop()
        lo.append(q)
    up = []
    for q in reversed(p):
        while len(up) >= 2 and cross(up[-2], up[-1], q) <= 0:
            up.pop()
        up.append(q)
    return np.array(lo[:-1] + up[:-1], dtype=np.float64)


def min_area_rect(pts: np.ndarray):
    """Exact minimum-area bounding rectangle of a point set. Returns (corners[4,2], (w,h))."""
    hull = convex_hull(pts)
    if len(hull) == 1:
        return np.repeat(hull, 4, axis=0), (0.0, 0.0)
    best = None
    n = len(hull)
    for i in range(n):
        e = hull[(i + 1) % n] - hull[i]
        ln = math.hypot(*e)
        if ln == 0:
            continue
        ux, uy = e / ln
        vx, vy = -uy, ux
        a = hull[:, 0] * ux + hull[:, 1] * uy
        b = hull[:, 0] * vx + hull[:, 1] * vy
        w, h = a.max() - a.min(), b.max() - b.min()
        if best is None or w * h < best[0]:
            best = (w * h, ux, uy, vx, vy, a.min(), a.max(), b.min(), b.max())
    _, ux, uy, vx, vy, a0, a1, b0, b1 = best
    c = np.array([[a0, b0], [a1, b0], [a1, b1], [a0, b1]])
    corners = np.stack([c[:, 0] * ux + c[:, 1] * vx, c[:, 0] * uy + c[:, 1] * vy], 1)
    return corners, (a1 - a0, b1 - b0)


def order_box(corners: np.ndarray) -> np.ndarray:
    """tl, tr, br, bl exactly as PaddleOCR's get_mini_boxes."""
    p = sorted(corners.tolist(), key=lambda q: q[0])
    i1, i4 = (0, 1) if p[1][1] > p[0][1] else (1, 0)
    i2, i3 = (2, 3) if p[3][1] > p[2][1] else (3, 2)
    return np.array([p[i1], p[i2], p[i3], p[i4]], dtype=np.float64)


def connected_components(mask: np.ndarray):
    """8-connected components of a boolean mask; yields arrays of (x, y) pixel coordinates."""
    h, w = mask.shape
    seen = np.zeros_like(mask, dtype=bool)
    ys, xs = np.nonzero(mask)
    for y0, x0 in zip(ys.tolist(), xs.tolist()):
        if seen[y0, x0]:
            continue
        stack = [(y0, x0)]
        seen[y0, x0] = True
        comp = []
        while stack:
            y, x = stack.pop()
            comp.append((x, y))
            for dy in (-1, 0, 1):
                yy = y + dy
                if yy < 0 or yy >= h:
                    continue
                for dx in (-1, 0, 1):
                    xx = x + dx
                    if 0 <= xx < w and mask[yy, xx] and not seen[yy, xx]:
                        seen[yy, xx] = True
                        stack.append((yy, xx))
        yield np.array(comp, dtype=np.float64)


def point_in_quad_mask(quad: np.ndarray, x0: int, y0: int, x1: int, y1: int) -> np.ndarray:
    """Pixels (inclusive window) whose centre lies inside the convex quad (cv2.fillPoly stand-in)."""
    ys, xs = np.mgrid[y0:y1 + 1, x0:x1 + 1]
    inside = np.ones(xs.shape, dtype=bool)
    sign = 1 if np.cross(quad[1] - quad[0], quad[2] - quad[1]) >= 0 else -1
    for i in range(4):
        a, b = quad[i], quad[(i + 1) % 4]
        cr = (b[0] - a[0]) * (ys - a[1]) - (b[1] - a[1]) * (xs - a[0])
        inside &= (cr * sign) >= -0.5 * math.hypot(*(b - a))  # half-pixel tolerance ~ fillPoly boundary
    return inside

# ---------------------------------------------------------------- detection

MEAN = np.array([0.485, 0.456, 0.406], dtype=np.float32)
STD = np.array([0.229, 0.224, 0.225], dtype=np.float32)


def det_preprocess(bgr: np.ndarray, limit: int = 960):
    h, w = bgr.shape[:2]
    ratio = limit / max(h, w) if max(h, w) > limit else 1.0
    rh = max(int(round(int(h * ratio) / 32) * 32), 32)
    rw = max(int(round(int(w * ratio) / 32) * 32), 32)
    img = resize_bilinear(bgr, rw, rh)
    x = (img / 255.0 - MEAN) / STD
    return x.transpose(2, 0, 1)[None].astype(np.float32), (h, w, rh, rw)


def det_postprocess(prob: np.ndarray, shape, thresh=0.3, box_thresh=0.6, unclip=1.5, min_size=3, max_cand=1000):
    h, w, rh, rw = shape
    boxes, scores = [], []
    comps = list(connected_components(prob > thresh))[:max_cand]
    for comp in comps:
        corners, (rw_, rh_) = min_area_rect(comp)
        if min(rw_, rh_) < min_size:
            continue
        quad = order_box(corners)
        x0 = max(0, min(int(math.floor(quad[:, 0].min())), rw - 1))
        x1 = max(0, min(int(math.ceil(quad[:, 0].max())), rw - 1))
        y0 = max(0, min(int(math.floor(quad[:, 1].min())), rh - 1))
        y1 = max(0, min(int(math.ceil(quad[:, 1].max())), rh - 1))
        m = point_in_quad_mask(quad, x0, y0, x1, y1)
        if m.sum() == 0:
            continue
        score = float(prob[y0:y1 + 1, x0:x1 + 1][m].mean())
        if score < box_thresh:
            continue
        # closed-form unclip of a rectangle (round joins): grow every side by d
        area, per = rw_ * rh_, 2 * (rw_ + rh_)
        d = area * unclip / per
        c = corners.mean(0)
        # corners are (a-extent, b-extent) rectangle; grow along its own axes
        axis_a = corners[1] - corners[0]
        axis_b = corners[3] - corners[0]
        na, nb = axis_a / (np.linalg.norm(axis_a) or 1), axis_b / (np.linalg.norm(axis_b) or 1)
        hw, hh = rw_ / 2 + d, rh_ / 2 + d
        big = np.array([c - na * hw - nb * hh, c + na * hw - nb * hh, c + na * hw + nb * hh, c - na * hw + nb * hh])
        qb = order_box(big)
        if min(2 * hw, 2 * hh) < min_size + 2:
            continue
        qb[:, 0] = np.clip(np.round(qb[:, 0] * (w / rw)), 0, w)
        qb[:, 1] = np.clip(np.round(qb[:, 1] * (h / rh)), 0, h)
        boxes.append(qb)
        scores.append(score)
    # PaddleOCR sorted_boxes: top-to-bottom, left-to-right with a 10 px row tolerance
    order = sorted(range(len(boxes)), key=lambda i: (boxes[i][0][1], boxes[i][0][0]))
    for i in range(len(order) - 1):
        for j in range(i, -1, -1):
            a, b = boxes[order[j]], boxes[order[j + 1]]
            if abs(b[0][1] - a[0][1]) < 10 and b[0][0] < a[0][0]:
                order[j], order[j + 1] = order[j + 1], order[j]
            else:
                break
    return [boxes[i] for i in order], [scores[i] for i in order]

# ---------------------------------------------------------------- recognition

def crop_line(bgr: np.ndarray, quad: np.ndarray) -> np.ndarray:
    w = int(max(np.linalg.norm(quad[0] - quad[1]), np.linalg.norm(quad[2] - quad[3])))
    h = int(max(np.linalg.norm(quad[0] - quad[3]), np.linalg.norm(quad[1] - quad[2])))
    w, h = max(w, 1), max(h, 1)
    out = warp_rect(bgr, quad, w, h)
    if h / w >= 1.5:
        out = np.rot90(out)
    return out


def rec_preprocess(crop: np.ndarray, img_h=48, min_w=320, max_w=3200):
    h, w = crop.shape[:2]
    ratio = w / float(h)
    tw = min(max(int(img_h * max(min_w / img_h, ratio)), 1), max_w)
    rw = tw if math.ceil(img_h * ratio) > tw else int(math.ceil(img_h * ratio))
    r = resize_bilinear(crop, rw, img_h)
    r = (r / 255.0 - 0.5) / 0.5
    pad = np.zeros((img_h, tw, 3), dtype=np.float32)
    pad[:, :rw] = r
    return pad.transpose(2, 0, 1)[None].astype(np.float32)


def ctc_decode(probs: np.ndarray, charset: list[str]):
    idx = probs.argmax(-1)
    mx = probs.max(-1)
    out, sc, prev = [], [], -1
    for i, (k, p) in enumerate(zip(idx, mx)):
        if k != 0 and k != prev:
            out.append(charset[k] if k < len(charset) else "")
            sc.append(p)
        prev = k
    return "".join(out), float(np.mean(sc)) if sc else 0.0


def load_charset(path: Path, space: bool) -> list[str]:
    chars = path.read_text(encoding="utf-8").split("\n")
    if chars and chars[-1] == "":
        chars.pop()
    return ["blank"] + chars + ([" "] if space else [])

# ---------------------------------------------------------------- driver

def poly_to_box(q):
    xs, ys = q[:, 0], q[:, 1]
    return {"x": float(xs.min()), "y": float(ys.min()), "width": float(xs.max() - xs.min()), "height": float(ys.max() - ys.min())}


def run(det_path, rec_path, dict_path, ids, space, det_kw):
    import onnxruntime as ort
    so = ort.SessionOptions()
    det = ort.InferenceSession(det_path, so, providers=["CPUExecutionProvider"])
    rec = ort.InferenceSession(rec_path, so, providers=["CPUExecutionProvider"])
    charset = load_charset(Path(dict_path), space)
    res = {}
    for i in ids:
        src = i.split("_")[0]
        img = np.asarray(Image.open(PROCESSED / src / "images" / f"{i}.png").convert("RGB"))
        bgr = img[:, :, ::-1].copy()
        t0 = time.time()
        x, shape = det_preprocess(bgr)
        prob = det.run(None, {det.get_inputs()[0].name: x})[0][0, 0]
        t1 = time.time()
        boxes, dscores = det_postprocess(prob, shape, **det_kw)
        t2 = time.time()
        words = []
        for q, ds in zip(boxes, dscores):
            crop = crop_line(bgr, q)
            p = rec.run(None, {rec.get_inputs()[0].name: rec_preprocess(crop)})[0][0]
            text, conf = ctc_decode(p, charset)
            words.append({"text": text, "confidence": conf, "det_score": ds, "poly": q.tolist(), "box": poly_to_box(q)})
        res[i] = {"words": words, "secs_det_net": t1 - t0, "secs_det_post": t2 - t1, "secs_total": time.time() - t0}
        print(i, len(words), f"det {t1 - t0:.2f}s post {t2 - t1:.2f}s total {time.time() - t0:.2f}s", flush=True)
    return res


def iou(a, b):
    ax0, ay0, ax1, ay1 = a["x"], a["y"], a["x"] + a["width"], a["y"] + a["height"]
    bx0, by0, bx1, by1 = b["x"], b["y"], b["x"] + b["width"], b["y"] + b["height"]
    iw, ih = max(0, min(ax1, bx1) - max(ax0, bx0)), max(0, min(ay1, by1) - max(ay0, by0))
    inter = iw * ih
    u = a["width"] * a["height"] + b["width"] * b["height"] - inter
    return inter / u if u > 0 else 0.0


def lev(a: str, b: str) -> int:
    prev = list(range(len(b) + 1))
    for i, ca in enumerate(a, 1):
        cur = [i]
        for j, cb in enumerate(b, 1):
            cur.append(min(prev[j] + 1, cur[j - 1] + 1, prev[j - 1] + (ca != cb)))
        prev = cur
    return prev[-1]


def norm(t):
    return "".join(t.split()).lower()


def compare(a, b, ids=None, label_a="A", label_b="B"):
    """Greedy match each A line to the best-IoU B line (IoU>=0.5); report text exact/normalised/CER and recall."""
    tot = matched = exact = nexact = 0
    ed = ch = 0
    ious = []
    unmatched_b = 0
    for i in (ids or sorted(set(a) & set(b))):
        wa, wb = a[i]["words"], b[i]["words"]
        used = set()
        for w in wa:
            tot += 1
            best, bj = 0.0, -1
            for j, v in enumerate(wb):
                if j in used:
                    continue
                s = iou(w["box"], v["box"])
                if s > best:
                    best, bj = s, j
            if best >= 0.5:
                used.add(bj)
                matched += 1
                ious.append(best)
                ta, tb = w["text"], wb[bj]["text"]
                exact += ta == tb
                nexact += norm(ta) == norm(tb)
                ed += lev(norm(ta), norm(tb))
                ch += max(len(norm(ta)), 1)
        unmatched_b += len(wb) - len(used)
    print(f"{label_a} lines={tot}  matched(IoU>=.5)={matched} ({matched / max(tot, 1):.1%})  extra in {label_b}={unmatched_b}")
    print(f"  text exact={exact / max(matched, 1):.1%}  exact after space/case normalisation={nexact / max(matched, 1):.1%}  CER vs {label_a}={ed / max(ch, 1):.2%}  mean IoU={np.mean(ious) if ious else 0:.3f}")
    return {"lines": tot, "matched": matched, "exact": exact, "nexact": nexact, "cer": ed / max(ch, 1), "iou": float(np.mean(ious)) if ious else 0, "extra": unmatched_b}


def main():
    ap = argparse.ArgumentParser()
    sub = ap.add_subparsers(dest="cmd", required=True)
    r = sub.add_parser("run")
    r.add_argument("--det", required=True)
    r.add_argument("--rec", required=True)
    r.add_argument("--dict", required=True)
    r.add_argument("--ids", required=True)
    r.add_argument("--out", required=True)
    r.add_argument("--space", action="store_true")
    r.add_argument("--thresh", type=float, default=0.3)
    r.add_argument("--box-thresh", type=float, default=0.6)
    r.add_argument("--unclip", type=float, default=1.5)
    c = sub.add_parser("cmp")
    c.add_argument("--a", required=True)
    c.add_argument("--b", required=True)
    args = ap.parse_args()
    if args.cmd == "run":
        ids = args.ids.split(",")
        res = run(args.det, args.rec, args.dict, ids, args.space, dict(thresh=args.thresh, box_thresh=args.box_thresh, unclip=args.unclip))
        Path(args.out).write_text(json.dumps(res, ensure_ascii=False), encoding="utf-8")
    else:
        def load(p):
            d = json.loads(Path(p).read_text(encoding="utf-8"))
            for v in d.values():
                for w in v["words"]:
                    if "box" not in w and "poly" in w:
                        xs = [q[0] for q in w["poly"]]; ys = [q[1] for q in w["poly"]]
                        w["box"] = {"x": min(xs), "y": min(ys), "width": max(xs) - min(xs), "height": max(ys) - min(ys)}
            return d
        compare(load(args.a), load(args.b))


if __name__ == "__main__":
    sys.exit(main())
