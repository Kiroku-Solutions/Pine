using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Headless;

namespace Pine.Tests.Unit;

/// <summary>
/// Registers a headless Skia rendering platform once, before any test runs.
/// </summary>
/// <remarks>
/// Without this, any test that reaches <c>new Avalonia.Media.Imaging.Bitmap(stream)</c> -- as
/// <c>ScanReceiptViewModel.ProcessReceiptImageAsync</c> does on its very first line -- fails with
/// "Unable to locate 'Avalonia.Platform.IPlatformRenderInterface'". Pine.Ui.Avalonia is a shared
/// class library and deliberately never calls <c>AppBuilder.Configure</c> itself; that call lives
/// only in the OS-specific heads (Pine.Ui.Windows, Pine.Ui.Macos), which a unit test project does
/// not reference. <see cref="ModuleInitializerAttribute"/> runs this exactly once, as the test
/// assembly loads, so every test gets a working (headless, off-screen) Skia backend without any
/// test having to know it is needed.
/// </remarks>
internal static class TestAppInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        AppBuilder.Configure<Avalonia.Application>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions())
            .SetupWithoutStarting();
    }
}
