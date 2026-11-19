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
            });

            // Hand the UI what it needs, by name (TD-28) -- a typed record, not the container.
            PineCompositionRoot.InitializeUi(serviceProvider);

            // Start the idle auto-lock watchdog (TD-11). No generic host on this head.
            PineCompositionRoot.Start(serviceProvider);

            return base.CustomizeAppBuilder(builder)
                .WithInterFont();
        }
    }
}
