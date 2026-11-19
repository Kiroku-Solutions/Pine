# Roboflow dataset ingest (own dataset: ~6500 English document images, class `document`)

Status: scripts and tests are ready and verified on SYNTHETIC data only. Nothing has been run on the real dataset yet.

## 1. Export from Roboflow (COCO)
1. Open your project -> Versions -> pick the version -> **Export Dataset**.
2. Format: **COCO** (YOLOv8 also works). Choose "Download zip to computer" (no key needed) or "Show download code".
3. Keep the version's own train/valid/test split; the pipeline respects it.
4. Unzip to a folder, e.g. `J:\Kiroku\Pine\research\data\roboflow\raw` (git-ignored data dir). Expected layout:
   `train/_annotations.coco.json` + images, `valid/...`, `test/...` (YOLO: `train/images`, `train/labels`, ...).

## 2. Run
```
cd research
.venv\Scripts\python scripts\roboflow_download.py --from-dir data\roboflow\raw      # validates, prints counts
.venv\Scripts\python scripts\roboflow_prepare.py --export data\roboflow\raw         # crops + manifest + splits.json
.venv\Scripts\python scripts\sample_for_labeling.py                                 # test (150) + rounds 200/250/250
.venv\Scripts\python scripts\ocr_dump.py ...                                        # OCR of the crops (existing script)
.venv\Scripts\python scripts\prelabel_for_labelstudio.py --round data\roboflow\labeling\round1.json --ocr-dir data\ocr --out tasks_round1.json --config-out labelstudio_config.xml
```
For the test: `--round data\roboflow\labeling\test_to_verify.json` (blind, no predictions).

### Optional: download through the API
```
pip install -r requirements-roboflow.txt
# PowerShell, current session only; type the key in your own terminal, never paste it in chat or commit it:
$env:ROBOFLOW_API_KEY = Read-Host "Roboflow API key"     # (or set it via Windows "Environment variables" UI)
.venv\Scripts\python scripts\roboflow_download.py --api --workspace <ws> --project <proj> --version <n>
```
The key is read only from `ROBOFLOW_API_KEY`, never accepted as an argument, never printed; errors are scrubbed. Close the shell afterwards.
Note `Read-Host` echoes visibly; use `Read-Host -AsSecureString` plus conversion if someone can see your screen.

## 3. Behaviour details
- **Negatives**: dropped if the image has tag `Negative-Sample` (only visible if the export includes tags; the standard COCO export does not) OR has no boxes. No-boxes is the reliable criterion. Reasons are listed under `dropped` in `manifest.json`.
- **Dedup**: own 64-bit DCT perceptual hash (no network, no `imagehash`); Hamming <= 6 (`--hamming`) = same group. A group is never split across train/valid/test (priority test > valid > train if Roboflow disagreed).
- **Crops**: box + `--margin` (default 3% per side), saved to `data/roboflow/crops/<split>/`. One crop per box.
- **Frozen splits**: `data/roboflow/splits.json` is written once and reused; it is never regenerated. Delete it manually only on purpose.
- **Sampling**: strata = blur tercile (Laplacian variance) x mean-luminance tercile, reusing `quality_tags.py` (imported, unmodified). One representative per dup group. The 150-image test is drawn FIRST, then rounds 200/250/250, seed 42. `test_to_verify.json` is frozen; the script refuses to overwrite it.
- **Label Studio**: import the tasks JSON, paste `labelstudio_config.xml` as the labeling config (labels MERCHANT/DATE/TOTAL/TAX; Choices blur/iluminacion/layout, prefilled from `quality_tags`). To serve local images set `LOCAL_FILES_SERVING_ENABLED=true` and `LOCAL_FILES_DOCUMENT_ROOT` to `research/data/roboflow`, and add a Local Files storage; `--url-prefix` matches the default URL form.
- **Pre-label bias**: pre-labels for train rounds come from OCR + simple heuristics; annotators tend to accept suggestions, so train labels inherit that bias (anchoring). The test set is labeled with NO pre-label, fully by hand, to keep evaluation unbiased. Report this in the thesis.

## 4. License / privacy
Images stay local. The only network traffic is the optional Roboflow download of your own dataset. OCR (PaddleOCR) and Label Studio run locally. Check the dataset license/consent for any redistribution; do not commit `data/roboflow/` or API keys.

## 5. Blocked on the user
Real dataset folder (or API workspace/project/version + key in the env var). Until then nothing has been executed on real images.
