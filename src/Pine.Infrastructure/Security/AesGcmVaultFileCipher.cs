using System;
using System.Security.Cryptography;
using System.Text;
using Pine.Core.Security;

namespace Pine.Infrastructure.Security;

/// <summary>
/// Seals standalone vault files with AES-256-GCM under a purpose-separated key derived from the
/// open vault's data key (TD-16).
/// </summary>
/// <remarks>
/// <para>
/// THIS IS THE SAME PRIMITIVE THE KEY WRAP USES, AND NOT THE SAME KEY. <c>AesGcmVaultKeyService</c>
/// wraps the data key with AES-256-GCM under an Argon2id-derived auth key. This class encrypts
/// FILES with AES-256-GCM under a key derived from the data key by HKDF. Nothing here ever sees a
/// password, and nothing here ever encrypts under the database key itself.
/// </para>
/// <para>
/// WHY HKDF RATHER THAN USING THE DATA KEY DIRECTLY. Using one key for two purposes is how nonce
/// spaces collide. SQLCipher manages its own IVs under the data key across every page of the
/// database; this class would manage a separate 96-bit nonce space under the same key. Nothing
/// forces those to overlap today, but nothing prevents it either, and a GCM nonce reused under one
/// key is a catastrophic failure rather than a degraded one -- it leaks the authentication subkey,
/// not just one plaintext. Deriving a distinct key per purpose costs one hash and removes the
/// question. It also gives purpose separation for free: <c>ai.categorizer.model</c> and a future
/// <c>attachments.receipt</c> are encrypted under keys that cannot open each other's files.
/// </para>
/// <para>
/// BLOB LAYOUT. <c>PINEFC1\0</c> (8 bytes) || nonce (12) || tag (16) || ciphertext. The version
/// lives in the magic's seventh character, so a future format change is detected by the same
/// comparison that rejects a foreign file, rather than needing a separate field that a v1 reader
/// would not know to look at.
/// </para>
/// <para>
/// ASSOCIATED DATA. The magic and the length-prefixed purpose, following the rule
/// <c>VaultHeaderBinding</c> established: bind everything that is available, as a rule, rather than
/// deciding field by field which parts are "security relevant". Its practical effect here is that
/// renaming a sealed file cannot repurpose it -- the purpose is authenticated, not merely used to
/// pick a key.
/// </para>
/// <para>
/// WHAT IS NOT BOUND: the vault id. Binding it would mean reading <c>pine.vault.json</c> on every
/// call, and it would buy nothing -- the HKDF input is already that vault's own data key, so a blob
/// carried to another vault fails at the tag regardless.
/// </para>
/// </remarks>
public sealed class AesGcmVaultFileCipher : IVaultFileCipher
{
    /// <summary>Magic and format version. Changing the trailing digit is a format break by design.</summary>
    private static readonly byte[] Magic = "PINEFC1\0"u8.ToArray();

    /// <summary>Length of the derived per-purpose file key, in bytes. AES-256 takes 32.</summary>
    private const int FileKeyLengthBytes = 32;

    /// <summary>
    /// HKDF info prefix. Versioned independently of the blob magic: the magic describes the file's
    /// SHAPE, this describes how its key was DERIVED, and the two can move for different reasons.
    /// </summary>
    private const string KeyDerivationInfoPrefix = "pine:file-cipher:v1:";

    private const int NonceOffset = 8;
    private const int TagOffset = NonceOffset + VaultKeyWrap.Aes256GcmNonceLengthBytes;
    private const int CiphertextOffset = TagOffset + VaultKeyWrap.Aes256GcmTagLengthBytes;

    private readonly IVaultDataKeyAccessor _vault;

    /// <summary>Creates the cipher over the process's vault session.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="vault"/> is null.</exception>
    public AesGcmVaultFileCipher(IVaultDataKeyAccessor vault)
    {
        ArgumentNullException.ThrowIfNull(vault);
        _vault = vault;
    }

    /// <inheritdoc />
    public byte[] Encrypt(ReadOnlySpan<byte> plaintext, string purpose)
    {
        EnsurePurpose(purpose);
        EnsureAvailable();

        var fileKey = DeriveFileKey(purpose);
        try
        {
            var blob = new byte[CiphertextOffset + plaintext.Length];
            Magic.CopyTo(blob.AsSpan(0, Magic.Length));

            var nonce = blob.AsSpan(NonceOffset, VaultKeyWrap.Aes256GcmNonceLengthBytes);
            // A FRESH nonce per call, from the CSPRNG, never a counter. A counter would have to be
            // persisted alongside the file to stay unique across restarts, and a counter that
            // resets is precisely the reuse this whole class is arranged to avoid.
            RandomNumberGenerator.Fill(nonce);

            var tag = blob.AsSpan(TagOffset, VaultKeyWrap.Aes256GcmTagLengthBytes);
            var ciphertext = blob.AsSpan(CiphertextOffset);

            using var aesGcm = new AesGcm(fileKey, VaultKeyWrap.Aes256GcmTagLengthBytes);
            aesGcm.Encrypt(nonce, plaintext, ciphertext, tag, BuildAssociatedData(purpose));

            return blob;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(fileKey);
        }
    }

