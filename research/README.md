# Receipt Field Extraction Research

This directory contains data preparation, training, ONNX export, and evaluation for Pine's four receipt fields: merchant, date, total, and tax. The application shares its C# extractor with the evaluator.

## Reproducing the Deliverable 2 Table

From the repository root, run `git lfs install` and `git lfs pull` to fetch the models.
Use the [INT8 model and tokenizer](checkpoints/layoutlmv3/), with checksums in the [model card](docs/model_card.md#checksums-sha-256).
The comparison uses the fixed `splits.json` split with 164 receipts from CORD and SROIE. All three variants receive the exact same PaddleOCR 3.7 output.

In PowerShell, set up the environment and download the datasets following the commands in the next section, through `convert_sroie.py`. Then, from `research/`, run:

```powershell
python scripts/ocr_dump.py --split test --out data/deliverable2/ocr
$modelDir = 'checkpoints/layoutlmv3'
dotnet run --project baseline_dotnet -c Release -- data/deliverable2/ocr data/deliverable2/rules
dotnet run --project baseline_dotnet -c Release -- data/deliverable2/ocr data/deliverable2/layoutlm --layoutlm "$modelDir/model.onnx" "$modelDir/tokenizer.json"
python scripts/eval_fields.py --split test --pred data/deliverable2/rules --out data/deliverable2/rules-scores.json
python scripts/eval_fields.py --split test --pred data/deliverable2/layoutlm --out data/deliverable2/layoutlm-scores.json
python scripts/eval_fields.py --split test --pred data/deliverable2/layoutlm-hybrid --out data/deliverable2/hybrid-scores.json
```

The second .NET command generates both the model predictions and the hybrid predictions. The `macro_f1_4_fields (unsupported fields count as 0)` key averages F1 across all four fields. Rounded to three decimal places, the Deliverable 2 values are **0.702** for the corrected rules baseline, **0.894** for LayoutLMv3, and **0.904** for the hybrid extractor. The evaluator writes one JSON report per variant.

## Preparing Data and Running the Baseline

```powershell
cd research
py -3.12 -m venv .venv ; .\.venv\Scripts\Activate.ps1
pip install -r requirements.txt            # exact versions (Python 3.12, CPU); paddleocr/paddlepaddle only needed for ocr_dump.py

python scripts/download_datasets.py        # idempotent -> data/raw/ (gitignored)
python scripts/convert_cord.py             # -> data/processed/cord/{images,labels}
python scripts/convert_sroie.py            # -> data/processed/sroie/{images,labels}
python scripts/make_splits.py              # freezes splits.json (not regenerated if it already exists)
python scripts/audit_labels.py             # -> reports/audit.json + audit.md

python scripts/ocr_dump.py --split test    # PaddleOCR -> data/ocr/<id>.json
dotnet run --project baseline_dotnet -- data/ocr data/predictions   # HeuristicFieldExtractor
python scripts/eval_fields.py --pred data/predictions --split test  # P/R/F1 + Macro-F1
python scripts/quality_tags.py --split test                         # image quality breakdown

python -m pytest tests -q                  # synthetic unit tests, offline
```

`splits.json` is versioned (frozen manifest); `data/`, `.venv/`, and `bin/obj` are listed in `research/.gitignore`.

## Label Format (FUNSD-BIO, one JSON per document)

`data/processed/<source>/labels/<id>.json`:

```json
{
  "id": "cord_train_00001", "source": "cord",
  "image": "images/cord_train_00001.png", "image_sha256": "...",
  "width": 864, "height": 1296,
  "words": [ {"text": "1,591,600", "box": [x0, y0, x1, y1], "label": "B-TOTAL"} ],
  "ground_truth": {"MERCHANT": null, "DATE": null, "TOTAL": "1,591,600", "TAX": "144,691"},
  "fields_available": ["TOTAL", "TAX"]
}
```

- `box` is in image pixels `(x0, y0, x1, y1)`; `image` is relative to the source folder.
- BIO `label`: `O`, `B-/I-MERCHANT`, `B-/I-DATE`, `B-/I-TOTAL`, `B-/I-TAX`.
- `ground_truth`: final field value (`null` if absent in the dataset); used for field-level F1. Taken from the dataset's structured ground truth, not reconstructed from word tokens.

OCR output (`ocr_dump.py`, matching `Pine.Core.Ai.OcrWord`):
`{"id", "words": [{"text", "confidence", "box": {"x","y","width","height"}}]}`.
Baseline predictions: `{"id", "merchant", "total", "date", "tax": null, "tax_supported": false}`.

## Source-to-Field Mapping

| Dataset | Source field | Target field |
|---|---|---|
| CORD-v2 | `total.total_price` | TOTAL |
| CORD-v2 | `sub_total.tax_price` (and `total.tax_price` if present) | TAX |
| CORD-v2 | (not present) | MERCHANT, DATE = `null` |
| SROIE | `company` | MERCHANT |
| SROIE | `date` | DATE |
| SROIE | `total` | TOTAL |
| SROIE | `address` | discarded (`O`) |

Note: in CORD, tax lives in `sub_total.tax_price`; `total.tax_price` is accepted as a fallback.
SROIE is downloaded from the Hugging Face mirror `arvindrajan92/sroie_document_understanding` (see `convert_sroie.py` for the expected schema).

## Limitations

- CORD: Indonesian receipts (rupiah, `,` thousands separator), no reliable merchant or date -> only TOTAL and TAX have ground truth.
- SROIE: Malaysian receipts, no TAX. Ambiguous numeric dates: normalization assumes day-first.
- Neither dataset covers all 4 fields at once: Macro-F1 is computed over fields with ground truth in the evaluated subset (excluded fields are listed in `excluded_no_gt`, never counted as 0 or 1).
- Amount normalization: heuristic (single separator + 3 digits = thousands). `0.001` would be misread; real amounts have 2 decimal places.
- Hugging Face SROIE mirrors are unofficial; verify alignment with the ICDAR 2019 competition before publishing results.

## Baseline (C#)

`baseline_dotnet/` is a standalone console app (not in `Pine.slnx`) that references `src/Pine.Infrastructure` via `ProjectReference` and runs `HeuristicFieldExtractor`. Root `Directory.Build.props` and Central Package Management apply directly; building it generates `baseline_dotnet/packages.lock.json`.
The heuristic baseline is tuned for Spanish receipts (`FECHA`, `IVA`, etc.), so it is expected to perform poorly on English documents—it is a baseline to beat, not a target.

**Explicit `TAX = N/A`.** `ReceiptExtractionResult` does not expose Tax (taxes are added as adjustment lines in `LineItems`, detected by Spanish keywords). Deriving it from there is unreliable on English receipts, so the baseline emits `tax: null`.
`eval_fields.py` reports two Macro-F1 scores: `macro_f1_4_fields` (tax counts as 0 when GT exists) and `macro_f1_supported_fields` (MERCHANT, DATE, TOTAL).

Metrics: per-field Precision/Recall/F1; TP = matching prediction; FP = wrong prediction or missing GT; FN = missed GT. Matching: merchant normalized for case/punctuation/whitespace; total and tax decimal within 0.01 tolerance; ISO date.

## Licenses (Open Considerations)

- CORD-v2: CC BY 4.0 (attribution to NAVER CLOVA).
- SROIE: ICDAR 2019 competition data; the HF mirror states MIT, but upstream licensing is unclear -> verify before redistribution.
- **LayoutLMv3:** original weights and Pine's fine-tuned adaptation are under **CC BY-NC-SA 4.0**. The [research artifact](checkpoints/layoutlmv3/) is managed via Git LFS and loaded as a local file outside the APK. The [attribution and license notice](docs/LAYOUTLMV3-NOTICE.md) documents credit to Microsoft, the authors, and Pine's modifications. See [distribution terms](docs/risks_and_deviations.md#sharing-the-model).

## Oracle OCR Verification (Not Real OCR)

To test the C# -> eval pipeline without PaddleOCR, `data/ocr_oracle/` was generated from annotation lines (confidence 1.0); results are in `reports/baseline_eval_oracle_ocr.json`. This is an optimistic upper bound, not a real pipeline measurement.

## Real OCR with PaddleOCR

Verified on Python 3.12, CPU, Windows, `paddlepaddle==3.3.1` + `paddleocr==3.7.0` (+ `paddlex==3.7.2`), pinned in `requirements.txt`.

```powershell
pip install -r requirements.txt
python scripts/ocr_dump.py --split test --out data/ocr_paddle
```

- **Required workaround: `enable_mkldnn=False`.** On Paddle 3.3.1 CPU/Windows, the oneDNN/PIR backend fails with `NotImplementedError: ConvertPirAttribute2RuntimeAttribute`. `ocr_dump.py` already passes this when constructing `PaddleOCR`.
- PP-OCR models are downloaded on first run and cached in `~/.paddlex` (offline afterwards).
- Low RAM: run in batches of 40 images (`--limit 40`; idempotent, existing JSON files are skipped).

## Real Baseline (PaddleOCR + HeuristicFieldExtractor)

Source: `reports/baseline_eval_paddleocr.md` / `.json`. Test split: 164 docs (CORD 101, SROIE 63).

| Field | F1 |
|---|---|
| MERCHANT (SROIE only) | 0.444 |
| DATE (SROIE only) | 0.918 |
| TOTAL | 0.191 |
| TAX (CORD only, unsupported by baseline) | 0 |

Macro-F1 4 fields (TAX counted as 0): **0.3885**. Macro-F1 3 supported fields: **0.5179**.

**Note:** CORD (Indonesian) and SROIE (Malaysian) are not typical English receipts, and the heuristic extractor is tuned for Spanish; the definitive baseline is evaluated on the manually verified Roboflow test set.

## Roboflow Pipeline

Details in [`docs/roboflow_ingest.md`](docs/roboflow_ingest.md).

- Downloaded in **COCO** format (the YOLOv8 export from the segmentation project was empty).
- 12,004 images -> 11,999 after dropping 5 without boxes -> 7,207 unique groups after deduplication (Roboflow augmentations create near-duplicates; pHash, Hamming distance 6).
- Seed 42 sampling: frozen test set of 150 images (`data/roboflow/labeling/test_to_verify.json`, never overwritten) and labeling rounds of 200 / 250 / 250.

## Remaining Manual Steps

- Manually verify the 150 images of the frozen test set in Label Studio.
- Review upstream training dataset terms before public model distribution (see Licenses).
