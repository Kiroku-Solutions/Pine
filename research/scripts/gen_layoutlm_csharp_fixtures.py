"""Generates the parity fixtures the C# LayoutLM tokenizer / image preprocessor tests compare against.
Reference: HF `tokenizers` (same Rust engine as LayoutLMv3TokenizerFast) + PIL bilinear (as LayoutLMv3ImageProcessor)."""
import json, random, sys
from pathlib import Path
import numpy as np
from PIL import Image
from tokenizers import Tokenizer

root = Path(__file__).resolve().parents[2]
out = root / "tests/Pine.Tests.Unit/Ai/Fixtures"
out.mkdir(parents=True, exist_ok=True)
tok = Tokenizer.from_file(str(root / "research/models/layoutlmv3_run1/final/tokenizer.json"))
tok.no_padding(); tok.no_truncation()

words = ["TOTAL", "Total", "$12.50", "1,591,600", "60.000", "-TICKET", "Café", "Niño", "Ñandú", "S/.25.90", "€ 3,40",
         "IVA 12%", "a", "I", "don't", "it's", "http://www.example.com/a?b=1", "R.U.C.:1790012345001", "2026-11-19",
         "19/11/2026", "SUPERMERCADO", "supermercado", "Münchën", "日本語", "😀", "TOTAL😀", " nbsp", "a\tb",
         "x" * 60, "0123456789" * 6, "(555)-123-4567", "#42", "50%", "AT&T", "O'Brien's", "e-mail", "  padded  ",
         "ﬁnal", "Zażółć", "٣٤٥", "—", "“quoted”", "<s>", "A.B.C"]
single = []
for w in words:
    ws = w.strip()
    if not ws:
        continue
    enc = tok.encode([ws], is_pretokenized=True, add_special_tokens=False)
    single.append({"word": ws, "ids": enc.ids})

def doc(ws, mx=512, stride=128):
    t = Tokenizer.from_file(str(root / "research/models/layoutlmv3_run1/final/tokenizer.json"))
    t.no_padding(); t.enable_truncation(max_length=mx, stride=stride)
    enc = t.encode(ws, is_pretokenized=True)
    chunks = [enc] + list(enc.overflowing)
    return [{"ids": c.ids, "word_ids": [(-1 if w is None else w) for w in c.word_ids]} for c in chunks]

short = ["MERCADONA", "S.A.", "TOTAL", "$12.50", "19/11/2026", "IVA", "2,10"]
rnd = random.Random(7)
vocab_words = [w["word"] for w in single]
longw = [rnd.choice(vocab_words) + (str(rnd.randint(0, 999)) if rnd.random() < .5 else "") for _ in range(700)]
mid = longw[:260]
docs = [{"name": "short", "words": short, "chunks": doc(short)},
        {"name": "mid", "words": mid, "chunks": doc(mid)},
        {"name": "long", "words": longw, "chunks": doc(longw)}]
(out / "layoutlm_tokenizer_fixtures.json").write_text(json.dumps({"single": single, "docs": docs}, ensure_ascii=False), encoding="utf-8")
print("single", len(single), "docs", [(d["name"], len(d["chunks"])) for d in docs])

# ---- image fixtures: PIL bilinear 224x224, rescale 1/255, normalize .5/.5 ----
def synth(w, h, seed):
    r = np.random.RandomState(seed)
    y, x = np.mgrid[0:h, 0:w]
    img = np.stack([(x * 255 // max(w - 1, 1)), (y * 255 // max(h - 1, 1)), ((x + y) * 255 // (w + h - 2))], -1).astype(np.uint8)
    for _ in range(6):
        x0, y0 = r.randint(0, w - 4), r.randint(0, h - 4)
        img[y0:y0 + r.randint(1, 4), x0:x0 + r.randint(3, max(w // 2, 4))] = r.randint(0, 255, 3)
    return img
for name, (w, h) in {"down": (301, 421), "up": (53, 71)}.items():
    a = synth(w, h, 1)
    Image.fromarray(a).save(out / f"img_{name}_src.png")
    res = Image.fromarray(a).resize((224, 224), resample=2)
    res.save(out / f"img_{name}_expected224.png")
    pv = (np.asarray(res).astype(np.float32) / 255.0 - 0.5) / 0.5
    print(name, pv.shape, float(pv.min()), float(pv.max()))
