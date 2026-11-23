#!/usr/bin/env python3
"""Generate every app icon from the ONE source logo, public/PineLogo.svg.

Run from the repo root (needs Pillow and rsvg-convert on PATH or in C:/msys64/mingw64/bin):
    research/.venv/Scripts/python scripts/make-icons.py
The outputs are committed (small); rerun this after changing the logo.
"""
import io, os, shutil, subprocess, sys
from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SVG = os.path.join(ROOT, "public", "PineLogo.svg")
BG = (255, 243, 224, 255)  # warm cream, #FFF3E0: the orange logo reads on it in light and dark launchers
RSVG = shutil.which("rsvg-convert") or "C:/msys64/mingw64/bin/rsvg-convert.exe"

def logo(box_w, box_h):
    """Logo rasterised to fit inside box_w x box_h, centred on a transparent box_w x box_h canvas."""
    scale = min(box_w / 119, box_h / 113)
    w = max(1, round(119 * scale)); h = max(1, round(113 * scale))
    png = subprocess.run([RSVG, "-w", str(w), "-h", str(h), SVG], check=True, capture_output=True).stdout
    im = Image.open(io.BytesIO(png)).convert("RGBA")
    out = Image.new("RGBA", (box_w, box_h), (0, 0, 0, 0))
    out.paste(im, ((box_w - w) // 2, (box_h - h) // 2), im)
    return out

def square(size, fill):
    """Logo centred on a transparent square; fill = fraction of the square the logo's box spans."""
    inner = round(size * fill)
    out = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    out.paste(logo(inner, inner), ((size - inner) // 2, (size - inner) // 2))
    return out

def on_bg(size, fill, shape="square"):
    s = size * 4  # supersample the mask for smooth edges
    mask = Image.new("L", (s, s), 0)
    d = ImageDraw.Draw(mask)
    if shape == "round": d.ellipse((0, 0, s - 1, s - 1), fill=255)
    elif shape == "rounded": d.rounded_rectangle((0, 0, s - 1, s - 1), radius=s // 5, fill=255)
    else: d.rectangle((0, 0, s, s), fill=255)
    mask = mask.resize((size, size), Image.LANCZOS)
    bg = Image.new("RGBA", (size, size), BG); bg.putalpha(mask)
    bg.alpha_composite(square(size, fill))
    return bg

def save(im, *parts):
    p = os.path.join(ROOT, *parts); os.makedirs(os.path.dirname(p), exist_ok=True); im.save(p); print(p)

def write(text, *parts):
    p = os.path.join(ROOT, *parts); os.makedirs(os.path.dirname(p), exist_ok=True)
    with open(p, "w", encoding="utf-8", newline="\n") as f: f.write(text)
    print(p)

# ---- Avalonia (in-app logo + window icon) ----
save(logo(240, 227), "src/Pine.Ui.Avalonia/Assets/PineLogo.png")
shutil.copyfile(SVG, os.path.join(ROOT, "src/Pine.Ui.Avalonia/Assets/PineLogo.svg"))
save(square(256, 0.94), "src/Pine.Ui.Avalonia/Assets/PineIcon.png")

# ---- Windows .ico (transparent, logo fills the square) ----
p = os.path.join(ROOT, "src/Pine.Ui.Windows/Branding/Pine.ico"); os.makedirs(os.path.dirname(p), exist_ok=True)
# per-size renders so small sizes are rasterised from the vector, not downscaled
frames = [square(s, 0.94) for s in (16, 24, 32, 48, 64, 128, 256)]
frames[-1].save(p, format="ICO", sizes=[(f.width, f.height) for f in frames], append_images=frames[:-1])
print(p)

# ---- macOS .icns ----
big = on_bg(1024, 0.70, "rounded")
icns = os.path.join(ROOT, "src/Pine.Ui.Macos/Branding/Pine.icns"); os.makedirs(os.path.dirname(icns), exist_ok=True)
big.save(icns, format="ICNS"); print(icns)

# ---- iOS AppIcon.appiconset (single 1024 opaque icon; Xcode derives the rest) ----
ios = "src/Pine.Ui.Ios/Resources/Assets.xcassets"
save(on_bg(1024, 0.74).convert("RGB"), ios, "AppIcon.appiconset", "Icon-1024.png")
write('{\n  "images": [\n    { "filename": "Icon-1024.png", "idiom": "universal", "platform": "ios", "size": "1024x1024" }\n  ],\n'
      '  "info": { "author": "xcode", "version": 1 }\n}\n', ios, "AppIcon.appiconset", "Contents.json")
write('{ "info": { "author": "xcode", "version": 1 } }\n', ios, "Contents.json")

# ---- macOS asset catalog is not needed: .icns above ----

# ---- Android ----
res = "src/Pine.Ui.Android/Resources"
dens = {"mdpi": 1, "hdpi": 1.5, "xhdpi": 2, "xxhdpi": 3, "xxxhdpi": 4}
for name, m in dens.items():
    d = f"{res}/mipmap-{name}"
    save(on_bg(round(48 * m), 0.74, "rounded"), d, "ic_launcher.png")        # legacy (<API 26)
    save(on_bg(round(48 * m), 0.74, "round"), d, "ic_launcher_round.png")
    fg = round(108 * m)  # adaptive layer: logo inside the central 66% (the always-visible safe zone)
    save(square(fg, 0.62), d, "ic_launcher_foreground.png")
# monochrome (themed icons): silhouette of the logo, alpha only
mono = square(432, 0.62); r, g, b, a = mono.split()
sil = Image.merge("RGBA", (Image.new("L", mono.size, 0),) * 3 + (a,))
save(sil, res, "drawable-nodpi", "ic_launcher_monochrome.png")
adaptive = ('<?xml version="1.0" encoding="utf-8"?>\n<adaptive-icon xmlns:android="http://schemas.android.com/apk/res/android">\n'
            '  <background android:drawable="@color/ic_launcher_background" />\n'
            '  <foreground android:drawable="@mipmap/ic_launcher_foreground" />\n'
            '  <monochrome android:drawable="@drawable/ic_launcher_monochrome" />\n</adaptive-icon>\n')
write(adaptive, res, "mipmap-anydpi-v26", "ic_launcher.xml")
write(adaptive, res, "mipmap-anydpi-v26", "ic_launcher_round.xml")
write('<?xml version="1.0" encoding="utf-8"?>\n<resources>\n  <color name="ic_launcher_background">#FFF3E0</color>\n</resources>\n',
      res, "values", "ic_launcher_background.xml")
# splash: Android 12+ draws the icon inside a circle of 2/3 of 288dp; keep the logo within it
save(square(576, 0.46), res, "drawable-nodpi", "splash_icon.png")
