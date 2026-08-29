namespace Pine.Core.Security;

/// <summary>
/// The vault's data key, encrypted under the auth key that Argon2id derives from the master
/// password. This is the "envelope" of envelope encryption (TD-06): the ciphertext here is the
/// ONLY form in which the data key ever touches disk.
/// </summary>
/// <remarks>
/// <para>
/// WHY AN ENVELOPE AT ALL. Before this type existed, the Argon2id output WAS the database key, so
/// the password and the data were welded together: changing the password meant re-encrypting every
/// byte of the vault, and unlocking by any means other than typing that password was
/// architecturally impossible. With the data key wrapped, the password protects a 32-byte blob
/// instead of a database. Changing it rewrites one small file; adding a second way to unlock the
/// same data key (a platform keychain, a biometric prompt) becomes an additional wrap rather than
/// a redesign.
/// </para>
/// <para>
/// AEAD, NOT PLAIN ENCRYPTION, AND THAT IS THE POINT. The wrap is authenticated
/// (<see cref="VaultKeyWrapAlgorithms.Aes256Gcm"/>), so a wrong password is detected HERE, by a tag
/// mismatch, rather than three layers down as an unhelpful "file is not a database" from SQLCipher.
/// The associated data (see <see cref="VaultHeaderBinding"/>) binds the wrap to the rest of its
/// header, so an attacker cannot splice one vault's wrap onto another vault's header, nor edit any
/// header field and have the result still open.
/// </para>
/// <para>
/// EQUALITY WARNING: as with <see cref="VaultHeader"/>, this is a record holding arrays, so record
/// equality compares references and not contents. Never compare two wraps with <c>==</c>.
/// </para>
/// </remarks>
public sealed record VaultKeyWrap
{
    /// <summary>Nonce length required by AES-256-GCM, in bytes. The 96-bit nonce is the only size NIST SP 800-38D recommends.</summary>
    public const int Aes256GcmNonceLengthBytes = 12;

    /// <summary>Authentication tag length required by AES-256-GCM, in bytes. Full-length tags only; a truncated tag is a weakened tag.</summary>
    public const int Aes256GcmTagLengthBytes = 16;

    private readonly byte[] _nonce;
    private readonly byte[] _ciphertext;
    private readonly byte[] _tag;

    /// <summary>Creates a validated wrap.</summary>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    /// <exception cref="ArgumentException">
    /// The algorithm is unknown, or the nonce, ciphertext or tag has a length the algorithm cannot produce.
    /// </exception>
    public VaultKeyWrap(string algorithm, byte[] nonce, byte[] ciphertext, byte[] tag)
    {
        ArgumentNullException.ThrowIfNull(algorithm);
        ArgumentNullException.ThrowIfNull(nonce);
        ArgumentNullException.ThrowIfNull(ciphertext);
        ArgumentNullException.ThrowIfNull(tag);

        // Rejecting an unknown algorithm at CONSTRUCTION, not at decryption, is deliberate: it
        // means no code path anywhere can hold a VaultKeyWrap whose algorithm nobody implements.
        if (!string.Equals(algorithm, VaultKeyWrapAlgorithms.Aes256Gcm, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Unsupported key-wrap algorithm '{algorithm}'; supported: {VaultKeyWrapAlgorithms.Aes256Gcm}.",
                nameof(algorithm));
        }

        if (nonce.Length != Aes256GcmNonceLengthBytes)
        {
            throw new ArgumentException(
                $"An AES-256-GCM nonce must be exactly {Aes256GcmNonceLengthBytes} bytes; got {nonce.Length}.",
                nameof(nonce));
        }

        if (tag.Length != Aes256GcmTagLengthBytes)
        {
            throw new ArgumentException(
                $"An AES-256-GCM tag must be exactly {Aes256GcmTagLengthBytes} bytes; got {tag.Length}.",
                nameof(tag));
        }

        if (ciphertext.Length == 0)
        {
            throw new ArgumentException("The wrapped data key cannot be empty.", nameof(ciphertext));
        }

        Algorithm = algorithm;

        // Defensive copies in and out, for the same reason VaultHeader takes them for the salt:
        // a constructed wrap is a fact about a vault, and a caller must not be able to edit it
        // after the fact.
        _nonce = (byte[])nonce.Clone();
        _ciphertext = (byte[])ciphertext.Clone();
        _tag = (byte[])tag.Clone();
    }

    /// <summary>Identifier of the AEAD used to wrap the data key.</summary>
    public string Algorithm { get; }

    /// <summary>The AEAD nonce, as a fresh copy. Unique per wrap: a reused nonce under the same key breaks GCM catastrophically.</summary>
    public byte[] Nonce => (byte[])_nonce.Clone();

    /// <summary>The encrypted data key, as a fresh copy.</summary>
    public byte[] Ciphertext => (byte[])_ciphertext.Clone();

    /// <summary>The authentication tag, as a fresh copy.</summary>
    public byte[] Tag => (byte[])_tag.Clone();

    /// <summary>Length of the wrapped data key in bytes, without allocating a copy.</summary>
    public int CiphertextLength => _ciphertext.Length;
}

/// <summary>Identifiers of the AEADs Pine understands for wrapping a data key.</summary>
public static class VaultKeyWrapAlgorithms
{
    /// <summary>
    /// AES-256 in Galois/Counter Mode - the primitive `15-Security` §4 names for encryption at
    /// rest, used here for the key wrap itself.
    /// </summary>
    public const string Aes256Gcm = "AES-256-GCM";
}
