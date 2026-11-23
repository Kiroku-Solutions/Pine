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
1. **Licence.** The fine-tuned LayoutLMv3 checkpoint retains CC BY-NC-SA 4.0 and is managed as a separate Git LFS research artifact. Pine loads it optionally from local storage. Attribution and share-alike apply when sharing the checkpoint for noncommercial use. The training-data review in item 2 is pending before public distribution.
2. **Dataset licences.** WildReceipt: no licence file found (treated as research-only). CORD/SROIE mirror terms not confirmed. A model trained on them inherits the question.
3. **Tokenizer tests use an optional fixture.** Set `PINE_LAYOUTLM_TOKENIZER` to `research/checkpoints/layoutlmv3/tokenizer.json` (absolute path) to run them with the demo checkpoint. In the recorded run, 44/44 cases matched HuggingFace.
4. **Only the Windows OpenCV/ONNX runtime was exercised.**
5. **Test-set size.** Test has 164 documents (63 for MERCHANT/DATE); one document is ~1.6 points of F1. Differences below ~2 points between systems are noise.
6. **Quality-tag thresholds are uncalibrated placeholders** (`quality_tags.py`); stratified slices are indicative.
7. **Secrets.** A Roboflow API key was pasted in the chat; it should be regenerated. It was never written to the repository.
8. **BuildHygiene tests.** `research/baseline_dotnet/Baseline.csproj` is not in `Pine.slnx` (fails `EveryProjectFileIsInTheSolution`); the other 8 failures come from `.github/workflows/dotnet-ci.yml` being fully commented out at HEAD. Both await a decision.
9. **Stray file** `J:\tmp_dry.zip` (5.9 MB) outside the repo, created by a path typo.

## Sharing the model
The [demo pair](../checkpoints/layoutlmv3/) contains the INT8 model `model.onnx` and its matching `tokenizer.json`. For a noncommercial research handoff, include both files and the [LayoutLMv3 attribution and license notice](LAYOUTLMV3-NOTICE.md), and verify the [model card checksums](model_card.md#checksums-sha-256). Experimental exports and downloaded datasets remain in git-ignored paths. Public distribution of the weights is pending review of the training-data licenses.
