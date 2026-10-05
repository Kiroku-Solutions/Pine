using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Pine.Core.Security;
using Pine.Core.Sync;

namespace Pine.Infrastructure.Sync;

/// <summary>
/// A pairing offer: the user's <see cref="SyncKey"/> wrapped under a key derived from a one-time code, for a short
/// while, so a second vault can be given it over a channel the user confirms (post-audit plan item 3.1e; TD-154).
/// </summary>
/// <remarks>
/// <para>
/// LAYOUT, VERSION 1. Little-endian throughout; offsets are part of the format, and the whole thing is exactly
/// <see cref="Length"/> bytes.
/// </para>
/// <code>
///   0   8  magic "PINEPAIR"
///   8   2  format version (1)
///  10  16  offering vault id  (Guid.TryWriteBytes layout)
///  26  16  offer id           (random per offer; two offers are never the same offer)
///  42  16  key id             (SyncKey.KeyId — which key this offer carries)
///  58   8  created at         (Unix milliseconds, UTC)
///  66   8  expires at         (Unix milliseconds, UTC)
///  74  32  salt               (random per offer; the HKDF salt for the wrapping key)
/// 106  12  nonce              (random per offer)
/// 118  16  AES-GCM tag
/// 134  32  ciphertext of the sync key's 32 bytes
/// </code>
/// <para>
/// THE WRAPPING KEY IS <c>HKDF-SHA256(ikm = the code's 25 canonical characters as ASCII, salt = the offer's salt,
/// info = "pine:sync-pairing:v1")</c>, and the whole 118-byte header is the AEAD's associated data — the same two
/// rules <see cref="SyncEnvelope"/> follows, for the same reasons: no two offers share a GCM key, and no header
/// field can be edited or moved onto another offer's body.
/// </para>
/// <para>
/// HKDF AND NOT A PASSWORD KDF, AND THE ARGUMENT IS THE CODE'S ENTROPY. Argon2id exists in this codebase and is
/// what a <em>password</em> gets, because a password has perhaps thirty bits and the cost function is the only
/// thing between it and a wordlist. A pairing code has 125 (<see cref="SyncPairingCode"/>), so stretching it buys
/// a factor no attacker will ever notice against a search space nobody can enter. The dependency runs the other
/// way: this construction is sound ONLY while the code stays that long, which is why the length is a constant with
/// a test on it rather than a parameter.
/// </para>
/// <para>
/// THE KEY NEVER LEAVES MEMORY IN PLAINTEXT. What is written is the ciphertext of the 32 bytes; the plaintext
/// exists between <see cref="SyncKey.CopyMaterialTo"/> and <c>AesGcm.Encrypt</c>, in a buffer this class zeroes.
/// </para>
/// <para>
/// REFUSALS ARE ORDERED so that every reason after <see cref="SyncPairingRefusal.NotAuthentic"/> is about a header
/// that has been authenticated: shape and version, then the code's form, then the tag, then the expiry, the
/// offering vault and the payload. Whether the accepting vault may keep the key at all — an offer used here
/// already, or a vault holding another key — is <see cref="VaultSyncPairing"/>'s, because it needs the vault.
/// </para>
/// <para>
/// WHAT THIS DOES NOT DEFEND AGAINST, stated rather than glossed: someone who obtains BOTH the offer file and the
/// code inside the offer's lifetime gets the key. That is the channel the user confirms — a code read aloud to a
/// person in the room — and the expiry is what bounds it. There is no device authentication here either; pairing
/// hands over a key, and the key is still what an envelope's tag proves (TD-154).
/// </para>
/// </remarks>
public static class SyncPairingOffer
{
    /// <summary>The offer format's name and major version.</summary>
    public const string FormatName = "pine.sync-pairing-offer.v1";

    /// <summary>The format version this build writes and reads.</summary>
    public const ushort CurrentVersion = 1;

    /// <summary>Length of the header, which is the associated data: everything before the tag.</summary>
    public const int HeaderLength = 118;

