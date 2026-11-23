# Phase 5 - end-to-end evaluation (PaddleOCR -> Pine C# `LayoutLmFieldExtractor`)

Split: test (164 docs: CORD 101, SROIE 63), the same documents and matching rules as the Phase 1 baseline
(`baseline_eval_paddleocr.md`). OCR = PaddleOCR 3.7.0 output (`data/ocr_paddle`). Model = `model_int8_emb.onnx`
(128.4 MB) run by Pine's real C# code, not a Python re-implementation.
Reproduce:
```
dotnet run --project baseline_dotnet -c Release -- data/ocr_paddle data/pred_layoutlm \
    --layoutlm colab/onnx/model_int8_emb.onnx models/layoutlmv3_run1/final/tokenizer.json
python scripts/eval_fields.py --pred data/pred_layoutlm --split test --out reports/phase5_eval_layoutlm.json
python scripts/eval_fields.py --pred data/pred_layoutlm-hybrid --split test --out reports/phase5_eval_layoutlm-hybrid.json
python scripts/quality_tags.py --split test && python scripts/phase5_stratified.py --pred \
    heuristics=data/pred_paddle layoutlm=data/pred_layoutlm hybrid=data/pred_layoutlm-hybrid
```

## Results (P / R / F1, value-level)
| Field (GT docs) | Heuristics | LayoutLMv3 only | Hybrid (as shipped: model overlays heuristics) |
|---|---|---|---|
| MERCHANT (63) | .444 / .444 / .444 | .778 / .778 / .778 | .778 / .778 / .778 |
| DATE (63) | .949 / .889 / .918 | 1.00 / .873 / .932 | 1.00 / .937 / **.967** |
| TOTAL (163) | .193 / .190 / .191 | .955 / .908 / **.931** | .915 / .920 / .917 |
| TAX (41) | unsupported (0) | .972 / .854 / .909 | .972 / .854 / .909 |
| **Macro-F1, 4 fields** | 0.3885 | 0.8875 | **0.8929** |

The heuristic TOTAL score is dominated by CORD (0.04): its decimal parsing is tuned for Spanish receipts and fails on
thousands-separated amounts. Part of the gain is therefore the heuristics' locale, not only the model.

## Latency (Pine C# extractor, model stage only, this machine: Ryzen 7 5700G, 16 threads, ORT CPU)
Median **204 ms**, P95 **793 ms** per document (164 docs, 0 failures; includes image decode/resize, tokenization,
inference, decoding; excludes OCR). Real input lengths are dynamic, unlike the padded 512-token benchmark
(`benchmark_onnx.json`: median 526 ms, P95 610 ms, peak RSS 511 MB, load 0.41 s). Peak RSS of the full pipeline was not measured.

## Error analysis (hybrid, 164 docs)
| Field | Wrong value | No value |
|---|---|---|
| MERCHANT | 14 | 0 |
| TOTAL | 13 | 0 |
| DATE | 0 | 4 |
| TAX | 1 | 5 |

MERCHANT errors are mostly boundary/near-miss (`HOME MASTER HARDWARE &` vs `... & ELECTRICAL`, `POPULAR BOOK` vs
`POPULAR BOOK CO. (M) SDN BHD`) or single-character OCR errors (`HARDWAKE`, `S19537-X`); strict exact-match scoring counts all as wrong.

## Stratified (Macro-F1, 4 fields; heuristics | model | hybrid)
| Slice | n | Heuristics | Model | Hybrid |
|---|---|---|---|---|
| blurry | 27 | .510 | .976 | .967 |
| sharp | 137 | .393 | .883 | .890 |
| bright | 59 | .602 | .875 | .884 |
| ok illumination | 97 | .347 | .839 | .835 |
| low_contrast | 8 (low n) | .000 | .938 | .938 |
| long_narrow | 22 | .402 | .861 | .876 |
| standard | 142 | .395 | .911 | .914 |
| CORD | 101 | .020 | .927 | .917 |
| SROIE | 63 | .597 | .873 | .883 |

**Do not read "blurry > sharp" as a finding.** The blur/illumination thresholds are uncalibrated placeholders and the
tags are confounded with the source dataset (CORD vs SROIE have different image statistics and different annotated
fields). No slice shows the model collapsing, but the data cannot say that blur or lighting do not matter; a
real-photo English retail set with human labels is still needed.

## Limits of these numbers
- The model was trained on CORD/SROIE/WildReceipt train; test is held-out, but all three are Southeast-Asian-heavy
  receipts, not Western retail receipts. Expect lower scores on Pine's real users (WildReceipt, the most Western set, scored 0.83 in Colab).
- 164 documents: differences under ~2 points are noise.
- The Colab F1 (0.96) used the dataset's OCR and span matching; this report uses PaddleOCR and value matching - not comparable.
