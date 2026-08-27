namespace Pine.Core.Security;

/// <summary>
/// Versioned, self-describing metadata for an encrypted vault: which KDF and which cost
/// parameters produced its encryption key, plus the salt those parameters were applied to.
/// </summary>
/// <remarks>
/// <para>
/// EQUALITY WARNING: this is a record, and record equality compares the underlying fields. The
/// salt is stored as a <see cref="byte"/> array, so two headers holding identical salt BYTES in
/// different arrays are NOT equal. Never use <c>==</c> or <see cref="object.Equals(object)"/> to
/// compare salts - compare the bytes explicitly (e.g. <c>a.Salt.SequenceEqual(b.Salt)</c>).
/// </para>
/// </remarks>
public sealed record VaultHeader
{
    /// <summary>Magic marker written into the persisted header so a foreign file is rejected outright.</summary>
    public const string Magic = "PINE-VAULT";

    /// <summary>The header schema version this build writes and is able to read.</summary>
    public const int CurrentVersion = 1;

    private readonly byte[] _salt;

    /// <summary>Creates a validated header.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="kdfParameters"/> or <paramref name="salt"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="headerVersion"/> is less than 1.</exception>
    /// <exception cref="ArgumentException">
    /// The salt is empty, or its length does not match the declared salt length.
    /// </exception>
    public VaultHeader(
        int headerVersion,
        Guid vaultId,
        DateTimeOffset createdAtUtc,
        KdfParameters kdfParameters,
        byte[] salt)
    {
        ArgumentNullException.ThrowIfNull(kdfParameters);
        ArgumentNullException.ThrowIfNull(salt);
        ArgumentOutOfRangeException.ThrowIfLessThan(headerVersion, 1);

        if (salt.Length == 0)
        {
            throw new ArgumentException("Salt cannot be empty.", nameof(salt));
        }

        if (salt.Length != kdfParameters.SaltLengthBytes)
        {
            throw new ArgumentException(
                $"Salt length {salt.Length} does not match the declared salt length {kdfParameters.SaltLengthBytes}.",
                nameof(salt));
        }

        HeaderVersion = headerVersion;
        VaultId = vaultId;
        CreatedAtUtc = createdAtUtc;
        KdfParameters = kdfParameters;

        // Defensive copy on the way in: the caller must not be able to mutate a constructed header.
        _salt = (byte[])salt.Clone();
    }

    /// <summary>Schema version of this header.</summary>
    public int HeaderVersion { get; }

    /// <summary>Stable identity of the vault, preserved across header rewrites and legacy adoption.</summary>
    public Guid VaultId { get; }

    /// <summary>When the header was first created.</summary>
    public DateTimeOffset CreatedAtUtc { get; }

    /// <summary>The KDF and cost parameters that must be used to derive this vault's key.</summary>
    public KdfParameters KdfParameters { get; }

    /// <summary>
    /// The salt, returned as a fresh defensive copy on every access so callers cannot mutate the
    /// header's state. See the equality warning on the type.
    /// </summary>
    public byte[] Salt => (byte[])_salt.Clone();

    /// <summary>Length of the salt in bytes, without allocating a copy.</summary>
    public int SaltLength => _salt.Length;
}
