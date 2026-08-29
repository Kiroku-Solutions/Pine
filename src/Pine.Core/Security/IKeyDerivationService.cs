namespace Pine.Core.Security;

/// <summary>
/// Port for deriving a vault's AUTH key - the key-encryption key - from the master password.
/// </summary>
/// <remarks>
/// <para>
/// WHAT THIS DERIVES CHANGED WITH TD-06, THOUGH NOT ONE BYTE OF HOW. Before envelope encryption the
/// output of this call WAS the database key. It is now the key that unwraps
/// <see cref="VaultHeader.KeyWrap"/>, and the database key is the random data key inside that wrap.
/// The derivation itself is untouched - the pre-TD-06 known-answer vector still reproduces exactly -
/// because changing the KDF and adding the envelope in one step would have left no way to tell
/// which of the two broke a vault.
/// </para>
/// The parameters MUST come from <see cref="VaultHeader.KdfParameters"/> and never from
/// compile-time constants. That is the whole point of the versioned header: the cost parameters
/// can evolve for new vaults without making a single existing vault unopenable.
/// </remarks>
public interface IKeyDerivationService
{
    /// <summary>Derives the vault's auth key using exactly the parameters recorded in the header.</summary>
    /// <remarks>
    /// Defined as delegating to <see cref="DeriveKey(string, KdfParameters, byte[])"/> with the
    /// header's own <see cref="VaultHeader.KdfParameters"/> and <see cref="VaultHeader.Salt"/>. An
    /// implementation must not carry a second derivation body for this overload: two bodies is two
    /// chances to disagree, and the only symptom of disagreement is a vault that no longer opens.
    /// </remarks>
    /// <exception cref="ArgumentException">The password is null or empty.</exception>
    /// <exception cref="ArgumentNullException">The header is null.</exception>
    /// <exception cref="UnsupportedVaultHeaderException">
    /// The header names a KDF this implementation does not provide.
    /// </exception>
    byte[] DeriveKey(string password, VaultHeader header);

    /// <summary>
    /// Derives an auth key from a password, a KDF profile and a salt, with no header involved.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS OVERLOAD EXISTS BECAUSE OF AN ORDERING CONSTRAINT THAT ENVELOPE ENCRYPTION CREATED, not
    /// as a convenience. A <see cref="VaultHeader"/> cannot be constructed without its
    /// <see cref="VaultKeyWrap"/>; the wrap cannot be produced without the auth key; and before
    /// TD-06 the only way to ask for an auth key was to hand over a header. Creating a vault was
    /// therefore circular. Taking the parameters and salt directly breaks the cycle at the only
    /// point where it is safe to break it - the values are the same values the header is about to
    /// record, so the key derived here is bit-for-bit the key the header overload would later derive.
    /// </para>
    /// <para>
    /// USE THE HEADER OVERLOAD ANYWHERE A HEADER ALREADY EXISTS. Passing loose parameters is exactly
    /// the mistake <see cref="KdfParameters"/> was written to prevent: parameters that did not come
    /// from the vault's own header derive a different key, and the failure surfaces far away as an
    /// unopenable vault. This overload is for vault creation and for the re-wrap half of a password
    /// change, where the salt being derived against is one this call's caller just generated.
    /// </para>
    /// </remarks>
    /// <param name="password">The master password. The caller keeps ownership of the string.</param>
    /// <param name="parameters">The KDF profile to derive under.</param>
    /// <param name="salt">The salt to derive against. Its length must match the profile's declared salt length.</param>
    /// <exception cref="ArgumentException">
    /// The password is null or empty, the salt is empty, or the salt's length contradicts
    /// <see cref="KdfParameters.SaltLengthBytes"/>.
    /// </exception>
    /// <exception cref="ArgumentNullException"><paramref name="parameters"/> or <paramref name="salt"/> is null.</exception>
    /// <exception cref="UnsupportedVaultHeaderException">
    /// <paramref name="parameters"/> names a KDF this implementation does not provide.
    /// </exception>
    byte[] DeriveKey(string password, KdfParameters parameters, byte[] salt);
}
