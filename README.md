<div align="center">
  <img src="public/PineLogo.svg" alt="Pine Logo" width="128" />
  <h1>Pine</h1>
  <p><strong>A Privacy-First, Offline-First Personal Finance Engine</strong></p>
</div>

## What is Pine?

Pine is an offline-first personal finance app for managing accounts and transactions, importing
CSV and Excel statements, and scanning receipts. It stores financial data in an encrypted local vault.

The receipt scanner turns a photo into a transaction. It reads the merchant, date, total, and tax on
the device, then lets you review the fields before saving the transaction and its receipt.

On Android, Pine uses Camera2 to capture the receipt, Skia to straighten it, and ONNX Runtime
to run the models. [Android project](src/Pine.Ui.Android/Pine.Ui.Android.csproj)

## Architecture

Pine uses .NET and Avalonia. Its hexagonal architecture separates domain rules and application use
cases from infrastructure and the UI. Commands and queries go through MediatR, and each platform
starts the shared UI through its own composition root.

## Getting started

### Prerequisites

Install Git LFS and the .NET SDK version in [global.json](global.json). From the repository root
in PowerShell, download the model files:

```powershell
git lfs install
git lfs pull
```

### Run on Android

Install a JDK and the Android SDK with Platform-Tools (`adb`). Connect an ARM64 Android device
with USB debugging enabled.

#### 1. Build and install

From the repository root in PowerShell:

```powershell
dotnet workload restore src/Pine.Ui.Android/Pine.Ui.Android.csproj
dotnet publish src/Pine.Ui.Android/Pine.Ui.Android.csproj -c Release -r android-arm64
adb devices
adb install -r src/Pine.Ui.Android/bin/Release/net10.0-android/android-arm64/publish/org.pine.app-Signed.apk
```

#### 2. Load LayoutLMv3

Open Pine once. Copy the model and tokenizer to the tablet, then restart Pine.

```powershell
$modelDir = '/sdcard/Android/data/org.pine.app/files/layoutlm'
adb shell mkdir -p $modelDir
adb push research/checkpoints/layoutlmv3/model.onnx "$modelDir/model.onnx"
adb push research/checkpoints/layoutlmv3/tokenizer.json "$modelDir/tokenizer.json"
```

#### 3. Scan a receipt

Create or unlock a vault and select an account. Open **Scan Receipt**, take a photo or select
a saved receipt, review the extracted fields, and save the transaction. The scan runs in
airplane mode after setup. During a USB test, `adb logcat -s PineScan` shows `layoutlm=ok`
when LayoutLMv3 contributes to the scan.

### Run on Windows

From the repository root in PowerShell, select ONNX OCR and the local LayoutLMv3 checkpoint, then
launch the [Windows app](src/Pine.Ui.Windows/Pine.Ui.Windows.csproj):

```powershell
$env:PINE_Pine__Ai__OcrBackend = 'Onnx'
$env:PINE_Pine__Ai__LayoutLmModelPath = (Resolve-Path 'research/checkpoints/layoutlmv3/model.onnx').Path
$env:PINE_Pine__Ai__LayoutLmTokenizerPath = (Resolve-Path 'research/checkpoints/layoutlmv3/tokenizer.json').Path
dotnet run --project src/Pine.Ui.Windows/Pine.Ui.Windows.csproj
```

### Run tests

From the repository root in PowerShell, point the model-dependent tests to the checkpoint and
run the [unit](tests/Pine.Tests.Unit/Pine.Tests.Unit.csproj) and
[integration](tests/Pine.Tests.Integration/Pine.Tests.Integration.csproj) suites:

```powershell
$env:PINE_LAYOUTLM_MODEL = (Resolve-Path 'research/checkpoints/layoutlmv3/model.onnx').Path
$env:PINE_LAYOUTLM_TOKENIZER = (Resolve-Path 'research/checkpoints/layoutlmv3/tokenizer.json').Path
dotnet test tests/Pine.Tests.Unit/Pine.Tests.Unit.csproj
dotnet test tests/Pine.Tests.Integration/Pine.Tests.Integration.csproj
```

## Model files and credits

| File | Size | Role and credit | Training data |
| --- | ---: | --- | --- |
| [YOLOv8n-seg](src/Pine.Infrastructure/Assets/Ai/yolo-receipt-nano.onnx) | 13.3 MB | Finds the receipt. [Ultralytics](https://github.com/ultralytics/ultralytics), adapted to receipts by Pine. | Private Roboflow receipt segmentation dataset (curated by Pine). |
| [PP-OCRv5 detector](src/Pine.Infrastructure/Assets/Ai/ocr/det.onnx) | 4.8 MB | Finds text lines. [PaddleOCR](https://github.com/PaddlePaddle/PaddleOCR), ONNX export by [RapidOCR](https://github.com/RapidAI/RapidOCR). | Pre-trained PaddleOCR public scene-text / document detection datasets. |
| [PP-OCRv5 Latin recognizer](src/Pine.Infrastructure/Assets/Ai/ocr/rec.onnx) | 8.1 MB | Reads the text lines. PaddleOCR weights converted to ONNX by Pine using a RapidOCR graph. | Pre-trained PaddleOCR public Latin text-recognition datasets. |
| [PP-OCRv5 Latin dictionary](src/Pine.Infrastructure/Assets/Ai/ocr/dict_latin_v5.txt) | 2.6 KB | Character dictionary for the PaddleOCR recognizer. | — |
| [LayoutLMv3 INT8](research/checkpoints/layoutlmv3/model.onnx) | 128.4 MB | Extracts receipt fields. [Microsoft](https://huggingface.co/microsoft/layoutlmv3-base) and [Huang et al.](https://doi.org/10.1145/3503161.3548112), fine-tuned, exported to ONNX, and quantized by Pine. [CC BY-NC-SA 4.0](https://creativecommons.org/licenses/by-nc-sa/4.0/). | Public receipt datasets: **CORD**, **SROIE**, and **WildReceipt**. |
| [LayoutLMv3 tokenizer](research/checkpoints/layoutlmv3/tokenizer.json) | 3.6 MB | Converts OCR text into tokens for the LayoutLMv3 checkpoint. | — |

## Project structure

- [`Pine.Core`](src/Pine.Core) contains domain entities, value objects, and business rules.
- [`Pine.Application`](src/Pine.Application) contains commands, queries, and application interfaces.
- [`Pine.Infrastructure`](src/Pine.Infrastructure) implements persistence and local receipt processing.
- [`Pine.Ui.Avalonia`](src/Pine.Ui.Avalonia) contains the shared UI. [Android](src/Pine.Ui.Android) and
  [Windows](src/Pine.Ui.Windows) provide platform-specific startup and services.
- [`research`](research) contains dataset preparation, LayoutLMv3 training, ONNX export, and evaluation.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) and [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md).
