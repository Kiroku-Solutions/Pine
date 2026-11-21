# Risks and deviations (LayoutLMv3 KIE for Pine)

Status: 2026-11-19. Nothing here is resolved by this document; it only records the facts so they are not lost.

## Deviations from the plan
| # | Plan / paper | What we did | Why | Decision |
|---|---|---|---|---|
| D1 | INT8 model under 100 MB | `model_int8_emb.onnx` is **128.4 MB** (INT8 MatMul/Gemm + INT8 embeddings; plain INT8 is 247.0 MB, FP32 501.6 MB) | LayoutLMv3-base has ~125M parameters; vocabulary pruning was prepared (`colab/prune_vocab_export.py`) but not used | Accepted by the project owner; the model is interchangeable (same I/O), so a smaller one can replace it |
| D2 | Evaluate on English retail receipts (Roboflow set) | Trained/evaluated on CORD + SROIE (+ WildReceipt held-out) | The Roboflow dataset is generic document segmentation (~3 % receipts) | Owner chose public datasets |
| D3 | Partial labels | CORD annotates only TOTAL/TAX, SROIE only MERCHANT/DATE/TOTAL; unannotated fields are masked (-100) and scored only on documents that annotate them | No public set has all 4 fields except WildReceipt | Documented in `colab/README.md` |
| D4 | Macro-F1 of Colab evaluation | The Colab F1 (token-span level, dataset OCR) is **not** comparable to the baseline; Phase 5 re-measures value-level F1 on PaddleOCR output through Pine's C# code | Different matching rule and different OCR | See `reports/phase5_report.md` |

## Open risks
1. **Licence (DECIDED 2026-11-19: research / non-commercial use only).** LayoutLMv3 weights are CC BY-NC-SA 4.0 (non-commercial, share-alike); Pine is GPLv3. They are not obviously compatible, and fine-tuned weights are derivatives. Owner's decision: the work is for the academic document only, with no commercial use, so CC BY-NC-SA 4.0 is acceptable. Conditions that remain: (a) credit Microsoft/LayoutLMv3 and state that the weights were fine-tuned; (b) any redistribution of the weights, ONNX files or quantized variants carries CC BY-NC-SA 4.0 and says so; (c) the `.onnx` stays out of the Pine repository and releases (Pine is GPLv3, which forbids added non-commercial restrictions); (d) if the project ever becomes commercial, the model must be replaced (the extractor is interchangeable); (e) the dataset licences (risk 2) must be settled before publishing weights. Not legal advice.
2. **Dataset licences.** WildReceipt: no licence file found (treated as research-only). CORD/SROIE mirror terms not confirmed. A model trained on them inherits the question.
3. **Tokenizer tests do not run in CI.** They need `tokenizer.json`, which is gitignored. Locally 44/44 cases match HuggingFace.
4. **Only the Windows OpenCV/ONNX runtime was exercised.**
5. **Test-set size.** Test has 164 documents (63 for MERCHANT/DATE); one document is ~1.6 points of F1. Differences below ~2 points between systems are noise.
6. **Quality-tag thresholds are uncalibrated placeholders** (`quality_tags.py`); stratified slices are indicative.
7. **Secrets.** A Roboflow API key was pasted in the chat; it should be regenerated. It was never written to the repository.
8. **BuildHygiene tests.** `research/baseline_dotnet/Baseline.csproj` is not in `Pine.slnx` (fails `EveryProjectFileIsInTheSolution`); the other 8 failures come from `.github/workflows/dotnet-ci.yml` being fully commented out at HEAD. Both await a decision.
9. **Stray file** `J:\tmp_dry.zip` (5.9 MB) outside the repo, created by a path typo.

## Sharing the model
Pine keeps the experimental `.onnx` files outside git (`research/.gitignore`: `colab/onnx/`, `models/`). For a noncommercial research handoff, include the ONNX file, its matching `tokenizer.json`, and the [LayoutLMv3 attribution and license notice](LAYOUTLMV3-NOTICE.md). Verify the model and tokenizer against the [model card checksums](model_card.md#checksums-sha-256). Public distribution of the weights is pending review of the training-data licenses.
