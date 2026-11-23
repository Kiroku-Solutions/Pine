# Baseline: PaddleOCR 3.7.0 (PP-OCRv6 medium) + HeuristicFieldExtractor

Split: test (164 docs: CORD 101, SROIE 63). Detail: `baseline_eval_paddleocr.json`.
Reproduce: `scripts/ocr_dump.py --split test --out data/ocr_paddle`, then
`dotnet run --project baseline_dotnet -c Release -- data/ocr_paddle data/pred_paddle`, then
`scripts/eval_fields.py --pred data/pred_paddle --split test`.

| Field | GT docs | Precision | Recall | F1 |
|---|---|---|---|---|
| MERCHANT (SROIE only) | 63 | 0.444 | 0.444 | 0.444 |
| DATE (SROIE only) | 63 | 0.949 | 0.889 | 0.918 |
| TOTAL | 163 | 0.193 | 0.190 | 0.191 |
| TAX (CORD only) | 41 | 0 | 0 | 0 (unsupported by the baseline) |

Macro-F1, 4 fields (TAX counts as 0): **0.3885**. Macro-F1, 3 supported fields: **0.5179**.

Per source, TOTAL F1: SROIE 0.429, CORD 0.040.

## Versus the earlier "oracle OCR" run (annotated lines used as OCR)
Macro-F1 4 fields 0.4496 -> 0.3885; 3 supported 0.5995 -> 0.5179. MERCHANT 0.68 -> 0.44, DATE 0.94 -> 0.92, TOTAL 0.17 -> 0.19.

## Caveats
- The CORD TOTAL score (0.04) is dominated by the heuristic's decimal-amount parsing, tuned for Spanish receipts, failing on Indonesian
  thousands-separated amounts (e.g. `1,591,600`). It says little about OCR quality or about English receipts.
- CORD and SROIE are not English retail receipts, so these numbers are NOT the final baseline. The number to beat for the paper
  must be recomputed on the human-verified English test set from the Roboflow data.
- MERCHANT and DATE exist only in SROIE, TAX only in CORD; no single document has all four fields.
