# 65 — ONNX OCR and Managed Vision: Receipt Scanning Off Windows

| Field            | Value                                                                                |
|------------------|--------------------------------------------------------------------------------------|
| Document ID      | `PINE-DOC-65-ONNX-OCR-DESIGN`                                                        |
| Status           | Partially implemented — Phase A (research, models, reference) and Phase B1 (engine, post-processing, tests, C# parity run) are DONE and measured on Windows; the engine is selectable with `Pine:Ai:OcrBackend` but not the default. B2 (managed detector), B3 (Windows switch), C (Android) and D are unbuilt. |
| Reconciled       | 2026-11-19 · Written at the end of Phase A. Every number marked **measured** was produced by `research/scripts/onnx_ocr_reference.py` on this machine (Windows x64, onnxruntime 1.32 Python). Everything about Android latency, memory and the APK is an **estimate** and is listed in §10 as unverified. |
| Owner            | Engineering Review Board                                                             |
| Created          | 2026-11-19                                                                           |
| Last Updated     | 2026-11-19 (B1 implemented)                                                          |

## 1. Goal and decision

Receipt scanning (file upload first, live camera later) must work on Android and on every head
without native OpenCV or Paddle Inference. Today `PineHeadCapabilities.HasReceiptVision` is true on
Windows only (`src/Pine.Ui.Avalonia/Composition/PineHeadCapabilities.cs`), and `60-Production-Readiness-Audit`
§12.64 recorded why: `OpenCvSharp4.runtime.*` and `Sdcb.PaddleInference.runtime.*` publish no Android
or iOS build.

**Decision proposed:** a second, fully managed vision stack built from parts that already ship on
Android:

* **ONNX Runtime** (`Microsoft.ML.OnnxRuntime` 1.29.0, already pinned; `libonnxruntime.so` is already in the APK) runs
  PaddleOCR detection (DB) and recognition (CTC) as ONNX models.
* **SkiaSharp** (already in the APK through `Avalonia.Skia`; resolved 3.119.3-preview.1.1) decodes, resizes, warps and encodes images.
* All other pre/post-processing is plain C# (connected components, convex hull, minimum-area rectangle, CTC decode).

Phase A shows this is feasible with **no loss of recognition quality against the engine Pine ships today**
(§5) and it is faster than Paddle Inference on the same CPU.

## 2. Current state (what the pipeline uses)

There is **no OCR interface**. The seam is a concrete class and a concrete composition:

| Piece | Where | Notes |
|---|---|---|
| `IAIProvider` (Pine.Core) | `DefaultLocalAiProvider.cs`, `PortableAiProvider.cs` | `PortableAiProvider` throws `AiCapabilityUnavailableException` for detect/crop/OCR; registered by `TryAddSingleton` at `PineInfrastructureServiceCollectionExtensions.cs:238`. |
| `DefaultLocalAiProvider` | `src/Pine.Infrastructure/Ai/DefaultLocalAiProvider.cs` (144 lines) | **Hand-constructs** `new ReceiptDetector()` and `new PaddleOcrEngine(logger)` in its constructor; flow in `ExtractReceiptDataAsync`: `ReceiptDetector.AnalyzeAndCropReceiptAsync` → binarised crop stream → `PaddleOcrEngine.ExtractTextAsync` → `HeuristicFieldExtractor.ExtractFields` → `VisualExtractionGuard.OverlayAsync` (LayoutLM). |
| `PaddleOcrEngine` | `Ai/Vision/PaddleOcrEngine.cs` (268 lines) | `Stream` in, `List<OcrWord>` out (`Text`, `Confidence`, `BoundingBox` axis-aligned pixel box). Uses `PaddleOcrAll(LocalFullModels.LatinV5, PaddleDevice.Mkldnn())` (L60), `AllowRotateDetection=false`, `Enable180Classification=false`; decodes with `Cv2.ImDecode` (L108) and runs `engine.Run(mat)` (L109) on a thread-pool thread. Error sentinels `ERROR_MODELS_NOT_FOUND` / `OCR_ERROR_CRASH` are returned as words. |
| `IReceiptDetector` / `ReceiptDetector` | `Ai/Vision/ReceiptDetector.cs` (481 lines) | YOLO-seg ONNX (`Assets/Ai/yolo-receipt-nano.onnx`) + **OpenCV**. See table below. |
| `IImageProcessor` / `OpenCvImageProcessor` | `Pine.Core/Ai/IImageProcessor.cs`, `Ai/Vision/OpenCvImageProcessor.cs` (109 lines) | Perspective crop + adaptive-threshold enhance for the manual crop screen (`CropReceiptViewModel.cs:35,80`). Registered only inside the vision branch (`...Extensions.cs:219`). |
| `LayoutLmImagePreprocessor` | `Ai/Extraction/LayoutLm/LayoutLmImagePreprocessor.cs:36-55` | Uses `Cv2.ImDecode` only to get RGB pixels; the resize is already hand-written managed code (`ResizeBilinear`, L76). |
| `OpenCvCameraSource` / `ICameraSource` | `Ai/Vision/OpenCvCameraSource.cs`, `Pine.Core/Ai/ICameraSource.cs` | Live camera; desktop webcam via OpenCV. Phase D. |

OpenCvSharp types do **not** leak above `Pine.Infrastructure`: `ArchitectureTests.cs:517-555` already forbids
`Pine.Ui.Avalonia` from depending on OpenCvSharp. The leak that matters is *inside* Infrastructure.

### 2.1 OpenCV operations actually used (the replacement scope)

`ReceiptDetector.cs` (the larger job — the detector is OpenCV-based too, so OCR alone is **not enough** to light up Android):

| Operation | Lines | Managed replacement |
|---|---|---|
| `Cv2.ImDecode` (BGR) | 104, 226 | `SKBitmap.Decode` (+ EXIF orientation via `SKCodec.EncodedOrigin`; **order of channels and orientation must match what the OCR boxes assume**, see LayoutLM remark at `LayoutLmImagePreprocessor.cs:18`). |
| `Resize` to letterbox, `CopyMakeBorder` (114,114,114), `CvtColor BGR2RGB` | 114-126, 236-248 | `SKBitmap.Resize` / draw onto 640×640 canvas; fill the tensor from `SKBitmap.GetPixelSpan()` (the unsafe loop at 134/256 already works on a byte pointer). |
| 160×160 prototype-mask dot product (`new Mat(160,160,CV_32FC1)`), `Resize` mask to 640, `Threshold` 0.5, crop to letterbox rect, `Resize` to image size | 332-365 | float[] + managed bilinear + comparison (trivial). |
| `FindContours(External, ApproxSimple)`, `ContourArea`, `ArcLength`, `ApproxPolyDP` | 368-382 | Largest 8-connected component of the mask + convex hull + Douglas-Peucker (≈60 lines); the component/hull code exists in `onnx_ocr_reference.py`. |
| Corner ordering TL/TR/BR/BL | ~395-432 | already managed arithmetic. |
| `GetPerspectiveTransform` + `WarpPerspective` | 437-439 | homography solve (8×8) + inverse-mapped bilinear sampling (reference implementation `warp_rect` in the Python script; on device: `SKCanvas` + `SKMatrix` perspective or a hand loop). |
| 3×3 sharpen `Filter2D` | 444-451 | 3×3 convolution loop. |
| Gray, `GaussianBlur(5×5)`, `AdaptiveThreshold(Gaussian, 11, 2)`, `ImEncode(.jpg/.png)` | 454-470 | Gray + separable Gaussian + local-mean threshold via an integral/box approximation (**differs numerically from OpenCV; needs the fixture re-evaluation required by `13-AI-Architecture` §7.1**); `SKImage.Encode`. |

`OpenCvImageProcessor.cs`: `GetPerspectiveTransform`/`WarpPerspective` (L41-L57) and `CvtColor`/`GaussianBlur`/`AdaptiveThreshold`/`ImEncode` (L73-L93): same primitives as above. `LayoutLmImagePreprocessor`: decode only.

**Consequence:** the minimum viable Android scan is *not* "swap the OCR engine". `DefaultLocalAiProvider` creates a `ReceiptDetector` that cannot run without OpenCvSharp. Either the detector is ported to Skia (Phase B2), or the Android head runs OCR on the full image without YOLO crop/binarise (acceptable as a first milestone — see §8).

## 3. Models (Phase A result)

Pine's engine today is `Sdcb.PaddleOCR.Models.LocalV5` `LatinV5`, i.e. **PP-OCRv5 mobile detection** (`mobile_zh_det`, model name `PP-OCRv5_mobile_det`) and **`latin_PP-OCRv5_mobile_rec`**
(836-character dictionary; 838 CTC classes). The weights are embedded resources inside the 116 MB `Sdcb.PaddleOCR.Models.LocalV5.dll`, in Paddle's new PIR format (`inference.json` + `inference.pdiparams`), which is what Phase A extracted. Language scope: Latin script (Spanish/English product languages) — no CJK.

Artifacts in `C:\Users\Camilo\AppData\Local\Temp\claude\17c62dfd23e0f368\0a33c888-5ad4-4e28-822d-80fcba6285f0\scratchpad\ocr_onnx\` (outside the repo, per the brief):

| File | Size | Origin |
|---|---:|---|
| `det.onnx` | 4,819,576 B | RapidOCR v3.10.0 `ch_PP-OCRv5_det_mobile.onnx` (sha256 `4d97c44a…78ae`). **Weights bit-identical to Pine's**: 178 of 178 shared parameters equal to the extracted Sdcb `mobile_zh_det`. |
| `rec.onnx` | 8,066,169 B | **Built locally** by `research/scripts/paddle_rec_to_onnx_transplant.py` (see below). Pine's exact weights, 838 classes. |
| `dict_latin_v5.txt` | 2,616 B (836 lines) | From the model's `inference.yml` (`PostProcess.character_dict`). Runtime charset is `["blank"] + dict + [" "]`. |
| **Total** | **≈ 12.9 MB** | Target was < 25 MB. No cls model (see below). |

**Why a transplant and not paddle2onnx.** `paddle2onnx` 2.0.2rc3 and 2.1.0 install (2 MB wheels) but **fail to import against paddlepaddle 3.3.1 on Windows**: the extension imports ~110 PIR symbols that `libpaddle.pyd` does not export (checked with a PE import/export diff). Downgrading paddle is a >100 MB download and was not attempted. The only published ONNX for latin v5 (RapidOCR / monkt) has **different weights and a 502-character dictionary**, and measured worse (§5). The graph is the same architecture with the same parameter names, so the script keeps that donor graph and pours Pine's Paddle weights in (234 tensors: conv/BN/linear by name, conv biases through `Add→Conv` mapping, layer-norm scale/bias through PIR program order; 504→838 in output shapes). It is raw protobuf editing, numpy only, and reproducible (re-running gave a byte-identical `rec.onnx`).

**Validation of the transplant (measured, 603 lines, 20 images):** the transplanted ONNX recognises the same text as Paddle's own inference on the same crops in **98.3 %** of lines (99.3 % after whitespace/case normalisation), CER **0.07 %**. The residue is float/graph noise.

**ONNX compatibility with onnxruntime 1.29.0:** opset 14 for both (IR 10). Operators used — det: Conv, ConvTranspose, HardSwish, HardSigmoid, Relu, Resize, BatchNormalization, GlobalAveragePool, Concat, Sigmoid, Add, Mul; rec: additionally MatMul, Softmax, ReduceMean, Transpose, Reshape, Slice, Squeeze/Unsqueeze, Pow, Sqrt, Div, AveragePool. All in the CPU EP of the full ORT package. (Not tested against 1.29.0 itself: Phase A ran Python ORT 1.32; Phase B must run the C# tests on 1.29.0.) **Dynamic shapes verified** (ORT 1.32): det accepts any H, W (tested 64×64, 960×736, 320×1280; use multiples of 32); rec input is `[N,3,48,W]` with dynamic N and W (tested W=160/320/2000, N=4). Rec height is fixed at 48.

**Angle classifier:** not used. Pine runs `Enable180Classification = false` and `AllowRotateDetection = false`. A small `PP-LCNet_x0_25_textline_ori_cls_mobile.onnx` exists in RapidOCR if needed later; not fetched.

**Models still not quantised.** Float32 only. INT8/FP16 would shrink and might speed up on ARM but is unmeasured.

## 4. Reference algorithm (`research/scripts/onnx_ocr_reference.py`)

numpy + pillow + onnxruntime only; written to port line by line.

* **Det pre:** long side ≤ 960 (`limit_type=max`), round each side to a multiple of 32 (min 32), bilinear resize (half-pixel, no antialias = `cv2.INTER_LINEAR`), **BGR** order, `(x/255 − mean)/std` with mean `[0.485,0.456,0.406]`, std `[0.229,0.224,0.225]` applied to the BGR channels as they are (PaddleOCR quirk — do not swap to RGB).
* **Det post:** threshold 0.3 → 8-connected components → convex hull → exact minimum-area rectangle by rotating calipers → drop if shorter side < 3 → score = mean probability inside the rectangle ≥ `box_thresh` → unclip → drop if shorter side < 5 → scale to image coordinates, clamp, round → sort top-to-bottom/left-to-right with PaddleOCR's 10 px row tolerance.
* **Managed-friendly replacements, with their cost:** `findContours` → component pixels (holes ignored: only tiny boxes, filtered); `minAreaRect` → hull + calipers (exact); `pyclipper` unclip → **closed form for a rectangle**: grow each side by `d = area·ratio/perimeter` (exact for round joins). No accuracy was traded for these; the residual det difference (§5) comes from interpolation and rectangle-score rasterisation.
* **Rec:** crop the quadrilateral with a homography warp (bilinear), rotate 90° if h/w ≥ 1.5, height 48, width `ceil(48·w/h)` (padded to **min 320**, max 3200), `(x/255 − 0.5)/0.5`, BGR; one line per run (batching by similar width is an optimisation). CTC greedy decode; line confidence = mean of the kept time steps' maximum probabilities.
* **Parameters match Sdcb's defaults** (read from the IL of `PaddleOcrDetector`'s constructor): max side 960, binarisation 0.3, **box score 0.7**, unclip 1.5. (PaddleOCR 3.x's own pipeline defaults differ: box 0.6, `limit_type=min/64`; do not use them when comparing with Pine.)

## 5. Parity evidence (measured)

20 images from the `research/splits.json` test split (12 CORD + 8 SROIE), 559 reference lines. "Pine today" = the same Sdcb weights run by Paddle Inference through PaddleOCR 3.7 Python with Sdcb's detection parameters (the C# engine itself was not run).

| Comparison | Result |
|---|---|
| Lines matched to Pine today at IoU ≥ 0.5 | **99.6 %** (557/559); 10 extra lines in ONNX |
| Text identical on matched lines | **93.2 %** (94.3 % ignoring case/spaces) |
| Character error rate vs Pine today | **0.91 %** |
| Mean box IoU | **0.948** |
| Ground-truth word recall (words ≥ 3 chars found in the page text) | Pine today 91.8 %, **ONNX 92.4 %** (PP-OCRv6 medium reference in `data/ocr_paddle`: 94.6 %) |
| Managed crop + rec preprocessing alone, Paddle weights on both sides | 95.5 % exact, CER 0.49 % (so the preprocessing is faithful) |
| ONNX with the *published* RapidOCR latin rec instead of Pine's weights | recall 88.2 %, 82 % exact text: **rejected**, different model |

Systematic differences: the mean IoU of 0.948 is the bilinear-resize/rasterisation and 1-px box rounding; lines split or merged differently on a few SROIE receipts (10 extra boxes). Receipts with tiny text (SROIE) are the weak case for both engines. **Time on this PC (CPU, Python+numpy post-processing, onnxruntime):** 0.4-1.3 s per CORD image, 1.9-2.8 s per SROIE page (≥ 2000 px tall), versus 5-26 s for the Paddle Inference run used as baseline here. One outlier (20 s) came from the numpy warp on an unusually large crop and is a Python artefact, not a model cost.

### 5.1 C# parity run (Phase B1, measured)

`OnnxOcrParityTests` runs `OnnxOcrEngine` (ONNX Runtime 1.29.0, Skia decode, managed everything else) on the same 20 images. It skips cleanly when `research/data` or the reference outputs are absent.

| Comparison | Result |
|---|---|
| C# vs Python ONNX reference (non-empty reference lines) | **565/565 lines matched (100 %), text identical 100 %, CER 0.00 %, mean IoU 1.000** (required: ≥ 99 % and ≤ 1.5 %) |
| C# vs Paddle Inference baseline (`pine960`) | 99.3 % lines (555/559), 10 extra, text identical 93.3 %, CER 0.89 %, mean IoU 0.949 (Python ONNX gave the same numbers) |
| Ground-truth word recall | C# 92.4 % (= Python), Paddle 91.8 % |
| Latency, CPU x64, includes first-scan model load | mean 1.0 s per image, max 3.2 s (SROIE page); 20 s for all 20 |

The C# port is bit-for-bit on the decisions that matter because the resize and the warp are managed loops that reproduce `cv2.INTER_LINEAR`, not `SKBitmap.Resize` (Skia only decodes). The Python reference keeps 2 lines that decode to empty text; the engine drops lines with no text, and the comparison drops them from the reference too.

## 6. Design: `OnnxOcrEngine` and `SkiaImagePreprocessor`

```
Pine.Core            IOcrEngine   Task<IReadOnlyList<OcrWord>> ExtractTextAsync(Stream, CancellationToken)   // new, 1 method
Pine.Infrastructure  PaddleOcrEngine : IOcrEngine        (Windows, unchanged behaviour)
                     OnnxOcrEngine   : IOcrEngine        (all heads)
                     SkiaImagePreprocessor               (decode+EXIF, resize, warp, normalise -> float[])
                     DbPostProcessor / CtcDecoder        (pure managed, unit-testable, no ONNX, no Skia)
```

* `IOcrEngine` is extracted from the public surface of `PaddleOcrEngine` so that `DefaultLocalAiProvider` receives it by constructor injection instead of `new`. Keep the sentinel-word behaviour out of the new interface: failures should throw, and the provider maps them (the sentinels `ERROR_MODELS_NOT_FOUND`/`OCR_ERROR_CRASH` are an existing oddity; keep them in `PaddleOcrEngine` for compatibility and decide separately).
* `OnnxOcrEngine` owns two `InferenceSession`s created lazily under a `SemaphoreSlim`, like `ReceiptDetector.EnsureInitializedAsync`. Session options: `GraphOptimizationLevel.ORT_ENABLE_ALL`, `IntraOpNumThreads = min(4, cores)`, no NNAPI in v1 (op coverage and numerics unverified). `ExtractTextAsync` runs on the thread pool; **no work on the UI thread** (the same defect `PaddleOcrEngine` documents at L90-L100).
* `DbPostProcessor` and `CtcDecoder` are `internal static` with array-in/array-out signatures. They are the part that needs byte-for-byte unit tests: port the Python reference, generate golden fixtures from it (probability map → boxes; logits → text), the same technique as `scripts/gen_layoutlm_csharp_fixtures.py`.
* `SkiaImagePreprocessor` returns **BGR** float tensors (§4). Bitmaps are disposed promptly: a 12 MP camera photo decodes to ~48 MB; decode with `SKCodec` and a scale hint down to the 960 px det size when the full-resolution pixels are not needed, but the rec crops are cut from the **full-resolution** image.
* Models are resolved from a configurable directory (`PineOptions`-owned path, default `AppContext.BaseDirectory/Assets/Ai/ocr/`); on Android that is an extracted asset (§7).
* Logging follows the existing rule: counts and mean confidence only, never recognised text (`30-Observability` §4.3, TD-25).

### 6.1 Plugging into the pipeline and the capability table

1. `PineInfrastructureOptions` today has `IncludeReceiptVision` (set from `capabilities.HasReceiptVision`, `PineCompositionRoot.cs:114`). Add `OcrBackend` = `{ None, Paddle, Onnx }` rather than a second boolean; the existing comment block at `PineInfrastructureOptions.cs:27` ("a fact about the package graph") is rewritten to say vision is now portable.
2. `AddAi` registers `IOcrEngine` per backend, and `DefaultLocalAiProvider`'s constructor takes `IOcrEngine` and `IReceiptDetector` (it already holds `ReceiptDetector` by concrete type).
3. **Capability table (`PineHeadCapabilities`)**: add a granular value instead of overloading one flag: `ReceiptVision` becomes `{ None, OcrOnly, Full }`. Android moves `None → OcrOnly` at the end of Phase C (OCR + heuristics + LayoutLM, no YOLO crop), and `→ Full` when the detector is ported (B2). The `HasReceiptVision` test rows and the claim "macOS and Android have no vision" (`PineHeadCapabilities.cs` comments, `PineHeadCapabilitiesTests`) are updated **only after** a device run proves it — the existing comment on macOS says an untested `true` is worse than an honest `false`; that rule applies here.
4. macOS/iOS get the same managed stack at no extra package cost, but remain `false` until someone runs them (no runner; TD-44).
5. **Windows recommendation: switch to the ONNX engine after Phase B parity is re-measured in C#, and keep `PaddleOcrEngine` for one release behind `OcrBackend.Paddle`.** Reasons: one code path to test, removes 245 MB of NuGet models + the Paddle runtime from the Windows artefact (the package graph in `Pine.Infrastructure.csproj` and `docs/60` TD-41), 4-10× faster in Phase A. Do not delete Paddle until the C# run reproduces §5.

### 6.2 Architecture tests and the ONNX allow-list

`ArchitectureTests.cs:1290` — `TypesAllowedToUseOnnx = ["LayoutLmFieldExtractor", "ReceiptDetector"]` (test `TheOnlyTypeUsingTheOnnxRuntime_IsTheOneThatRunsInference`, L1317-L1341). Changes:

* Add `"OnnxOcrEngine"` (it genuinely calls `session.Run`, which is the test's stated entry condition). `DbPostProcessor`, `CtcDecoder` and `SkiaImagePreprocessor` must **not** name the ONNX runtime, and the test then enforces that they don't.
* Add a sibling rule: `Pine.Ui.Avalonia` may not depend on `SkiaSharp` types through Infrastructure (mirror of `ArchitectureTests.cs:517-555`).
* `TunedConstantsBoundaryTests` / `13-AI-Architecture` §7.1: the new engine adds tuned constants (limit 960, 0.3/0.7/1.5, min rec width 320, row tolerance 10). Mark `OnnxOcrEngine` and `DbPostProcessor` with `TUNED-CONSTANTS BOUNDARY (TD-118)` and extend §7.1's list of classes, or the test fails. Constants stay literals, not `PineOptions`.
* `ShippedModelAssetsAreNamedByProductionCode`: the three new files must be named (not globbed) in `Pine.Infrastructure.csproj` **and** in the production code that loads them, or the test fails by design.
* `NetworkEgressTests` is unaffected (models ship in the package; nothing downloads).
* Add `SkiaSharp` as an explicit `PackageReference` in `Pine.Infrastructure` via `Directory.Packages.props` at the version Avalonia already resolves (3.119.3-preview.1.1 — **a preview**; check that a stable pin is acceptable before choosing), plus `SkiaSharp.NativeAssets.*` only where a head does not already get them from Avalonia.

## 7. Packaging, licence, size budget

* **Location:** `src/Pine.Infrastructure/Assets/Ai/ocr/{det.onnx, rec.onnx, dict_latin_v5.txt}`, `<Content CopyToOutputDirectory="PreserveNewest">` like `yolo-receipt-nano.onnx` (named, not globbed — the rule written into the csproj after TD-41). Windows/desktop outputs get them automatically through the `ProjectReference` chain.
* **Android:** the same content must become `AndroidAsset` (the existing `Content` copy does not reach the APK, and `AppContext.BaseDirectory` is not a writable/readable file path for assets). Phase C: link the three files as `AndroidAsset` in `Pine.Ui.Android.csproj` and copy them once to `FilesDir` on first use (ONNX Runtime wants a path or a byte[]; a `byte[]` from `AssetManager.Open` also works and avoids the copy, at the price of 13 MB resident). YOLO has the same problem and must be handled in the same change.
* **B1 decision:** the three files are committed under `src/Pine.Infrastructure/Assets/Ai/ocr/` and named in `Pine.Infrastructure.csproj` (the same way as `yolo-receipt-nano.onnx`). `.gitattributes` already routes `*.onnx` through Git LFS (13 MB + 8 MB + 5 MB is below the existing YOLO blob), so no history bloat. `OnnxOcrOptions.FilesArePresent` treats an LFS pointer stub as absent, so tests skip rather than fail on a checkout without LFS. `Pine:Ai:OcrModelDirectory` overrides the folder.
* **Budget:** ≈ 12.9 MB of model files + ~0.5 MB tokenizer/other. APK impact ≈ 13 MB (compresses poorly: float weights). For comparison the Windows artefact currently carries 245 MB of Paddle models plus native runtimes, which is the larger win.
* **Licence:** PaddleOCR and PP-OCRv5 models are **Apache-2.0** (PaddlePaddle authors, Baidu). The detection weights were redistributed as ONNX by RapidOCR (Apache-2.0); the recognition weights were re-serialised by this project from the Sdcb package (Sdcb.PaddleOCR is Apache-2.0 as well). Pine is GPLv3; Apache-2.0 is compatible one way (Apache into GPLv3). Record in `THIRD-PARTY-NOTICES` (or the project's equivalent): copyright notice + licence text + "modified: converted to ONNX / graph re-weighted" (Apache §4(b)). **The licence of the Sdcb-redistributed copy and the dictionary file should be confirmed against the upstream repository before release; Phase A did not audit it.**
* Reproducibility: the build recipe (`paddle_rec_to_onnx_transplant.py` + donor SHA-256s) lives in `research/scripts/`; the 13 MB binaries are committed to the repo only if the project accepts a one-time blob (git history already contains three 100 MB blobs per TD-130; consider Git LFS or a release asset with a hash check instead).

## 8. Latency and memory budget (target: Samsung Galaxy Tab S6 Lite class, arm64, ~4 GB RAM)

**Measured on this PC (x64, CPU):** after loading both sessions the process grows ~32 MB; one 960×320 detection raises it by ~63 MB, a rec call by ~15 MB: ≈ 110 MB peak above an empty interpreter with 13 MB of weights. A full 960×736 map should be roughly 2× the detection activations (**estimate** ≈ 150-250 MB). Peak decoded bitmap for a 12 MP photo ≈ 48 MB (+ a downscaled copy).

**Estimate, not measured:** the S6 Lite (Exynos 9611, 4×A73 + 4×A53) is roughly 3-6× slower than this PC per thread. Phase A took 0.4-2.8 s per receipt on x64 → plan for **3-12 s per receipt** on the tablet with 4 threads, dominated by recognition of 30-80 lines; mitigations: batch rec crops of similar width, cap lines at ~100, run det at ≤ 736 px long side for phone photos, FP16/INT8 (unmeasured). The budget the product should accept is "≤ 15 s with a progress indicator, ≤ 400 MB peak, UI thread never blocked". Peak with LayoutLM (separate session) must be re-measured; do not hold all three sessions at once on a 4 GB device — dispose the OCR sessions before creating LayoutLM's if the total exceeds budget.

## 9. Task breakdown

**Phase B — engine + tests (Infrastructure only, no UI, runs on Windows CI)**
1. [x] B1 `IOcrEngine` in Pine.Core; `PaddleOcrEngine` implements it; `DefaultLocalAiProvider` takes it via DI; tests updated.
2. [x] B1 `DbPostProcessor`, `CtcDecoder` (pure C#, ports of the reference) + golden fixtures generated from `onnx_ocr_reference.py`; unit tests with tolerance on box coordinates (±1 px) and exact text.
3. [x] B1 `SkiaImagePreprocessor` + `OnnxOcrEngine`; models as named content; ArchitectureTests/TunedConstants/ShippedModelAssets updates (§6.2); an integration test with a real fixture receipt (skip if models absent).
4. [x] B1 **Parity run in C#** (done: section 5.1; 100 % / 0.00 % CER vs the Python reference) against `pine960_*` reference numbers in §5 on the same 20 images; must reproduce ≥ 99 % line match and ≤ 1.5 % CER vs the Python reference, else stop.
5. [~] B2 (done classically, see section 13; the YOLO port is not done): `SkiaReceiptDetector` replacing the OpenCV half of `ReceiptDetector` (§2.1) and `SkiaImageProcessor : IImageProcessor`; re-run the YOLO fixtures; threshold/sharpen differences go through the §7.1 re-evaluation. `LayoutLmImagePreprocessor` decode via Skia.
6. [ ] B3 Switch Windows to `OcrBackend.Onnx` only after 4 passes; keep Paddle one release.

**Phase C — Android wiring (C-minimal implemented, see §12)**
1. [x] File picker (Avalonia `StorageProvider`) → `Stream` → `IAIProvider.ExtractReceiptDataAsync`; image downscale policy (`SkiaReceiptDetector`, 2048 px long side); EXIF orientation (Skia).
2. [x] Models as `AndroidAsset`, first-run copy to the files directory (`OcrModelInstaller`). YOLO is **not** bundled: its pre/post-processing is OpenCV, so nothing would read it (B2).
3. [x] `PineHeadCapabilities`: `Android → OcrOnly` (`Full` after B2), tests; composition test (`AndroidOcrOnlyCompositionTests`) that the container resolves `IAIProvider`, the OCR engine and the scan page on the Android profile without any OpenCV-bound type.
4. [ ] **LayoutLM on device**: code path works (Skia decode, no OpenCV) and is picked up from `files/layoutlm/model.onnx` (§12); memory/latency measurement and the default decision are open.
5. [ ] Measure on the real tablet: latency per stage, peak RSS, APK size delta. Update §8 with numbers; until then they are estimates (§12 says what was measured).

**Phase D — live camera:** done as capture-on-tap, see section 13: `AndroidCameraSource : ICameraSource` (Camera2), viewfinder and Capture in `ScanReceiptViewModel`. Continuous preview analysis is not built.

## 10. Risks and what is unverified

* **Unverified:** all Android numbers (latency, memory, APK size); ORT 1.29.0 loading these opsets on arm64 (tested only with ORT 1.32 on x64 Python); the C# port's numerical parity (Python reference only); SkiaSharp resize/warp behaviour matches the numpy bilinear used here (Skia's bilinear sampling differs slightly; the 0.948 mean IoU may drop a little).
* **Evidence scope:** 20 images, 559 lines, two datasets (CORD, SROIE). No Spanish receipts and no phone camera photos (skew, blur, glare, EXIF rotation) were measured. Recall on Spanish diacritics is only as good as the dictionary: 836 characters include `áéíóúñÁÉÍÓÚÑüÜ¿¡€`, but accented text was not tested.
* **Detector is the bigger porting job** than OCR (§2.1), and its adaptive threshold is the input the OCR sees today: the Skia replacement changes what the OCR is fed. Mitigation: first milestone runs OCR on the original (colour) image; measure with and without binarisation.
* **`rec.onnx` provenance:** produced by a graph-transplant, validated at 98.3 % identity to Paddle on 603 lines, not exhaustively. The donor graph is a third party's export; if RapidOCR changes it, the recipe breaks (hashes pinned in the script's usage).
* `SkiaSharp 3.119.3-preview.1.1` is a preview package transitive from Avalonia 12.
* **Android 16 KB page size / ABI:** `libonnxruntime.so` for arm64 is present; 16 KB alignment for Android 15+ devices is untested.
* **Latency on the tablet** may exceed 15 s with 80-line receipts; the user-visible fallback (manual entry) must remain.
* Models are float32 with ~13 MB RAM and no quantisation; INT8 recognition may change CER — unmeasured.

## 12. Android phase C-minimal (as built)

* **Pipeline on Android:** system picker -> `Stream` -> `SkiaReceiptDetector` (decode, EXIF, shrink to 2048 px long side; re-encoded as JPEG only when it had to change, otherwise the original bytes are used) -> `OnnxOcrEngine` -> `HeuristicFieldExtractor` -> LayoutLM overlay when configured. No YOLO, no crop, no binarisation (OCR runs on the colour image, the §10 mitigation). `SkiaReceiptDetector` implements `IReceiptDetector`, the seam where B2 slots in (its Detect methods throw `AiCapabilityUnavailableException` until then).
* **Capabilities:** `ReceiptVisionLevel { None, OcrOnly, Full }`; `HasReceiptScan` vs `HasReceiptVision` (docs/13 section 7.4). The shell shows "Live scanning and manual crop are not available on this device" on Android instead of the generic note.
* **Models:** `det.onnx`, `rec.onnx`, `dict_latin_v5.txt` are `AndroidAsset` items (stored uncompressed) and are copied once to `<files>/ocr-models/` (size-compared on later starts); `Pine:Ai:OcrModelDirectory` is set to that folder unless configured.
* **LayoutLM on Android:** not bundled (CC BY-NC-SA, ~128 MB). Copy `model.onnx` and `tokenizer.json` to `/sdcard/Android/data/org.pine.app/files/layoutlm/` (e.g. `adb push model.onnx /sdcard/Android/data/org.pine.app/files/layoutlm/`; the app creates `files/` on first launch, create the folder with `adb shell mkdir -p` if needed) and restart the app. The app's own `appsettings.json` lives in the private `LocalApplicationData/Pine` directory, which `adb` cannot write on a release build, so this convention is the supported route. An explicit `Pine:Ai:LayoutLmModelPath` still wins.
* **Diagnostics:** each scan raises `ScanDiagnostics.Reported` and the Android head logs it as `adb logcat -s PineScan` (`scan prep_ms=... ocr_ms=... lines=N heuristics_ms=... layoutlm_ms=...`): counts and times only, never text.
* **Left for B2/D:** a Skia YOLO crop + perspective warp behind `IReceiptDetector` (then `Android -> Full` minus the camera, or add a capability for crop without live), `SkiaImageProcessor : IImageProcessor` for the crop screen, and the camera source (Phase D).

## 13. B2 and phase D as built (Android)

* **Crop:** `SkiaReceiptDetector` now finds the receipt with `ReceiptQuadFinder` (320 px greyscale, box blur, Otsu, both polarities, largest connected
  region of 12-95 % of the frame, fill ratio >= 0.8 of its maximum-area inscribed quadrilateral) and warps it with a homography drawn by Skia
  (`SKMatrix` with perspective terms). The cropped colour image (not binarised) goes to OCR. No quad means the previous EXIF + downscale behaviour.
* **Why not YOLO:** the model is a segmentation model; its mask, contour and polygon steps are OpenCV code, cannot be checked without receipt photos on a
  device, and bundling ~5 MB nobody reads is worse than a deterministic, unit-tested finder. Allow-list unchanged. Known limits: a receipt of the same
  brightness as its background, a receipt cut by the frame edge, strong shadows splitting the sheet; all fall back to the uncropped picture.
  Tested on synthetic pictures only (rotated 0/12/-25/70 degrees, perspective, dark-on-light, none, full frame, speck); no real photo has been run.
* **Camera:** `ICameraSource` (Pine.Core, TD-120) already existed for the OpenCV webcam; Android adds `AndroidCameraSource` (Camera2, back lens, YUV up to
  ~2 MP, rotated upright, BGRA in memory) registered through `PineCompositionRoot.Build(..., configureHeadServices)`. The manifest declares CAMERA (and
  camera `required=false`) and removes INTERNET and ACCESS_NETWORK_STATE (the ONNX Runtime AAR declares them and the manifest merger would otherwise add them; found on the tablet with `dumpsys package`). UNVERIFIED on a device: permission prompt, preview, orientation, capture.
* **YOLO on Android (B2, second step):** the same `yolo-receipt-nano.onnx` as Windows is bundled as an AndroidAsset (13 MB, copied with the OCR models) and run by `YoloReceiptSegmenter` (ONNX Runtime, SkiaSharp letterbox, threads = cores - 2) with the managed `YoloPostProcessor`: best anchor of >= 64 px boxes, confidence >= 0.85, mask = sigmoid(coefficients . prototypes) upsampled bilinear, largest region, maximum-area inscribed quadrilateral, then the same three refusals as `ReceiptDetector` (touches the frame edge, aspect < 1.15, < 10 % of the frame). Chain in `SkiaReceiptDetector`: YOLO, then the classical `ReceiptQuadFinder`, then EXIF + downscale. Capability level stays `OcrOnly` (the head still has no OpenCV crop screen); the detector is an implementation detail behind `IReceiptDetector`. Parity on 80 real photos from `research/data/roboflow`: OpenCV accepted 10, the portable detector accepted 10, and for 9 of those the crop size agreed within 8 % (test `On_real_photos_the_portable_detector_agrees_with_the_opencv_detector`). Per-scan timings: `adb logcat -s PineScan` line `detect source=yolo yolo_infer_ms=...`. Device cost of YOLO per frame is NOT measured yet.
* **Left:** full-resolution JPEG still capture, continuous preview analysis (auto-capture when a sheet is steady), YOLO crop.

## 14. Image enhancement for OCR, live scanning and photos of screens (Android)

**What Windows does and what Android does now.** After the warp, `ReceiptDetector` makes a sharpened copy for the screen
(3x3 kernel) and a binarised copy for OCR (grey, Gaussian 5x5, `AdaptiveThreshold` GaussianC, block 21, C 10).
`ReceiptEnhancer` ports both without OpenCV (separable Gaussian; sharpen is bit-identical to `Filter2D` on 5 research images; the
managed threshold differs from OpenCV on 0.1 % of pixels, rounding of the blur). The Android detector uses the sharpened
copy as the display image and sends **the colour crop** to OCR, because the data below say the binarisation hurts.

**Measured, PP-OCRv5 through `OnnxOcrEngine`** (ground-truth word recall, words of 3+ characters, the 20 CORD/SROIE test images;
`ReceiptEnhancementExperimentTests`). "Screen" is a synthetic photo of a screen made from the same images (sub-pixel stripes,
a moire beat, glare, gamma 1.4, blur 0.8, noise): it is a stand-in, not a measurement of real screens.

| OCR input | clean | screen |
|---|---|---|
| colour as is | 92.2 % | 61.8 % |
| binarise (Windows) | 78.1 % | 51.2 % |
| blur 1.5 + binarise | 60.9 % | 32.5 % |
| flatten (background divided out) | 89.8 % | 66.1 % |
| blur 1.2 + flatten | 87.8 % | 69.8 % |
| downscale 0.6 | 90.1 % | 67.9 % |
| **downscale 0.6 + flatten** | 88.5 % | **73.4 %** |

Decision: clean pictures go as they are; a picture that `ScreenPatternDetector` flags goes through 0.6x downscale + flatten
(+11.6 points on the synthetic screens, -3.7 on a clean picture wrongly flagged). The detector looks for a sharp peak in the
row/column power spectrum in the 2-6 px period band (JPEG block bins excluded): clean scans scored 5-340, synthetic screens
6000-34000, threshold 1500. **Provisional**: no real screen photo was available, and perfectly regular ruled lines also score high
(the synthetic receipts in the unit tests are flagged). If Windows moves to the ONNX engine, its binarised copy should be
re-measured the same way (docs/65 section 9, B3). Not done: telling the person "move closer/farther or use the original
photo" (the detector's verdict is only logged: `PineScan enhance mode=screen screen_score=...`).

**Live scan.** Edge detection runs on a 1024 px copy of the frame about every 0.7 s (adaptive); each probe also returns the
Laplacian variance of the receipt region at a fixed 256 px width (`PineScan probe ... sharpness=`). A frame counts toward
the automatic capture only if the outline is steady (corner movement under 3 %) and its sharpness is at least 70 % of the
sharpest seen in the session. The capture itself is the camera's own JPEG still (largest size up to ~13 MP, Camera2
`StillCapture` into a second `ImageReader`, JPEG orientation set so it decodes upright); if the device refuses
preview + still in one session it falls back to the viewfinder frame. The editor works on a 2048 px copy, the crop is taken from
the original (`CropOriginalAsync`, turns applied) and the pipeline's own cap is 3072 px. **Not verified on a device**: still
capture, the session combination on this tablet, memory at 8-12 MP, and whether the sharpness gate improves real reads.

## 11. Recommendation

**Go for Phase B (B1).** The model question — the part most likely to block — is resolved with no quality loss measured against Pine's current engine, in 13 MB, and the algorithm is already ported to portable code. **Do not promise Android scanning from B1 alone**: the OpenCV-based `ReceiptDetector`/`OpenCvImageProcessor` (§2.1) still need a Skia port (B2) or an explicit "OCR on the original image" milestone, and nothing about the tablet has been measured. Re-evaluate after the C# parity run (B1 step 4) and after the first real-device measurement (C5).