    /// <inheritdoc />
    public byte[] Decrypt(ReadOnlySpan<byte> sealedBlob, string purpose)
    {
        EnsurePurpose(purpose);
        EnsureAvailable();

        // Length and magic are checked BEFORE the key is derived. There is no security reason for
        // the order -- both failures raise the same exception -- but there is no reason to derive a
        // key for a file that is obviously not ours either.
        if (sealedBlob.Length < CiphertextOffset ||
            !sealedBlob[..Magic.Length].SequenceEqual(Magic))
        {
            throw new VaultFileCipherException(purpose);
        }

        var fileKey = DeriveFileKey(purpose);
        try
        {
            var plaintext = new byte[sealedBlob.Length - CiphertextOffset];
            using var aesGcm = new AesGcm(fileKey, VaultKeyWrap.Aes256GcmTagLengthBytes);

            try
            {
                aesGcm.Decrypt(
                    sealedBlob.Slice(NonceOffset, VaultKeyWrap.Aes256GcmNonceLengthBytes),
                    sealedBlob[CiphertextOffset..],
                    sealedBlob.Slice(TagOffset, VaultKeyWrap.Aes256GcmTagLengthBytes),
                    plaintext,
                    BuildAssociatedData(purpose));
            }
            catch (CryptographicException)
            {
                // The partially written plaintext buffer is zeroed before it is abandoned. AesGcm
                // is documented to clear it on tag failure, but a buffer holding a failed
                // decryption is not something to leave to another type's documented behaviour.
                CryptographicOperations.ZeroMemory(plaintext);
                throw new VaultFileCipherException(purpose);
            }

            return plaintext;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(fileKey);
        }
    }

    /// <summary>
    /// Derives this purpose's file key from the vault's data key with HKDF-SHA256.
    /// </summary>
    /// <remarks>
    /// No salt. HKDF's salt defends against a low-entropy input keying material, and the input here
    /// is a 32-byte CSPRNG data key -- it has nothing to strengthen. A per-file random salt would
    /// also have to be stored in the blob and would then have to be authenticated, which is more
    /// format for no gain. The purpose goes in <c>info</c>, which is what <c>info</c> is for.
    /// </remarks>
    private byte[] DeriveFileKey(string purpose)
    {
        var dataKey = _vault.Key;
        if (dataKey is null || dataKey.Length == 0)
        {
            // Re-checked after EnsureAvailable because the vault can lock between the two: the
            // session's lock runs on a timer thread. Failing here is the correct outcome, and it
            // fails closed.
            throw new VaultLockedException("The vault is locked, so no file key can be derived.");
        }

        return HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            ikm: dataKey,
            outputLength: FileKeyLengthBytes,
            salt: null,
            info: Encoding.UTF8.GetBytes(KeyDerivationInfoPrefix + purpose));
    }

    /// <summary>
    /// Builds the AEAD associated data: the magic, then the purpose length-prefixed.
    /// </summary>
    /// <remarks>
    /// LENGTH-PREFIXED FOR THE REASON <c>VaultHeaderBinding</c> GIVES: without it, concatenation is
    /// ambiguous and two different purposes could in principle produce identical associated data.
    /// There is only one purpose in the encoding today, so the ambiguity is not reachable -- the
    /// prefix is here so that it stays unreachable when a second field is added.
    /// </remarks>
    private static byte[] BuildAssociatedData(string purpose)
    {
        var purposeBytes = Encoding.UTF8.GetBytes(purpose);
        var associatedData = new byte[Magic.Length + sizeof(int) + purposeBytes.Length];

        Magic.CopyTo(associatedData.AsSpan(0, Magic.Length));
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(
            associatedData.AsSpan(Magic.Length, sizeof(int)), purposeBytes.Length);
        purposeBytes.CopyTo(associatedData.AsSpan(Magic.Length + sizeof(int)));

        return associatedData;
    }

    private void EnsureAvailable()
    {
        if (!_vault.IsUnlocked)
        {
            throw new VaultLockedException("The vault is locked, so vault files can be neither sealed nor opened.");
        }

        if (!AesGcm.IsSupported)
        {
            // No head reaches this today: the Browser head did (TD-88) and was deleted in the
            // Phase 3 platform review. The check stays because it is a claim about the PRIMITIVE,
            // not about that head -- AesGcm.IsSupported is a runtime question on any target, and a
            // future head is exactly the situation where the answer matters. Reporting it as "this
            // platform has no AES-GCM" rather than as a corrupt file is the difference between a
            // diagnosable build and a mystery.
            throw new VaultKeyWrapUnavailableException(VaultKeyWrapAlgorithms.Aes256Gcm);
        }
    }

    private static void EnsurePurpose(string purpose)
    {
        if (string.IsNullOrWhiteSpace(purpose))
        {
            throw new ArgumentException("A vault file purpose is required.", nameof(purpose));
        }
    }
}
