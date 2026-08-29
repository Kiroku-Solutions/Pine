namespace Pine.Core.Security;

/// <summary>
/// Thrown when a vault header is well-formed but describes something this build cannot honour:
/// a newer header schema version, or an unknown KDF identifier.
/// </summary>
/// <remarks>
/// Deriving a key with guessed parameters would silently produce the wrong key, so refusing is
/// the only safe behaviour. The details are exposed as properties (not just baked into the
/// message) so callers can render an actionable "this vault needs a newer Pine" message.
/// </remarks>
public sealed class UnsupportedVaultHeaderException : Exception
{
    private UnsupportedVaultHeaderException(
        string message,
        int? foundHeaderVersion,
        int supportedHeaderVersion,
        string? foundKdfId,
        IReadOnlyList<string> supportedKdfIds)
        : base(message)
    {
        FoundHeaderVersion = foundHeaderVersion;
        SupportedHeaderVersion = supportedHeaderVersion;
        FoundKdfId = foundKdfId;
        SupportedKdfIds = supportedKdfIds;
    }

    /// <summary>The canonical list of KDF identifiers this build implements.</summary>
    public static IReadOnlyList<string> SupportedKdfIdList { get; } = new[] { KdfParameters.Argon2id };

    /// <summary>The header version found in the file, when the version is what is unsupported.</summary>
    public int? FoundHeaderVersion { get; }

    /// <summary>The highest header version this build understands.</summary>
    public int SupportedHeaderVersion { get; }

    /// <summary>The KDF identifier found in the file, when the KDF is what is unsupported.</summary>
    public string? FoundKdfId { get; }

    /// <summary>Every KDF identifier this build understands.</summary>
    public IReadOnlyList<string> SupportedKdfIds { get; }

    /// <summary>Creates an exception for a header schema version newer than this build supports.</summary>
    public static UnsupportedVaultHeaderException ForHeaderVersion(int foundVersion, int supportedVersion) =>
        new(
            $"Vault header version {foundVersion} is not supported; this build supports up to version {supportedVersion}.",
            foundHeaderVersion: foundVersion,
            supportedHeaderVersion: supportedVersion,
            foundKdfId: null,
            supportedKdfIds: SupportedKdfIdList);

    /// <summary>Creates an exception for a KDF identifier this build does not implement.</summary>
    public static UnsupportedVaultHeaderException ForKdfId(string foundKdfId) =>
        new(
            $"Key derivation function '{foundKdfId}' is not supported; supported: {string.Join(", ", SupportedKdfIdList)}.",
            foundHeaderVersion: null,
            supportedHeaderVersion: VaultHeader.CurrentVersion,
            foundKdfId: foundKdfId,
            supportedKdfIds: SupportedKdfIdList);

    /// <summary>
    /// Creates an exception for a KDF profile whose derived-key length cannot key the wrap
    /// algorithm the envelope uses.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS EXISTS BECAUSE <see cref="KdfParameters"/> DELIBERATELY DOES NOT PIN
    /// <see cref="KdfParameters.KeyLengthBytes"/> TO 32. Its bounds are wide on purpose - they are
    /// there to reject nonsense, not to encode one profile - so a well-formed header may legally
    /// declare a 16-byte auth key. AES-256-GCM cannot be keyed with one.
    /// </para>
    /// <para>
    /// It is refused UP FRONT, with its own message, rather than being allowed to surface as
    /// <c>VaultKeyUnwrapException</c>. Both would fail closed, but "could not be unwrapped" is the
    /// sentence a wrong password produces, and a misconfigured profile is not a wrong password: the
    /// user would be sent to retype a password that was never the problem, which is exactly the
    /// confusion TD-05 and TD-24 exist to eliminate.
    /// </para>
    /// </remarks>
    public static UnsupportedVaultHeaderException ForAuthKeyLength(int foundKeyLengthBytes, int requiredKeyLengthBytes) =>
        new(
            $"A {foundKeyLengthBytes}-byte auth key cannot key {VaultKeyWrapAlgorithms.Aes256Gcm}, which requires {requiredKeyLengthBytes} bytes.",
            foundHeaderVersion: null,
            supportedHeaderVersion: VaultHeader.CurrentVersion,
            foundKdfId: null,
            supportedKdfIds: SupportedKdfIdList);
}

/// <summary>
/// Thrown when a vault header exists but cannot be trusted: unreadable JSON, wrong or missing
/// magic marker, missing required fields, invalid Base64, or a salt whose length contradicts the
/// declared salt length. Never recovered from by falling back to default parameters.
/// </summary>
public sealed class VaultHeaderCorruptException : Exception
{
    /// <summary>Creates the exception for the given header file.</summary>
    public VaultHeaderCorruptException(string filePath, string message)
        : base(message)
    {
        FilePath = filePath;
    }

    /// <summary>Creates the exception for the given header file, preserving the underlying failure.</summary>
    public VaultHeaderCorruptException(string filePath, string message, Exception innerException)
        : base(message, innerException)
    {
        FilePath = filePath;
    }

    /// <summary>Full path of the header file that could not be trusted.</summary>
    public string FilePath { get; }
}

/// <summary>Thrown when a vault directory contains neither a versioned header nor a legacy salt file.</summary>
public sealed class VaultHeaderNotFoundException : Exception
{
    /// <summary>Creates the exception for the given vault directory.</summary>
    public VaultHeaderNotFoundException(string vaultDirectory)
        : base($"No vault header was found in '{vaultDirectory}'.")
    {
        VaultDirectory = vaultDirectory;
    }

    /// <summary>The directory that was searched.</summary>
    public string VaultDirectory { get; }
}
