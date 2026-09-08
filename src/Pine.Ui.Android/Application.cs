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

        protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
        {
            // Build the container (TD-29). This head used to carry its own copy of ~25
            // registrations; it was missing all four receipt/settings nav targets (TD-85) and the
            // whole AI subsystem (TD-90), so importing a CSV threw at handler resolution. Both are
            // now decided by PineHeadCapabilities.For(Android), in one table, next to the four
            // other heads' rows.
            var serviceProvider = PineCompositionRoot.Build(PineHeadProfile.Android);

            // Hand the UI what it needs, by name (TD-28) -- a typed record, not the container.
            PineCompositionRoot.InitializeUi(serviceProvider);

            // Start the idle auto-lock watchdog (TD-11). No generic host on this head.
            PineCompositionRoot.Start(serviceProvider);

            return base.CustomizeAppBuilder(builder)
                .WithInterFont();
        }
    }
}
