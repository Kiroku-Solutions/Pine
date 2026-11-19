# Model card - Pine KIE, LayoutLMv3 token classification (run1)

| | |
|---|---|
| Task | Key information extraction on receipts: MERCHANT, DATE, TOTAL, TAX (BIO tags, 9 labels) |
| Base | `microsoft/layoutlmv3-base` (CC BY-NC-SA 4.0 - see `risks_and_deviations.md`) |
| Training data | CORD + SROIE (train) + WildReceipt (train), partial-label masking |
| Recommended file | `model_int8_emb.onnx` - 128.4 MB, dynamic INT8 incl. embeddings |
| Inputs | `input_ids`, `bbox`, `attention_mask` (int64 `[B,S]`, bbox `[B,S,4]` 0-1000), `pixel_values` float32 `[B,3,224,224]` (rescale 1/255, mean/std 0.5) |
| Output | `logits` `[B,S,9]`; ids: 0 O, 1/2 MERCHANT, 3/4 DATE, 5/6 TOTAL, 7/8 TAX (B-/I-) |
| Runtime | ONNX opset 17, `Microsoft.ML.OnnxRuntime` 1.29.0 (Pine) / onnxruntime 1.32.0 (Python benchmark), CPU |
| Tokenizer | `tokenizer.json` of the same checkpoint (RoBERTa byte-level BPE, prefix space) |
| Window | 512 tokens, stride 128 |
| Offline | no network access at load or inference |

## Checksums (SHA-256)
```
12b927cf21d39789154dd57bc4748191f9540d3373311c46de9470689d0e9826  model_int8_emb.onnx
f2e7c7a441826672db9931129e775512bcd59a73c9bd4cadb59b7fcd1512cbba  model_int8.onnx
890894b6a2b00c75e9044a5748cf74f3f66c1132aed4f7e3c480aec7750abce7  model_fp32.onnx
f3d92f0f721439c493fed4264ed92238a9ccf3aa5d18cc3da67b3b6b8036c3c3  tokenizer.json
8825f3bb5ce1854902be5d12c587f92bfde26c4b6fcc3573a18ea8fdfce3c909  model.safetensors (PyTorch checkpoint)
```

## Quality (Colab, dataset OCR, token-span F1; see `colab/eval_onnx_report.json`)
| Model | test Macro-F1 | WildReceipt Macro-F1 | val Macro-F1 |
|---|---|---|---|
| PyTorch = FP32 | 0.9647 | 0.8308 | 0.8929 |
| INT8 | 0.9596 | 0.8226 | 0.8832 |
| INT8 + embeddings | 0.9623 | 0.8283 | 0.8803 |

End-to-end value-level F1 on PaddleOCR output through Pine's C# extractor: `reports/phase5_report.md`.

## Licence and attribution
Fine-tuned from `microsoft/layoutlmv3-base` (Huang et al., 2022, CC BY-NC-SA 4.0). These weights and every ONNX/quantized variant are distributed under **CC BY-NC-SA 4.0**: research / non-commercial use only, attribution required, share-alike. Fine-tuned and quantized by the Pine project; not endorsed by Microsoft. Training-data licences are unverified (see `risks_and_deviations.md`).

## Intended use and limits
Extracting four fields from photographed/scanned receipts, mostly English/Latin script. Not evaluated on Spanish receipts, handwriting, or multi-receipt images. WildReceipt MERCHANT (0.77) and TOTAL (0.73) show the model is the limiting factor, not the quantization.
