using System.Runtime.Versioning;
using System.Security.Cryptography;
using Pine.Core.Security;

namespace Pine.Ui.Windows.Security;

/// <summary>
/// Windows platform key store: DPAPI, <see cref="DataProtectionScope.CurrentUser"/> (TD-87).
/// </summary>
/// <remarks>
/// The blob opens only for the same Windows user profile, and only with the same context
/// (passed as DPAPI optional entropy). DPAPI is bound to the Windows logon credential, not a
/// Hello biometric prompt; a per-unlock Hello gate is not built.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class DpapiPlatformKeyProtector : IPlatformKeyProtector
{
    public string Name => "dpapi-currentuser-v1";

    public bool IsAvailable => OperatingSystem.IsWindows();

    public byte[] Protect(byte[] secret, byte[] context) =>
        ProtectedData.Protect(secret, context, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(byte[] blob, byte[] context) =>
        ProtectedData.Unprotect(blob, context, DataProtectionScope.CurrentUser);
}
