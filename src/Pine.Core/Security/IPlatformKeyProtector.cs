namespace Pine.Core.Security;

/// <summary>
/// Seals a small secret under a key the operating system holds for the signed-in user (TD-87).
/// </summary>
/// <remarks>
/// <para>
/// THIS IS THE PLATFORM HALF OF A SECOND KEY WRAP. The vault envelope wraps the data key under the
/// Argon2id auth key; this interface lets a head wrap the same data key under something the OS
/// guards instead (DPAPI on Windows), so an unlock gated by the OS credential or biometric becomes
/// possible. It is strictly additive: the password wrap is never touched or replaced.
/// </para>
/// <para>
/// IMPLEMENTATIONS LIVE IN THE HEADS, not here, because each is a different native API. A head with
/// no implementation simply registers none, and <c>PlatformUnlock</c> then reports itself
/// unavailable. Android and iOS (Keystore, Keychain with biometric ACL) and macOS are not built.
/// </para>
/// <para>
/// FAIL CLOSED: <see cref="Unprotect"/> throws on any failure (wrong user, wrong context, tampered
/// blob). It must never return plausible-looking bytes.
/// </para>
/// </remarks>
public interface IPlatformKeyProtector
{
    /// <summary>Stable identifier recorded next to the blob, so a blob is never offered to a different protector.</summary>
    string Name { get; }

    /// <summary>True when this process can use the underlying platform store right now.</summary>
    bool IsAvailable { get; }

    /// <summary>Seals <paramref name="secret"/>, bound to <paramref name="context"/>.</summary>
    byte[] Protect(byte[] secret, byte[] context);

    /// <summary>Opens a blob made by <see cref="Protect"/> with the same context; throws otherwise.</summary>
    byte[] Unprotect(byte[] blob, byte[] context);
}
