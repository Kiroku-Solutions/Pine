using Foundation;
using Avalonia;
using Avalonia.iOS;
using Pine.Ui.Avalonia.Composition;

namespace Pine.Ui.Ios;

[Register("AppDelegate")]
#pragma warning disable CA1711
public partial class AppDelegate : AvaloniaAppDelegate<Pine.Ui.Avalonia.App>
#pragma warning restore CA1711
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        // Build the container (TD-29). Same story as the Android head: its own copy of the
        // registrations, missing four nav targets (TD-85) and the AI subsystem (TD-90). Both are
        // now read from PineHeadCapabilities.For(Ios).
        var serviceProvider = PineCompositionRoot.Build(PineHeadProfile.Ios);

        // Hand the UI what it needs, by name (TD-28) -- a typed record, not the container.
        PineCompositionRoot.InitializeUi(serviceProvider);

        // Start the idle auto-lock watchdog (TD-11). No generic host on this head.
        PineCompositionRoot.Start(serviceProvider);

        return base.CustomizeAppBuilder(builder)
            .WithInterFont();
    }
}
