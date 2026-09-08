using System.Threading.Tasks;
using Avalonia;
using Avalonia.Browser;
using Pine.Ui.Avalonia;
using Pine.Ui.Avalonia.Composition;

namespace Pine.Ui.Web;

internal sealed partial class Program
{
    private static async Task Main(string[] args)
    {
        // Build the container (TD-29). This head's own copy of the registrations claimed in a
        // comment that it used an in-memory database for WebAssembly and then constructed a real
        // SQLCipher context against ":memory:" anyway -- a false comment over a path that cannot
        // work, which is TD-53. The shared root builds the same context every head builds, from the
        // real vault directory. That does not make this head work: AesGcm.IsSupported is false on
        // WASM, so the vault cannot be unwrapped here at all (TD-88), and the vault layer says so
        // by name. It does mean this head no longer describes a fallback that was never written.
        var serviceProvider = PineCompositionRoot.Build(PineHeadProfile.Browser);

        // Hand the UI what it needs, by name (TD-28) -- a typed record, not the container.
        PineCompositionRoot.InitializeUi(serviceProvider);

        // Start the idle auto-lock watchdog (TD-11). No generic host here either.
        PineCompositionRoot.Start(serviceProvider);

        await BuildAvaloniaApp()
            .WithInterFont()
            .StartBrowserAppAsync("out");
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>();
}
