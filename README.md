<div align="center">
  <img src="public/PineLogo.svg" alt="Pine Logo" width="128" />
  <h1>Pine</h1>
  <p><strong>A Privacy-First, Offline-First Personal Finance Engine</strong></p>
</div>

---

| | |
|---|---|
| **Status** | Partially implemented — alpha. Do not put real money in it yet |
| **Reconciled** | 2026-08-31 · Rewritten against the code at remediation iteration 21 (TD-39). Every claim below is either verified in the tree or marked as not yet true |
| **Version** | `0.1.0-alpha.1` — no release has ever been published |

> **Read this before anything else.** Pine has never been released, has no installer, and is not
> signed. An internal production-readiness audit
> ([`docs/60-Production-Readiness-Audit.md`](docs/60-Production-Readiness-Audit.md)) rates it
> **2.3 / 5 — Not Production Ready** and tracks 110 numbered defects and gaps, a remediation cycle
> against them, and exactly what is and is not true of each subsystem. It is the honest document
> about this project; this README is the short version of it.

## The Vision

**Pine** is a personal finance application for people who do not want their financial data on
someone else's computer. It is **offline-first** and stores everything in a locally encrypted vault.

Your money. Your data. Your control.

### What is actually true today

- **No network egress, anywhere.** There is no `HttpClient`, no socket, and no analytics or
  crash-reporting SDK in `src/` — a property enforced by review rather than merely intended, and
  recorded in [`docs/adr/0012-no-telemetry.md`](docs/adr/0012-no-telemetry.md). Nothing to opt out
  of, because there is nothing to opt in to.
- **Encryption at rest.** The database is SQLCipher-encrypted. The key is a random 256-bit data key
  wrapped with AES-256-GCM under an auth key that Argon2id derives from your master password, bound
  to every other field of the vault header — see
  [`docs/adr/0013-encryption-baseline.md`](docs/adr/0013-encryption-baseline.md). Pine cannot read
  your data without your password, and neither can anyone who copies the files. **There is no
  recovery path if you forget it.**
- **Accounts, transactions, imports and receipt scanning** are real and work: cash, bank, card,
  investment and crypto accounts; CSV/Excel import with stable-key deduplication and a review step;
  on-device OCR that never sends an image anywhere and cannot write to the ledger without your
  explicit confirmation.
- **.NET 10, Avalonia UI, CQRS via MediatR, hexagonal architecture.** The layering is real: the
  domain project has no external dependency and the UI dispatches through MediatR.
- **Rules file transactions for you, and they beat the classifier.** A rule is a standing
  instruction — "payee contains STARBUCKS → Coffee" — with a priority, conditions that must all
  hold, and a switch to turn it off without deleting it. When a rule matches an imported row, the
  on-device classifier is not consulted at all: an instruction you wrote is not a guess to be
  weighed against a confidence score. Rules never overwrite a category you typed yourself, and
  never rewrite transactions already in the ledger.
- **A scanned receipt is kept with the transaction it produced.** The image is stored inside the
  encrypted vault database, hashed with SHA-256 when it is attached and verified every time it is
  opened, so a receipt that no longer matches its fingerprint is reported rather than shown. It is
  written in the same database transaction as the ledger row, and it is inside every backup —
  because the backup copies the database.
- **The interface is available in English and Spanish, and switches without a restart.** Every
  string in every view comes from a JSON table rather than from the markup, and choosing a language
  in Settings re-renders the running application. The preference is stored outside the vault, so
  the unlock and vault-selection screens are translated too — they are rendered before any vault is
  open. Adding a third language is one JSON file (TD-45).

### What is not true yet

- **Windows and Android are the targets for the academic demo.** The Android build was tested on a
  Galaxy Tab S6 Lite. The macOS and iOS projects share the UI and are outside the scope of this demo.
- **There is no WebAssembly head, and that is now a decision rather than an omission.** One existed
  until the Phase 3 platform review, and it could not run the product on three independent counts:
  the browser runtime provides no AES-GCM, so no vault could ever be unwrapped there; OpenCV and
  PaddleInference publish no WebAssembly build, so receipt scanning could never exist there; and
  `docs/02-Product-Scope` already listed browser-only distribution as an explicit non-goal. It was
  deleted rather than left as source, because a head that compiles is a head that looks supported.
- **A transaction cannot be edited.** Correction is void-and-re-enter, which is the right default
  for a ledger and costs two rows with no link between them (TD-68).
- **The budgets screen does not compute anything.** Its progress bar is hard-coded to zero (TD-99).
- **No installer, no code-signing certificate, and no update mechanism.** An update check would be
  Pine's first network call ever, so it is a privacy decision and not a packaging chore. Backups can
  now be made *and restored* from Settings (TD-56, closed 2026-09-10): a restore refuses anything it
  cannot verify first, keeps a copy of what it replaces, and locks the vault afterwards.
- **CI is armed but the release path is not.** Every pull request into `main` and every push to it
  builds all four heads and runs the suite (TD-07, closed). What still has no automation is the
  step after that: no installer, no signing certificate, no release workflow.

---

## Architecture

Pine was engineered from the ground up using strict **Clean / Hexagonal Architecture** principles.

1. **Domain-Driven Design (DDD):** The core engine uses rich Domain Entities (`AggregateRoot`, `ValueObject`, `DomainEvent`) to eliminate primitive obsession (e.g., using a `Money` Value Object instead of raw decimals).
2. **CQRS via MediatR:** Every action in Pine is segregated into `Commands` (state mutations) and `Queries` (data retrieval), with validation and transaction behaviours in the pipeline so a rejected command never opens a database transaction.
3. **Composition Roots:** The UI layer (`Pine.Ui.Avalonia`) is OS-agnostic. The executables (`Pine.Ui.Windows`, `Pine.Ui.Macos`, `Pine.Ui.Android`, `Pine.Ui.Ios`) are composition roots that bootstrap the native OS window. They no longer register services of their own — `PineCompositionRoot` does that for all four (TD-29, TD-85, TD-90).

