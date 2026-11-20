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
        /// <c>&lt;app external files&gt;/layoutlm/model.onnx</c> (with <c>tokenizer.json</c> beside it), i.e.
        /// <c>/sdcard/Android/data/org.pine.app/files/layoutlm/</c> -- writable by <c>adb push</c> and the system file
        /// manager, readable by the app with no storage permission.
        /// </summary>
        private string? FindLayoutLmModel()
        {
            var directory = GetExternalFilesDir(null)?.AbsolutePath;
            if (directory is null)
            {
                return null;
            }

            var model = System.IO.Path.Combine(directory, "layoutlm", "model.onnx");
            return System.IO.File.Exists(model) ? model : null;
        }

        protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
        {
            // Build the container (TD-29). This head used to carry its own copy of ~25
            // registrations; it was missing all four receipt/settings nav targets (TD-85) and the
            // whole AI subsystem (TD-90), so importing a CSV threw at handler resolution. Both are
            // now decided by PineHeadCapabilities.For(Android), in one table, next to the four
            // other heads' rows.
            //
            // The Android row is OcrOnly (docs/65 phase C): the managed ONNX OCR engine, no OpenCV. The models
            // come out of the APK into the files directory, and the LayoutLM model -- never bundled -- is picked
            // up from the app's external files directory if the user copied one there. The OCR backend is Onnx by
            // construction on this head (the composition root); the paths below yield to explicit configuration.
            var ocrModels = OcrModelInstaller.Install(this);
            var layoutLm = FindLayoutLmModel();
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

                    // Then the LayoutLM model (session, tokenizer and one inference over a dummy page), last and unhurried: it is the biggest
                    // (128 MB) and its first load cost 1.4 s of the first scan.
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