    /// <summary>Length of a whole offer. It is fixed: an offer carries one key and nothing else.</summary>
    public const int Length = HeaderLength + TagLength + SyncKey.LengthBytes;

    internal const int SaltLength = 32;
    internal const int NonceLength = VaultKeyWrap.Aes256GcmNonceLengthBytes;
    internal const int TagLength = VaultKeyWrap.Aes256GcmTagLengthBytes;

    private const int VersionOffset = 8;
    private const int VaultOffset = 10;
    private const int OfferIdOffset = 26;
    private const int KeyIdOffset = 42;
    private const int CreatedOffset = 58;
    private const int ExpiresOffset = 66;
    private const int SaltOffset = 74;
    private const int NonceOffset = 106;
    private const int TagOffset = HeaderLength;
    private const int CiphertextOffset = TagOffset + TagLength;

    private static readonly byte[] Magic = "PINEPAIR"u8.ToArray();
    private static readonly byte[] WrapInfo = Encoding.ASCII.GetBytes("pine:sync-pairing:v1");

    /// <summary>Wraps <paramref name="key"/> for one other vault, under <paramref name="code"/>.</summary>
    /// <param name="key">The user's sync key.</param>
    /// <param name="offeringVaultId">The vault making the offer.</param>
    /// <param name="code">The one-time code, which the user carries by hand.</param>
    /// <param name="createdAt">When it was made.</param>
    /// <param name="lifetime">How long it may be accepted for.</param>
    /// <returns>The offer: ciphertext and a header, never the key.</returns>
    /// <exception cref="ArgumentException">Not a code, no vault, or a lifetime an offer may not have.</exception>
    public static byte[] Create(SyncKey key, Guid offeringVaultId, string code, DateTimeOffset createdAt, TimeSpan lifetime)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (lifetime <= TimeSpan.Zero || lifetime > SyncPairingLimits.MaximumLifetime)
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime), lifetime, $"A pairing offer lasts between nothing and {SyncPairingLimits.MaximumLifetime}.");
        }

        var created = DateTimeOffset.FromUnixTimeMilliseconds(createdAt.ToUnixTimeMilliseconds());
        var header = new SyncPairingOfferHeader(
            CurrentVersion,
            offeringVaultId,
            Guid.NewGuid(),
            key.KeyId,
            created,
            created + lifetime);

        var material = new byte[SyncKey.LengthBytes];
        try
        {
            key.CopyMaterialTo(material);
            return CreateCore(header, material, code, RandomNumberGenerator.GetBytes(SaltLength), RandomNumberGenerator.GetBytes(NonceLength));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(material);
        }
    }

    /// <summary>
    /// Reads an offer's header WITHOUT authenticating it — what a screen may show before a code is typed. Nothing
    /// read here may be trusted until <see cref="Open"/> has succeeded.
    /// </summary>
    /// <exception cref="SyncPairingRefusedException">Not an offer, or a header no offerer writes.</exception>
    public static SyncPairingOfferHeader ReadHeader(ReadOnlySpan<byte> offer)
    {
        if (offer.Length != Length)
        {
            throw new SyncPairingRefusedException(SyncPairingRefusal.Malformed);
        }

        if (!offer[..Magic.Length].SequenceEqual(Magic))
        {
            throw new SyncPairingRefusedException(SyncPairingRefusal.Malformed);
        }

        var version = BinaryPrimitives.ReadUInt16LittleEndian(offer[VersionOffset..]);
        if (version != CurrentVersion)
        {
            throw new SyncPairingRefusedException(SyncPairingRefusal.UnsupportedVersion);
        }

        var vault = new Guid(offer.Slice(VaultOffset, 16));
        var offerId = new Guid(offer.Slice(OfferIdOffset, 16));
        if (vault == Guid.Empty || offerId == Guid.Empty)
        {
            throw new SyncPairingRefusedException(SyncPairingRefusal.Malformed);
        }

        DateTimeOffset created;
        DateTimeOffset expires;
        try
        {
            created = DateTimeOffset.FromUnixTimeMilliseconds(BinaryPrimitives.ReadInt64LittleEndian(offer[CreatedOffset..]));
            expires = DateTimeOffset.FromUnixTimeMilliseconds(BinaryPrimitives.ReadInt64LittleEndian(offer[ExpiresOffset..]));
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new SyncPairingRefusedException(SyncPairingRefusal.Malformed);
        }

        if (expires <= created || expires - created > SyncPairingLimits.MaximumLifetime)
        {
            throw new SyncPairingRefusedException(SyncPairingRefusal.Malformed);
        }

        return new SyncPairingOfferHeader(
            version,
            vault,
            offerId,
            offer.Slice(KeyIdOffset, SyncKey.KeyIdLengthBytes).ToArray(),
            created,
            expires);
    }

    /// <summary>Opens <paramref name="offer"/> with the code the user typed.</summary>
    /// <param name="offer">The offer as it was found.</param>
    /// <param name="code">As typed: spacing, case and hyphens do not matter.</param>
    /// <param name="thisVaultId">The vault accepting it; an offer from this vault is refused.</param>
    /// <param name="now">The accepting machine's clock, for the expiry.</param>
    /// <returns>The user's sync key. The caller owns it and disposes it.</returns>
    /// <exception cref="SyncPairingRefusedException">Refused; nothing was written.</exception>
    public static SyncKey Open(ReadOnlySpan<byte> offer, string code, Guid thisVaultId, DateTimeOffset now)
    {
        var header = ReadHeader(offer);

        if (SyncPairingCode.Normalise(code) is null)
        {
            // Before any cryptography, and before the clock: what was typed is not a code at all, which is a
            // different thing to say than "that code did not open it".
            throw new SyncPairingRefusedException(SyncPairingRefusal.CodeFormat);
        }

        var material = new byte[SyncKey.LengthBytes];
        var wrapKey = DeriveWrapKey(code, offer.Slice(SaltOffset, SaltLength));
        try
        {
            try
            {
                using var aesGcm = new AesGcm(wrapKey, TagLength);
                aesGcm.Decrypt(
                    offer.Slice(NonceOffset, NonceLength),
                    offer[CiphertextOffset..],
                    offer.Slice(TagOffset, TagLength),
                    material,
                    offer[..HeaderLength]);
            }
            catch (CryptographicException)
            {
                // ONE reason for every tag failure. A mistyped code and an altered offer are the same event here,
                // deliberately: separating them would tell a holder of a stolen offer whether a guess was close.
                throw new SyncPairingRefusedException(SyncPairingRefusal.NotAuthentic);
            }

            // Authenticated from here on, so the header's own claims can be acted on.
            if (now >= header.ExpiresAtUtc)
            {
                throw new SyncPairingRefusedException(SyncPairingRefusal.Expired);
            }

            if (header.OfferingVaultId == thisVaultId)
            {
                throw new SyncPairingRefusedException(SyncPairingRefusal.SameVault);
            }

            SyncKey key;
            try
            {
                key = SyncKey.FromBytes(material);
            }
            catch (ArgumentException)
            {
                // Authentic, but what it carries is not a key: refused, never kept.
                throw new SyncPairingRefusedException(SyncPairingRefusal.PayloadMismatch);
            }

            if (!key.HasId(header.KeyId))
            {
                key.Dispose();
                throw new SyncPairingRefusedException(SyncPairingRefusal.PayloadMismatch);
            }

            return key;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wrapKey);
            CryptographicOperations.ZeroMemory(material);
        }
    }

    /// <summary>
    /// Wraps a given payload with a given salt and nonce. Internal so a test can pin a known-answer vector and
    /// build offers no honest offerer writes; everything in production goes through <see cref="Create"/>.
    /// </summary>
    internal static byte[] CreateCore(SyncPairingOfferHeader header, ReadOnlySpan<byte> payload, string code, byte[] salt, byte[] nonce)
    {
        ArgumentNullException.ThrowIfNull(header);

        if (SyncPairingCode.Normalise(code) is null)
        {
            throw new ArgumentException("Not a pairing code.", nameof(code));
        }

        if (header.OfferingVaultId == Guid.Empty || header.OfferId == Guid.Empty)
        {
            throw new ArgumentException("An offer is made by a vault, and is one offer.", nameof(header));
        }

        if (header.ExpiresAtUtc <= header.CreatedAtUtc || header.ExpiresAtUtc - header.CreatedAtUtc > SyncPairingLimits.MaximumLifetime)
        {
            throw new ArgumentException("An offer expires after it was made, and within its maximum life.", nameof(header));
        }

        if (payload.Length != SyncKey.LengthBytes)
        {
            throw new ArgumentException($"An offer carries {SyncKey.LengthBytes} bytes.", nameof(payload));
        }

        if (salt.Length != SaltLength || nonce.Length != NonceLength)
        {
            throw new ArgumentException("Wrong salt or nonce length.", nameof(salt));
        }

        if (!AesGcm.IsSupported)
        {
            throw new VaultKeyWrapUnavailableException(VaultKeyWrapAlgorithms.Aes256Gcm);
        }

        var offer = new byte[Length];
        var span = offer.AsSpan();
        Magic.CopyTo(span);
        BinaryPrimitives.WriteUInt16LittleEndian(span[VersionOffset..], header.Version);
        header.OfferingVaultId.TryWriteBytes(span.Slice(VaultOffset, 16));
        header.OfferId.TryWriteBytes(span.Slice(OfferIdOffset, 16));
        header.KeyId.CopyTo(span.Slice(KeyIdOffset, SyncKey.KeyIdLengthBytes));
        BinaryPrimitives.WriteInt64LittleEndian(span[CreatedOffset..], header.CreatedAtUtc.ToUnixTimeMilliseconds());
        BinaryPrimitives.WriteInt64LittleEndian(span[ExpiresOffset..], header.ExpiresAtUtc.ToUnixTimeMilliseconds());
        salt.CopyTo(span.Slice(SaltOffset, SaltLength));
        nonce.CopyTo(span.Slice(NonceOffset, NonceLength));

        var wrapKey = DeriveWrapKey(code, salt);
        try
        {
            using var aesGcm = new AesGcm(wrapKey, TagLength);
            aesGcm.Encrypt(
                span.Slice(NonceOffset, NonceLength),
                payload,
                span[CiphertextOffset..],
                span.Slice(TagOffset, TagLength),
                span[..HeaderLength]);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wrapKey);
        }

        return offer;
    }

    /// <summary>The offer's AES-256-GCM key. The caller zeroes it.</summary>
    private static byte[] DeriveWrapKey(string code, ReadOnlySpan<byte> salt)
    {
        var codeBytes = SyncPairingCode.ToKeyMaterial(code);
        try
        {
            var wrapKey = new byte[SyncKey.LengthBytes];
            HKDF.DeriveKey(HashAlgorithmName.SHA256, codeBytes, wrapKey, salt, WrapInfo);
            return wrapKey;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(codeBytes);
        }
    }
}

/// <summary>What a pairing offer's header says. Unauthenticated until <see cref="SyncPairingOffer.Open"/> succeeds.</summary>
/// <param name="Version">The format version.</param>
/// <param name="OfferingVaultId">The vault that made the offer.</param>
/// <param name="OfferId">This offer, told apart from any other.</param>
/// <param name="KeyId">The id of the sync key it carries.</param>
/// <param name="CreatedAtUtc">When it was made, to the millisecond.</param>
/// <param name="ExpiresAtUtc">After this it is refused.</param>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "CA1819:Properties should not return arrays",
    Justification = "A 16-byte id read from an offer; ReadHeader hands out its own copy.")]
public sealed record SyncPairingOfferHeader(
    ushort Version,
    Guid OfferingVaultId,
    Guid OfferId,
    byte[] KeyId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc);
