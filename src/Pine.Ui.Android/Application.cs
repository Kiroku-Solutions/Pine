using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using Pine.Ui.Avalonia.Composition;

namespace Pine.Ui.Android
{
    [Application]
    public class Application : AvaloniaAndroidApplication<Pine.Ui.Avalonia.App>
    {
        protected Application(nint javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
        {
        }

        /// <summary>
        /// Checks for a user-supplied override at <c>&lt;app external files&gt;/layoutlm/model.onnx</c>
        /// (<c>/sdcard/Android/data/org.pine.app/files/layoutlm/</c>), falling back to the LayoutLMv3
        /// model bundled inside the APK when present.
        /// </summary>
        private (string? ModelPath, bool NeedsBundledInstall) ResolveLayoutLmModel()
        {
            var directory = GetExternalFilesDir(null)?.AbsolutePath;
            if (directory is not null)
            {
                var externalModel = System.IO.Path.Combine(directory, "layoutlm", "model.onnx");
                var externalTokenizer = System.IO.Path.Combine(directory, "layoutlm", "tokenizer.json");
                if (System.IO.File.Exists(externalModel) && System.IO.File.Exists(externalTokenizer))
                {
                    return (externalModel, false);
                }
            }

            if (OcrModelInstaller.HasBundledLayoutLm(this))
            {
                return (OcrModelInstaller.GetBundledLayoutLmTargetPath(this), true);
            }

            return (null, false);
        }

        protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
        {
            // Build the container (TD-29). The Android row is OcrOnly (docs/65 phase C): the managed ONNX OCR engine
            // and YOLO receipt detector, plus the bundled LayoutLMv3 INT8 field extractor when present in APK assets.
            var ocrModels = OcrModelInstaller.Install(this);
            var (layoutLm, needsLayoutLmInstall) = ResolveLayoutLmModel();
            // Unpack the 125 MB bundled LayoutLMv3 checkpoint off the UI thread so first launch never hits Android's ANR budget.
            var layoutLmInstallTask = needsLayoutLmInstall
                ? System.Threading.Tasks.Task.Run(() => OcrModelInstaller.InstallLayoutLm(this))
                : System.Threading.Tasks.Task.FromResult<string?>(layoutLm);
            Pine.Infrastructure.Ai.ScanDiagnostics.Reported += line => global::Android.Util.Log.Info("PineScan", line);

            var serviceProvider = PineCompositionRoot.Build(PineHeadProfile.Android, options =>
            {
                options.Ai.OcrModelDirectory ??= ocrModels;
                if (layoutLm is not null && string.IsNullOrWhiteSpace(options.Ai.LayoutLmModelPath))
                {
                    options.Ai.LayoutLmModelPath = layoutLm;
                }
            },
            services => services.UseCameraSource<AndroidCameraSource>());

            // Hand the UI what it needs, by name (TD-28) -- a typed record, not the container.
            PineCompositionRoot.InitializeUi(serviceProvider);

            // Start the idle auto-lock watchdog (TD-11). No generic host on this head.
            PineCompositionRoot.Start(serviceProvider);

            // Warm the receipt models in the background a moment after start-up: loading the YOLO, detection and recognition sessions and
            // running them once cost seconds on the first scan; done here, nobody waits for it. Nothing is read or kept from the blank picture.
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    await System.Threading.Tasks.Task.Delay(3000);
                    using var blank = new SkiaSharp.SKBitmap(640, 640, SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Opaque);
                    blank.Erase(new SkiaSharp.SKColor(200, 200, 200));
                    using var image = SkiaSharp.SKImage.FromBitmap(blank);
                    var jpeg = image.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 80).ToArray();
                    if (serviceProvider.GetService(typeof(Pine.Infrastructure.Ai.Vision.IReceiptDetector)) is Pine.Infrastructure.Ai.Vision.IReceiptDetector detector)
                    {
                        await detector.AnalyzeAndCropReceiptAsync(new System.IO.MemoryStream(jpeg));
                    }

                    if (serviceProvider.GetService(typeof(Pine.Core.Ai.IOcrEngine)) is Pine.Core.Ai.IOcrEngine ocr)
                    {
                        await ocr.ExtractTextAsync(new System.IO.MemoryStream(jpeg), System.Threading.CancellationToken.None);
                    }

                    // Ensure bundled LayoutLMv3 asset extraction finished before warming up its ONNX session.
                    await layoutLmInstallTask.ConfigureAwait(false);
                    if (serviceProvider.GetService(typeof(Pine.Core.Ai.IVisualFieldExtractor)) is Pine.Core.Ai.IVisualFieldExtractor layout)
                    {
                        await System.Threading.Tasks.Task.Delay(1000);
                        Pine.Core.Ai.OcrWord Word(string text, float x, float y) => new()
                        {
                            Text = text,
                            Confidence = 1f,
                            BoundingBox = new Pine.Core.Ai.BoundingBox { X = x, Y = y, Width = 120, Height = 24 },
                        };
                        layout.ExtractFields([Word("SHOP", 40, 20), Word("TOTAL", 40, 300), Word("10.00", 400, 300)], jpeg);
                    }

                    global::Android.Util.Log.Info("PineScan", "warm-up done");
                }
                catch (System.Exception ex)
                {
                    global::Android.Util.Log.Info("PineScan", $"warm-up skipped: {ex.GetType().Name}");
                }
            });

            return base.CustomizeAppBuilder(builder)
                .WithInterFont();
        }
    }
}