## Getting Started

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), using the version in [`global.json`](global.json).
- [Git LFS](https://git-lfs.com/) for the ONNX weights and LayoutLMv3 tokenizer. From the repository root, run `git lfs install` and `git lfs pull`.
- For an Android build, install the .NET Android workload (`dotnet workload restore src/Pine.Ui.Android/Pine.Ui.Android.csproj`), Android SDK and JDK. For USB setup and diagnostics, use [Android SDK Platform-Tools](https://developer.android.com/tools/releases/platform-tools).

### Run on Windows

From the repository root in PowerShell, run these commands to use ONNX OCR and the optional LayoutLMv3 extractor:

```powershell
$env:PINE_Pine__Ai__OcrBackend = 'Onnx'
$env:PINE_Pine__Ai__LayoutLmModelPath = (Resolve-Path 'research/checkpoints/layoutlmv3/model.onnx').Path
$env:PINE_Pine__Ai__LayoutLmTokenizerPath = (Resolve-Path 'research/checkpoints/layoutlmv3/tokenizer.json').Path
dotnet run --project src/Pine.Ui.Windows
```

For OCR with heuristic extraction, launch Pine without the LayoutLMv3 settings. In the app, create
or unlock a vault, open **Scan Receipt**, select a local receipt, then review the fields before saving.

### Run on an Android tablet

Build and install the signed ARM64 APK from the repository root. If Pine is already installed on
the tablet, start with the model setup below.

```powershell
dotnet publish src/Pine.Ui.Android/Pine.Ui.Android.csproj -c Release -r android-arm64
adb install -r src/Pine.Ui.Android/bin/Release/net10.0-android/android-arm64/publish/org.pine.app-Signed.apk
```

Open Pine once, then copy the research files to its app directory and reopen Pine:

```powershell
$modelDir = '/sdcard/Android/data/org.pine.app/files/layoutlm'
adb shell mkdir -p $modelDir
adb push research/checkpoints/layoutlmv3/model.onnx "$modelDir/model.onnx"
adb push research/checkpoints/layoutlmv3/tokenizer.json "$modelDir/tokenizer.json"
adb logcat -s PineScan
```

After a scan, `layoutlm=ok` confirms that the model contributed to extraction. Once the files are
on the tablet, the demo can run in airplane mode. Use the live camera or a receipt saved on the device.
The APK packages YOLO and OCR. Pine loads LayoutLMv3 from the local research files when available.

### Run tests

```bash
dotnet test tests/Pine.Tests.Unit/Pine.Tests.Unit.csproj
dotnet test tests/Pine.Tests.Integration/Pine.Tests.Integration.csproj
```

## Receipt scanning research

Pine finds the receipt and corrects its perspective, uses OCR to read the text, then extracts the
merchant, date, total and tax. The user reviews these fields before saving the transaction and its
receipt. Android uses Camera2 for capture, Skia for image preparation, and ONNX Runtime for inference.

| Model used in the demo | File size | Role and credit |
| --- | ---: | --- |
| [YOLOv8n-seg](src/Pine.Infrastructure/Assets/Ai/yolo-receipt-nano.onnx) | 13.3 MB | Receipt detection. [Ultralytics](https://github.com/ultralytics/ultralytics), adapted to receipts by Pine. |
| [PP-OCRv5 mobile detector](src/Pine.Infrastructure/Assets/Ai/ocr/det.onnx) | 4.8 MB | Text-line detection. [PaddleOCR](https://github.com/PaddlePaddle/PaddleOCR), ONNX export from [RapidOCR](https://github.com/RapidAI/RapidOCR). |
| [PP-OCRv5 Latin recognizer](src/Pine.Infrastructure/Assets/Ai/ocr/rec.onnx) | 8.1 MB | Text recognition. PaddleOCR weights, converted to ONNX by Pine using the RapidOCR graph. |
| [LayoutLMv3 INT8](research/checkpoints/layoutlmv3/model.onnx) | 128.4 MB | Field extraction. Microsoft and Huang et al., fine-tuned and quantized by Pine. See the [model card](research/docs/model_card.md) and [CC BY-NC-SA 4.0 attribution notice](research/docs/LAYOUTLMV3-NOTICE.md). |

In the Deliverable 2 experiment, all extractors received the same OCR output for 164 receipts from
CORD and SROIE. Macro-F1 across the four fields was **0.702** for the corrected rules, **0.894** for
LayoutLMv3, and **0.904** for the hybrid. The reported Android scan took about **8.9 s** in one run.
See the [reproduction steps](research/README.md#reproducir-la-tabla-del-entregable-2), the
[Android implementation notes](docs/65-Onnx-Ocr-Design.md), and the
[paper repository](https://github.com/camiloo1210/Pine-Docs).

---

## Project Structure

- `src/Pine.Core`: The Domain Layer. Contains entities, value objects, and business rules. No external dependencies.
- `src/Pine.Application`: The Use Case Layer. Contains CQRS Commands/Queries and interfaces.
- `src/Pine.Infrastructure`: The Persistence Layer. Implements repositories and the EF Core SQLCipher Database Context.
- `src/Pine.Ui.Avalonia`: The Cross-Platform UI Layer. Contains MVVM views, routing, and Fluent Design styles.
- `src/Pine.Ui.*`: Platform-specific bootstrappers (Windows, macOS, iOS, Android).

## Contributing
Please see our [CONTRIBUTING.md](CONTRIBUTING.md) and [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md) for details on how to help out.
