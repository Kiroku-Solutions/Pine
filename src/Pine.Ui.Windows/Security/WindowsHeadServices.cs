using Microsoft.Extensions.DependencyInjection;
using Pine.Core.Security;

namespace Pine.Ui.Windows.Security;

/// <summary>Services only the Windows head can supply (TD-87). Kept out of Program.cs, which may not register services itself.</summary>
public static class WindowsHeadServices
{
    public static void Register(IServiceCollection services)
    {
        // DPAPI-backed opt-in unlock. The guard is for the analyser; this head is Windows-only.
        if (OperatingSystem.IsWindows())
            services.AddSingleton<IPlatformKeyProtector, DpapiPlatformKeyProtector>();
    }
}
