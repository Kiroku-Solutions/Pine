namespace Pine.Core.Security;

/// <summary>
/// Thrown when a vault's data key could not be unwrapped: the password is wrong, or the header has
/// been altered since the wrap was made.
/// </summary>
/// <remarks>
/// <para>
/// IT CARRIES NOTHING, AND THAT IS THE FEATURE. There is deliberately no property distinguishing
/// "wrong password" from "tampered header", and no inner exception is preserved: both are a failed
/// authentication tag, and telling the two apart would hand an attacker an oracle for exactly the
/// question they want answered. TD-23's rule - a closed status set carrying no exception text -
/// applies here more sharply than anywhere else in the codebase.
/// </para>
/// <para>
/// The unlock screen may still report this differently from a corrupt header or a failed migration,
/// because those are distinct EXCEPTION TYPES thrown before any password is tested.
/// </para>
/// </remarks>
public sealed class VaultKeyUnwrapException : Exception
{
    /// <summary>Creates the exception with its single, fixed message.</summary>
    public VaultKeyUnwrapException()
        : base("The vault's data key could not be unwrapped.")
    {
    }
}

/// <summary>
/// Thrown when the running platform does not provide the AEAD a vault's key wrap requires.
/// </summary>
/// <remarks>
/// Not a corruption and not an authentication failure: the header is perfectly valid and some other
/// build could open it. <c>AesGcm.IsSupported</c> is false on the browser/WASM runtime in
/// particular — the head that ran there was deleted in the Phase 3 platform review, and this type
/// deliberately outlives it, because it describes the PRIMITIVE and not the head — which is why
/// this is a named type rather than a bare
/// <see cref="PlatformNotSupportedException"/> - the UI has to say "this build cannot open vaults"
/// and not "wrong password".
/// </remarks>
public sealed class VaultKeyWrapUnavailableException : Exception
{
    /// <summary>Creates the exception for the algorithm the platform does not provide.</summary>
    public VaultKeyWrapUnavailableException(string algorithm)
        : base($"This platform does not provide '{algorithm}', which this vault's key wrap requires.")
    {
        Algorithm = algorithm;
    }

    /// <summary>The algorithm the header asked for.</summary>
    public string Algorithm { get; }
}

/// <summary>
/// Thrown when a vault directory holds a pre-envelope vault: a version-1 header, or the raw
/// <c>pine.salt</c> file that predates any header at all.
/// </summary>
/// <remarks>
/// <para>
/// These vaults have no wrapped data key - their database key IS the Argon2id output - so this
/// build cannot open them. Supporting them would mean keeping a second, un-enveloped key path alive
/// forever; see <see cref="VaultHeader.MinimumSupportedVersion"/> for why that trade was refused
/// while no vault exists outside a developer machine.
/// </para>
/// <para>
/// It is a distinct type so the unlock screen can say what actually happened. Reported as a corrupt
/// header it would send the user hunting for a backup; reported as a bad password it would send
/// them to reset a password that was never wrong.
/// </para>
/// </remarks>
public sealed class LegacyVaultFormatException : Exception
{
    /// <summary>Creates the exception for a vault directory holding a pre-envelope vault.</summary>
    public LegacyVaultFormatException(string vaultDirectory, int? foundHeaderVersion)
        : base(
            foundHeaderVersion is { } version
                ? $"The vault in '{vaultDirectory}' uses header version {version}, which predates envelope encryption; this build opens version {VaultHeader.MinimumSupportedVersion} and above."
                : $"The vault in '{vaultDirectory}' predates the versioned header entirely.")
    {
        VaultDirectory = vaultDirectory;
        FoundHeaderVersion = foundHeaderVersion;
    }

    /// <summary>The directory holding the pre-envelope vault.</summary>
    public string VaultDirectory { get; }

    /// <summary>The header version found, or <see langword="null"/> when only a legacy salt file was present.</summary>
    public int? FoundHeaderVersion { get; }
}
