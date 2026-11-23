# Phase 3 - ONNX evaluation and vocabulary pruning (Colab)

Status: NOT executed in Colab. Only the pure logic (`vocab_prune_utils.py`) was tested locally
(`research/tests/test_onnx_pruning.py`); `eval_onnx.py` and `prune_vocab_export.py` were only py_compiled.

Paths below assume: Drive `MyDrive/pine/` with `pine_bundle.zip`, `run1/final` (checkpoint) and `onnx/`
(`model_fp32.onnx`, `model_int8.onnx`, `model_int8_emb.onnx`).

## 1. Setup (one cell each)
```python
from google.colab import drive
drive.mount('/content/drive')
```
```bash
pip install -q onnx onnxruntime seqeval
mkdir -p /content/scripts /content/work/bundle
unzip -q -n /content/drive/MyDrive/pine/pine_bundle.zip -d /content/work/bundle
```
Upload (Files panel or `files.upload()`) into `/content/scripts/`:
`layout_utils.py train_layoutlmv3.py export_onnx_direct.py eval_onnx.py prune_vocab_export.py vocab_prune_utils.py`

## 2. Evaluate the 3 existing models (+ PyTorch reference)
```bash
cd /content/scripts
python eval_onnx.py --data-dir /content/work/bundle \
  --checkpoint /content/drive/MyDrive/pine/run1/final \
  --torch-checkpoint /content/drive/MyDrive/pine/run1/final \
  --onnx /content/drive/MyDrive/pine/onnx/model_fp32.onnx \
         /content/drive/MyDrive/pine/onnx/model_int8.onnx \
         /content/drive/MyDrive/pine/onnx/model_int8_emb.onnx \
  --out /content/drive/MyDrive/pine/onnx/eval_onnx_report.json
```
Smoke test first with `--limit 20`. Per-model id map: `--onnx pruned.onnx=id_map.json` (or global `--id-map`).
Output: per-field F1 (docs annotating the field only), Macro-F1 (mean of the 4 fields), 4-field subset, and the
difference in F1 points vs the reference (torch if given, else the first ONNX).

## 3. Prune vocabulary, export, quantize, re-evaluate
```bash
python prune_vocab_export.py --data-dir /content/work/bundle \
  --checkpoint /content/drive/MyDrive/pine/run1/final \
  --out /content/drive/MyDrive/pine/onnx_pruned \
  --keep 8192 12000 16000 \
  --baseline-onnx /content/drive/MyDrive/pine/onnx/model_int8_emb.onnx
```
Per `keepN` it builds `int8_emb_perchannel` and `int8_emb_pertensor` (per_channel falls back to False if ORT
rejects Gather per-channel; this is printed and stored). At the end it prints a size vs Macro-F1 table and, if
nothing is under 100 MB, the suggested next lever (not implemented).
Add `--eval-fp32` to also score the pruned fp32 model, `--limit 20` for a smoke test.

## 4. Download
- `onnx_pruned/keepN/model_int8_emb_perchannel.onnx`, `model_int8_emb_pertensor.onnx`
- `onnx_pruned/keepN/id_map.json` and `id_map_meta.json` (REQUIRED at inference: tokenizer ids are the original
  ones; remap old->new, unknown -> `unk_new_id`)
- `onnx_pruned/prune_report.json`, `onnx/eval_onnx_report.json`
