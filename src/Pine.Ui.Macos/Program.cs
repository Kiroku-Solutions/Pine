using Avalonia;
using System;
using Pine.Infrastructure.Diagnostics;
using Pine.Ui.Avalonia;
using Pine.Ui.Avalonia.Composition;

namespace Pine.Ui.Macos;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            // 1. Build the container. This head used to carry its own 90-line copy of the
            //    registrations, which is where its divergences lived: a singleton ShellViewModel
            //    (TD-72), no AI subsystem at all (TD-90), and four of the seven nav targets missing
            //    (TD-85). All of it now comes from PineCompositionRoot (TD-29).
            var serviceProvider = PineCompositionRoot.Build(PineHeadProfile.MacOs);

            // 2. Hand the UI what it needs, by name (TD-28) -- a typed record, not the container.
            PineCompositionRoot.InitializeUi(serviceProvider);

            // 2.5 Start the idle auto-lock watchdog (TD-11). This head runs no generic host, so
            //     nothing else would start it; it used to be armed by resolving it once and letting
            //     its constructor start the timer, which worked but was a second mechanism for the
            //     same job. Start() runs every IHostedService, on every head, the same way.
            PineCompositionRoot.Start(serviceProvider);

            // 3. Start Avalonia
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            // Record the crash so a windowed head does not swallow it silently (TD-25).
            //
            // PineCrashLogger is STATIC and takes no container on purpose: the most likely thing to
            // have thrown above is the container build itself, and a crash handler that needs the
            // container cannot record the failure of the container.
            PineCrashLogger.WriteCrash(ex);
            throw;
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
