namespace Pine.Core.Security;

/// <summary>
/// Seals and opens a standalone file under the open vault's data key (TD-16).
/// </summary>
/// <remarks>
/// <para>
/// WHAT THIS IS FOR. SQLCipher encrypts <c>pine.db</c>, and until TD-16 that was the entire
/// encryption boundary -- so anything Pine derived from the user's data and stored outside the
/// database was stored in the clear. The ML categoriser's model was exactly that: trained on real
/// payees and real amounts, written next to the executable, shared by every vault. This port is the
/// boundary that file now crosses, and it is deliberately general rather than model-specific
/// because it is not the last such file. Phase 3's encrypted receipt attachments need the same
/// thing.
/// </para>
/// <para>
/// PURPOSE SEPARATION IS PART OF THE CONTRACT, NOT AN IMPLEMENTATION DETAIL. Every call names a
/// <c>purpose</c>, and a blob sealed under one purpose MUST NOT open under another. An
/// implementation is expected to achieve that by deriving a distinct key per purpose, so that the
/// key protecting the categoriser's model is not the key protecting anything else and is not the
/// database key either. The consequence worth wanting: compromising one blob's key does not hand
/// over the vault.
/// </para>
/// <para>
/// AEAD, FOR THE SAME REASON THE KEY WRAP USES IT. The output is authenticated, so tampering is
/// detected on open rather than surfacing as a corrupt payload three layers up. Callers get an
/// exception, not a plausible-looking wrong answer.
/// </para>
/// <para>
/// THIS IS NOT A STREAMING API, AND THAT IS A LIMIT. Both directions take and return whole arrays,
/// so a sealed file is held in memory twice while it is being opened. That is acceptable for the
/// artefacts this currently protects -- an ML.NET categoriser model is small -- and it is the wrong
/// shape for a large attachment. Say so before reusing it for one.
/// </para>
/// </remarks>
public interface IVaultFileCipher
{
    /// <summary>Seals <paramref name="plaintext"/> under the open vault's key and the given purpose.</summary>
    /// <param name="plaintext">The bytes to protect. May be empty.</param>
    /// <param name="purpose">
    /// A stable, non-empty label identifying what this blob is for -- for example
    /// <c>ai.categorizer.model</c>. It selects the key and is bound into the authentication tag, so
    /// changing it after data has been written makes that data unreadable.
    /// </param>
    /// <returns>A self-describing blob: magic, nonce, tag and ciphertext. Never the plaintext.</returns>
    /// <exception cref="ArgumentException"><paramref name="purpose"/> is null or whitespace.</exception>
    /// <exception cref="VaultLockedException">No vault is open, so there is no key to seal under.</exception>
    /// <exception cref="VaultKeyWrapUnavailableException">The platform does not provide the AEAD.</exception>
    byte[] Encrypt(ReadOnlySpan<byte> plaintext, string purpose);

    /// <summary>Opens a blob produced by <see cref="Encrypt"/> under the same purpose.</summary>
    /// <param name="sealedBlob">The blob as it was stored.</param>
    /// <param name="purpose">The same label the blob was sealed under.</param>
    /// <returns>The original plaintext.</returns>
    /// <exception cref="ArgumentException"><paramref name="purpose"/> is null or whitespace.</exception>
    /// <exception cref="VaultLockedException">No vault is open.</exception>
    /// <exception cref="VaultKeyWrapUnavailableException">The platform does not provide the AEAD.</exception>
    /// <exception cref="VaultFileCipherException">
    /// The blob is malformed, was sealed for a different purpose, was sealed by a different vault,
    /// or has been tampered with. These are ONE exception on purpose: distinguishing them would
    /// tell an attacker which of their guesses was closer.
    /// </exception>
    byte[] Decrypt(ReadOnlySpan<byte> sealedBlob, string purpose);
}

/// <summary>Identifiers naming what a sealed vault file holds.</summary>
/// <remarks>
/// Constants rather than free strings because a purpose is effectively part of the on-disk format:
/// mistyping one at the write site and again at the read site would work, mistyping it at only one
/// would silently make existing data unreadable.
/// </remarks>
public static class VaultFilePurposes
{
    /// <summary>The serialised ML.NET transaction-categorisation model (TD-16).</summary>
    public const string CategorizerModel = "ai.categorizer.model";

    /// <summary>
    /// The user's sync key (post-audit plan item 3.1d; `14-Synchronization` §5): the 32 bytes every envelope is
    /// sealed under, kept beside the vault only as this purpose's ciphertext.
    /// </summary>
    public const string SyncKey = "sync.key";

    /// <summary>
    /// The user's local receipt-layout field adaptation model trained from interactive bounding-box tags.
    /// </summary>
    public const string ReceiptLayoutModel = "ai.receipt.layout.model";
}
