# LayoutLMv3 NER on Colab

Trains `microsoft/layoutlmv3-base` for token classification of `MERCHANT`, `DATE`, `TOTAL`, and `TAX`.

## Data Flow

| Split (file in zip) | Contents |
|---|---|
| `train.json` | `splits.json` train (CORD + SROIE) + official WildReceipt train WITHOUT the carve-out |
| `val.json` | `splits.json` val + deterministic carve-out (~10%, seed 42, grouped) from official WildReceipt train |
| `test.json` | Main `splits.json` test (CORD + SROIE) |
| `test_wildreceipt.json` | Official WildReceipt test (held-out; never enters train or val; verified by `assert_disjoint`) |
| `subset_4fields.json` | Per-split IDs of docs with all 4 fields (only WildReceipt has all 4) for separate reporting |
| `manifest.json` | Per-split/source/field counts, split IDs, seed, image parameters |

Carve-out: `colab_splits.py`. Unit = group of docs sharing `image_sha256` (WildReceipt is already deduplicated by sha256) or normalized text (near-identical receipts); a group is never split across train and val. IDs are also saved in `research/splits_colab.json` for traceability (`splits.json` and `splits_extra.json` are not modified).

Partial labels by source (`layout_utils.SOURCE_FIELDS`): `cord` = TOTAL, TAX; `sroie` = MERCHANT, DATE, TOTAL; `wildreceipt` = all 4. B-/I- tags for fields not annotated by a source are masked to `-100` (per source, not per document).
Evaluation (`field_report.json` + log): entity-level F1 (exact match) ONLY on docs that annotate that field, for `val`, `test`, and `test_wildreceipt`, plus the 4-field subset of each split. (`test` has no 4-field docs, so that subset is empty.) Seqeval's global F1 during training mixes sources; use `field_report.json`.

Known label noise: with a single BIO head, an `O` word belonging to an unannotated field enters as `O` (e.g., merchant in CORD, TAX in SROIE). In addition, the WildReceipt converter keeps only the FIRST contiguous span per field; additional spans of the same field stay `O`.

## Steps

1. Locally, from `research/`: `.venv/Scripts/python colab/prepare_colab_bundle.py --out colab/dist/pine_bundle.zip`
   (images max 1024 px, JPEG q85; `--limit N` runs a dry-run with N docs per split; the zip goes to `colab/dist/`, gitignored).
2. Upload `pine_bundle.zip` to Google Drive under `MyDrive/pine/`.
3. Upload `train_layoutlmv3.ipynb` to Colab (or open it from Drive).
4. Runtime > Change runtime type > **T4 GPU**.
5. Run cells in order (installs dependencies, mounts Drive, unzips, 8-doc smoke test, full training with `--eval-test`, evaluating `test` and `test_wildreceipt`).
6. Download `MyDrive/pine/run1/final/` (model, processor, `id2label.json`, `val_metrics.json`, `test_metrics.json`, `test_wildreceipt_metrics.json`, `field_report.json`).
   If the session disconnects, re-run the last cell: `--resume` continues from the latest Drive checkpoint.

## Estimated Runtime

~1,700 train chunks, ~1–2 min/epoch on T4 with fp16 -> 15 epochs ~20–40 min, plus ~5 min preprocessing.
Early stopping (patience 4 on validation seqeval F1) may finish sooner.

## Where Each Step Runs

- Local: `prepare_colab_bundle.py`, tests (`research/tests/test_colab_prep.py`, `test_colab_splits.py`), and `py_compile`.
- Colab: `train_layoutlmv3.py` (downloads base model, trains, evaluates).
- ONNX export: `export_onnx_direct.py` / `export_onnx_int8.py`.

## Design Details

- Bounding boxes normalized to `0–1000`; `apply_ocr=False`; label on first subtoken only (`--label-all-tokens` optional).
- Partial labels: see Data Flow (`--partial-mode source|available|none`).
- Long documents: 512-token windows with `--stride 128`; metrics are per chunk.
- Default hyperparameters: batch 4 × accum 4, lr 5e-5, 15 epochs, fp16, seed 42.

## License (Important)

LayoutLMv3 is published under **CC BY-NC-SA 4.0** (non-commercial, share-alike). Pine is GPLv3: derived weights inherit the non-commercial restriction, and CC BY-NC-SA is incompatible with GPLv3, so they should not be bundled inside the app binary. Check dataset licenses (CORD, SROIE) separately. A permissive alternative to evaluate is **LiLT** (`SCUT-DLVCLab/lilt-roberta-en-base`, MIT).
