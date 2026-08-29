namespace Pine.Core.Security;

/// <summary>
/// Versioned, self-describing metadata for an encrypted vault: which KDF and which cost
/// parameters produced its auth key, the salt those parameters were applied to, and the vault's
/// data key wrapped under that auth key.
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
    /// <remarks>
    /// Version 2 introduced envelope encryption (TD-06): the database key became a random data key
    /// stored only as <see cref="KeyWrap"/>, instead of being the Argon2id output itself.
    /// </remarks>
    public const int CurrentVersion = 2;

    /// <summary>
    /// The oldest header schema version this build will open.
    /// </summary>
    /// <remarks>
    /// <para>
    /// EQUAL TO <see cref="CurrentVersion"/>, ON PURPOSE, AND THIS IS A BREAKING CHANGE. A version-1
    /// header describes a vault whose database key IS its Argon2id output, with no envelope at all.
    /// Supporting it would mean carrying a second key path forever - one enveloped, one not - and
    /// every later decision (password change, a keychain wrap, a rekey) would have to be correct in
    /// both. That second path is precisely where a downgrade bug would live.
    /// </para>
    /// <para>
    /// It is affordable only because it was taken while NO vault exists outside a developer machine.
    /// Once a real vault does, this constant may never rise again without a migration that
    /// re-encrypts the database; see the audit's TD row for that rekey. A version-1 header is
    /// therefore refused with its own exception rather than a generic one, so the UI can say what
    /// actually happened instead of blaming the password.
    /// </para>
    /// </remarks>
    public const int MinimumSupportedVersion = 2;

    private readonly byte[] _salt;

    /// <summary>Creates a validated header.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="kdfParameters"/>, <paramref name="salt"/> or <paramref name="keyWrap"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="headerVersion"/> is less than 1.</exception>
    /// <exception cref="ArgumentException">
    /// The salt is empty, or its length does not match the declared salt length.
    /// </exception>
    public VaultHeader(
        int headerVersion,
        Guid vaultId,
        DateTimeOffset createdAtUtc,
        KdfParameters kdfParameters,
        byte[] salt,
        VaultKeyWrap keyWrap)
    {
        ArgumentNullException.ThrowIfNull(kdfParameters);
        ArgumentNullException.ThrowIfNull(salt);
        ArgumentNullException.ThrowIfNull(keyWrap);
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
        KeyWrap = keyWrap;

        // Defensive copy on the way in: the caller must not be able to mutate a constructed header.
        _salt = (byte[])salt.Clone();
    }

    /// <summary>Schema version of this header.</summary>
    public int HeaderVersion { get; }

    /// <summary>Stable identity of the vault, preserved across header rewrites and legacy adoption.</summary>
    public Guid VaultId { get; }

    /// <summary>When the header was first created.</summary>
    public DateTimeOffset CreatedAtUtc { get; }

    /// <summary>The KDF and cost parameters that must be used to derive this vault's auth key.</summary>
    public KdfParameters KdfParameters { get; }

    /// <summary>
    /// The vault's data key, encrypted under the auth key. Non-null by construction: a header
    /// without a wrap describes a vault this build cannot open, so it must not be representable.
    /// </summary>
    public VaultKeyWrap KeyWrap { get; }

    /// <summary>
    /// The salt, returned as a fresh defensive copy on every access so callers cannot mutate the
    /// header's state. See the equality warning on the type.
    /// </summary>
    public byte[] Salt => (byte[])_salt.Clone();

    /// <summary>Length of the salt in bytes, without allocating a copy.</summary>
    public int SaltLength => _salt.Length;

    /// <summary>
    /// Returns this header with a new salt and a new wrap of the SAME data key - the shape of a
    /// password change.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="VaultId"/> and <see cref="CreatedAtUtc"/> are carried over unchanged, and that is
    /// the whole reason this lives here rather than at the call site. The vault is the same vault:
    /// minting a fresh id on a password change would break every reference to it, and the binding
    /// (see <see cref="VaultHeaderBinding"/>) would no longer identify the thing being opened.
    /// </para>
    /// <para>
    /// The salt is rotated rather than reused because it is free to do so and strictly better: a
    /// fresh salt means the old and new auth keys share no precomputation, so an adversary who had
    /// begun grinding the old password's salt gains nothing against the new one.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="newSalt"/> or <paramref name="newKeyWrap"/> is null.</exception>
    /// <exception cref="ArgumentException">The new salt's length contradicts the declared salt length.</exception>
    public VaultHeader Rewrapped(byte[] newSalt, VaultKeyWrap newKeyWrap) =>
        new(HeaderVersion, VaultId, CreatedAtUtc, KdfParameters, newSalt, newKeyWrap);
}
