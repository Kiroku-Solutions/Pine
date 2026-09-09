using Avalonia;
using System;
using Pine.Infrastructure.Diagnostics;
using Pine.Ui.Avalonia;
using Pine.Ui.Avalonia.Composition;

namespace Pine.Ui.Windows;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            // 1. Build the container. Every registration this head used to spell out by hand now
            //    lives in PineCompositionRoot, which is shared by all four heads and reachable from
            //    a test (TD-29, TD-83). What is left here is this head's name.
            //
            //    THE SQLCipher PROVIDER INITIALISATION USED TO BE STEP 0 OF THIS METHOD. It was
            //    here and in no other head, defended by a comment whose reason ("each head's
            //    provider bundle differs") was false. It is now in Build(), so every head does the
            //    same thing. It was NOT a defect: Microsoft.Data.Sqlite arms the bundle by
            //    reflection whether or not anyone calls Init -- iteration 31 proved that with a
            //    clean-process probe after a mutation refused to die. Deleting it from here keeps
            //    the divergence from coming back with the same wrong justification.
            var serviceProvider = PineCompositionRoot.Build(PineHeadProfile.Windows);

            // 2. Hand the UI what it needs, by name (TD-28). This used to be
            //    App.InitializeServices(serviceProvider) -- the whole container parked in a public
            //    static that any type could reach into. InitializeUi resolves the two objects the
            //    Avalonia layer actually uses and hands those over instead.
            PineCompositionRoot.InitializeUi(serviceProvider);

            // 2.5 Start background services: the idle auto-lock watchdog (TD-11) and, on this head,
            //     categoriser training (TD-16).
            PineCompositionRoot.Start(serviceProvider);

            // 3. Start Avalonia
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            // Record the crash so a WinExe does not swallow it silently (TD-25).
            //
            // WAS: File.WriteAllText(%LOCALAPPDATA%/Pine/crash.log, $"{DateTime.UtcNow}\n{ex}") --
            // the whole exception, every inner exception and every stack frame, verbatim. A stack
            // trace from a build with PDBs carries the source path of each frame, and the message
            // itself routinely carries the vault path, so that file was a standing violation of
            // 30-Observability §4.3's third clause.
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
