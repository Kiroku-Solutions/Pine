using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using Pine.Core.Security;

namespace Pine.Infrastructure.Sync;

/// <summary>
/// §5's envelope: a <see cref="SyncChangeSet"/> sealed with the user's <see cref="SyncKey"/>, so that what leaves the
/// process is ciphertext (post-audit plan item 3.1d; `14-Synchronization` §5, `11-Data-Model` §6.1).
/// </summary>
/// <remarks>
/// <para>
/// LAYOUT, VERSION 1. Little-endian throughout; offsets are part of the format.
/// </para>
/// <code>
///   0   8  magic "PINESYNC"
///   8   2  format version (1)
///  10  16  source vault id   (Guid.TryWriteBytes layout)
///  26  16  target vault id
///  42   8  since clock       (the cursor it was cut from; -1 for "everything")
///  50   8  source clock      (the cursor the target moves to)
///  58   8  created at        (Unix milliseconds, UTC)
///  66  16  key id            (SyncKey.KeyId)
///  82  32  salt              (random per envelope; the HKDF salt for its key)
/// 114  12  nonce             (random per envelope)
/// 126  16  AES-GCM tag
/// 142   …  ciphertext of the pine.sync.v1 JSON, UTF-8
/// </code>
/// <para>
/// WHAT A PROVIDER SEES (§5): the ciphertext, the cursor (since and source clock), the size and the creation
/// timestamp — and the vault ids and the key id, which route and select, and say nothing about the rows.
/// </para>
/// <para>
/// THE WHOLE HEADER IS THE ASSOCIATED DATA — every byte before the tag, nonce included, following the rule
/// <c>VaultHeaderBinding</c> set: bind everything available rather than choosing fields. So no header field can be
/// edited, and no header moved onto another envelope's body, without the tag failing. The header is fixed-length,
/// so the binding is unambiguous without length prefixes.
/// </para>
/// <para>
/// "THE SIGNATURE IS OVER THE CIPHERTEXT" (§5) IS THE GCM TAG. It authenticates the ciphertext and the header under a
/// key only holders of the sync key can derive. It is not an asymmetric signature: it proves "sealed by a holder of
/// the user's sync key", not "sealed by device X". A per-device signature needs device key pairs and a way to trust
/// them, neither of which exists; building one would be new cryptography this item does not invent.
/// </para>
/// <para>
/// NO COMPRESSION. §5 does not ask for it, and compressing before encrypting makes the ciphertext's length a function
/// of the plaintext's content, which a provider can see.
/// </para>
/// <para>
/// REFUSALS ARE ORDERED so that every reason after <see cref="SyncEnvelopeRefusal.NotAuthentic"/> is about a header
/// that has been authenticated: shape and version, then the key id (the wrong key named as such), then the tag, then
/// the target vault and the payload's agreement with its header. The cursor rules (replay, gap) need the target's
/// database and are applied by <see cref="VaultMerger.ApplySealedAsync"/>, inside its transaction.
/// </para>
/// </remarks>
public static class SyncEnvelope
{
    /// <summary>The envelope format's name and major version.</summary>
    public const string FormatName = "pine.sync-envelope.v1";

    /// <summary>The format version this build writes and reads.</summary>
    public const ushort CurrentVersion = 1;

    /// <summary>Length of the header, which is the associated data: everything before the tag.</summary>
    public const int HeaderLength = 126;

    /// <summary>The shortest envelope there can be: a header, a tag and no ciphertext.</summary>
    public const int MinimumLength = HeaderLength + TagLength;

    internal const int SaltLength = 32;
    internal const int NonceLength = VaultKeyWrap.Aes256GcmNonceLengthBytes;
    internal const int TagLength = VaultKeyWrap.Aes256GcmTagLengthBytes;

    private const int VersionOffset = 8;
    private const int SourceOffset = 10;
    private const int TargetOffset = 26;
    private const int SinceOffset = 42;
    private const int SourceClockOffset = 50;
    private const int CreatedOffset = 58;
    private const int KeyIdOffset = 66;
    private const int SaltOffset = 82;
    private const int NonceOffset = 114;
    private const int TagOffset = HeaderLength;
    private const int CiphertextOffset = TagOffset + TagLength;

    private static readonly byte[] Magic = "PINESYNC"u8.ToArray();

    /// <summary>Seals <paramref name="changeSet"/> under <paramref name="key"/>.</summary>
    /// <param name="changeSet">A <c>pine.sync.v1</c> change set with both vault ids.</param>
    /// <param name="key">The user's sync key.</param>
    /// <param name="createdAt">When it was sealed; informative, for the provider (§5).</param>
    /// <returns>The envelope. Never the plaintext.</returns>
    /// <exception cref="ArgumentException">The change set is not one an envelope can carry.</exception>
    public static byte[] Seal(SyncChangeSet changeSet, SyncKey key, DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(changeSet);
        ArgumentNullException.ThrowIfNull(key);

        var plaintext = changeSet.ToUtf8Json();
        try
        {
            return SealCore(
                new SyncEnvelopeHeader(
                    CurrentVersion,
                    changeSet.SourceVaultId,
                    changeSet.TargetVaultId,
                    changeSet.SinceClock,
                    changeSet.SourceClock,
                    DateTimeOffset.FromUnixTimeMilliseconds(createdAt.ToUnixTimeMilliseconds()),
                    key.KeyId),
                changeSet.Format,
                plaintext,
                key,
                RandomNumberGenerator.GetBytes(SaltLength),
                RandomNumberGenerator.GetBytes(NonceLength));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    /// <summary>
    /// Reads an envelope's header WITHOUT authenticating it — what a provider or a router may look at. Nothing read
    /// here may be trusted until <see cref="Open"/> has succeeded.
    /// </summary>
    /// <exception cref="SyncEnvelopeRefusedException">Too short, not an envelope, another version, or a header no
    /// sealer writes.</exception>
    public static SyncEnvelopeHeader ReadHeader(ReadOnlySpan<byte> envelope)
    {
        if (envelope.Length < MinimumLength)
        {
            throw new SyncEnvelopeRefusedException(SyncEnvelopeRefusal.Malformed, $"An envelope is at least {MinimumLength} bytes; this is {envelope.Length}.");
        }

        if (!envelope[..Magic.Length].SequenceEqual(Magic))
        {
            throw new SyncEnvelopeRefusedException(SyncEnvelopeRefusal.Malformed, "This is not a Pine sync envelope.");
        }

        var version = BinaryPrimitives.ReadUInt16LittleEndian(envelope[VersionOffset..]);
        if (version != CurrentVersion)
        {
            throw new SyncEnvelopeRefusedException(SyncEnvelopeRefusal.UnsupportedVersion, $"The envelope is version {version}; this build reads version {CurrentVersion}.");
        }

        var source = new Guid(envelope.Slice(SourceOffset, 16));
        var target = new Guid(envelope.Slice(TargetOffset, 16));
        var since = BinaryPrimitives.ReadInt64LittleEndian(envelope[SinceOffset..]);
        var sourceClock = BinaryPrimitives.ReadInt64LittleEndian(envelope[SourceClockOffset..]);
        var createdMs = BinaryPrimitives.ReadInt64LittleEndian(envelope[CreatedOffset..]);

        if (source == Guid.Empty || target == Guid.Empty || source == target)
        {
            throw new SyncEnvelopeRefusedException(SyncEnvelopeRefusal.Malformed, "The envelope's header does not name two different vaults.");
        }

        if (since < SyncChangeSet.Never || sourceClock < 0 || sourceClock < since)
        {
            throw new SyncEnvelopeRefusedException(SyncEnvelopeRefusal.Malformed, "The envelope's cursor runs backwards.");
        }

        DateTimeOffset createdAt;
        try
        {
            createdAt = DateTimeOffset.FromUnixTimeMilliseconds(createdMs);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new SyncEnvelopeRefusedException(SyncEnvelopeRefusal.Malformed, "The envelope's creation time is not a time.");
        }

        return new SyncEnvelopeHeader(version, source, target, since, sourceClock, createdAt, envelope.Slice(KeyIdOffset, SyncKey.KeyIdLengthBytes).ToArray());
    }

    /// <summary>Opens an envelope sealed for <paramref name="thisVaultId"/> under <paramref name="key"/>.</summary>
    /// <param name="envelope">The envelope as it arrived.</param>
    /// <param name="key">The user's sync key.</param>
    /// <param name="thisVaultId">The vault it is being opened for; <see cref="Guid.Empty"/> for a vault with no id yet,
    /// which no envelope can be for.</param>
    /// <returns>The change set, in memory only.</returns>
    /// <exception cref="SyncEnvelopeRefusedException">For every reason in <see cref="SyncEnvelopeRefusal"/> except
    /// the two cursor rules, which need the target vault.</exception>
    public static SyncChangeSet Open(ReadOnlySpan<byte> envelope, SyncKey key, Guid thisVaultId)
    {
        ArgumentNullException.ThrowIfNull(key);

        var header = ReadHeader(envelope);
        if (!key.HasId(header.KeyId))
        {
            throw new SyncEnvelopeRefusedException(SyncEnvelopeRefusal.WrongKey, "The envelope was sealed under another sync key.");
        }

        var plaintext = new byte[envelope.Length - CiphertextOffset];
        var envelopeKey = key.DeriveEnvelopeKey(envelope.Slice(SaltOffset, SaltLength));
        try
        {
            try
            {
                using var aesGcm = new AesGcm(envelopeKey, TagLength);
                aesGcm.Decrypt(
                    envelope.Slice(NonceOffset, NonceLength),
                    envelope[CiphertextOffset..],
                    envelope.Slice(TagOffset, TagLength),
                    plaintext,
                    envelope[..HeaderLength]);
            }
            catch (CryptographicException)
            {
                // One reason for every tag failure — a flipped bit, a swapped header, a forged key id — and no inner
                // exception: which of those it was is the question an attacker would want answered.
                throw new SyncEnvelopeRefusedException(SyncEnvelopeRefusal.NotAuthentic, "The envelope is not authentic: it was altered, or not sealed under this key.");
            }

            if (header.TargetVaultId != thisVaultId)
            {
                throw new SyncEnvelopeRefusedException(SyncEnvelopeRefusal.WrongTarget, $"The envelope was sealed for vault {header.TargetVaultId}, not this vault ({thisVaultId}).");
            }

            SyncChangeSet changeSet;
            try
            {
                changeSet = SyncChangeSet.FromUtf8Json(plaintext);
            }
            catch (SyncMergeRefusedException)
            {
                // No inner exception: its message can quote the payload.
                throw new SyncEnvelopeRefusedException(SyncEnvelopeRefusal.PayloadMismatch, "The envelope is authentic but does not hold a pine.sync.v1 change set.");
            }

            if (changeSet.SourceVaultId != header.SourceVaultId ||
                changeSet.TargetVaultId != header.TargetVaultId ||
                changeSet.SinceClock != header.SinceClock ||
                changeSet.SourceClock != header.SourceClock)
            {
                throw new SyncEnvelopeRefusedException(SyncEnvelopeRefusal.PayloadMismatch, "The envelope's header and the change set inside it name different vaults or cursors.");
            }

            return changeSet;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(envelopeKey);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    /// <summary>
    /// Seals with a given salt and nonce. Internal so that a test can pin a known-answer vector; everything in
    /// production goes through <see cref="Seal"/>, which draws both from the CSPRNG.
    /// </summary>
    internal static byte[] SealCore(SyncEnvelopeHeader header, string format, ReadOnlySpan<byte> plaintext, SyncKey key, byte[] salt, byte[] nonce)
    {
        if (!string.Equals(format, SyncChangeSet.FormatName, StringComparison.Ordinal))
        {
            throw new ArgumentException($"An envelope carries a {SyncChangeSet.FormatName} change set, not '{format}'.", nameof(format));
        }

        if (header.SourceVaultId == Guid.Empty || header.TargetVaultId == Guid.Empty || header.SourceVaultId == header.TargetVaultId)
        {
            throw new ArgumentException("An envelope is from one vault to another.", nameof(header));
        }

        if (header.SinceClock < SyncChangeSet.Never || header.SourceClock < 0 || header.SourceClock < header.SinceClock)
        {
            throw new ArgumentException("An envelope's cursor cannot run backwards.", nameof(header));
        }

        if (salt.Length != SaltLength || nonce.Length != NonceLength)
        {
            throw new ArgumentException("Wrong salt or nonce length.", nameof(salt));
        }

        if (!AesGcm.IsSupported)
        {
            throw new VaultKeyWrapUnavailableException(VaultKeyWrapAlgorithms.Aes256Gcm);
        }

        var envelope = new byte[CiphertextOffset + plaintext.Length];
        var span = envelope.AsSpan();
        Magic.CopyTo(span);
        BinaryPrimitives.WriteUInt16LittleEndian(span[VersionOffset..], header.Version);
        header.SourceVaultId.TryWriteBytes(span.Slice(SourceOffset, 16));
        header.TargetVaultId.TryWriteBytes(span.Slice(TargetOffset, 16));
        BinaryPrimitives.WriteInt64LittleEndian(span[SinceOffset..], header.SinceClock);
        BinaryPrimitives.WriteInt64LittleEndian(span[SourceClockOffset..], header.SourceClock);
        BinaryPrimitives.WriteInt64LittleEndian(span[CreatedOffset..], header.CreatedAtUtc.ToUnixTimeMilliseconds());
        key.KeyId.CopyTo(span.Slice(KeyIdOffset, SyncKey.KeyIdLengthBytes));
        salt.CopyTo(span.Slice(SaltOffset, SaltLength));
        nonce.CopyTo(span.Slice(NonceOffset, NonceLength));

        var envelopeKey = key.DeriveEnvelopeKey(salt);
        try
        {
            using var aesGcm = new AesGcm(envelopeKey, TagLength);
            aesGcm.Encrypt(
                span.Slice(NonceOffset, NonceLength),
                plaintext,
                span[CiphertextOffset..],
                span.Slice(TagOffset, TagLength),
                span[..HeaderLength]);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(envelopeKey);
        }

        return envelope;
    }
}

/// <summary>What an envelope's header says. Unauthenticated until <see cref="SyncEnvelope.Open"/> succeeds.</summary>
/// <param name="Version">The format version.</param>
/// <param name="SourceVaultId">The vault that sealed it.</param>
/// <param name="TargetVaultId">The vault it is for.</param>
/// <param name="SinceClock">The cursor it was cut from.</param>
/// <param name="SourceClock">The cursor the target moves to.</param>
/// <param name="CreatedAtUtc">When it was sealed, to the millisecond.</param>
/// <param name="KeyId">The id of the sync key it was sealed under.</param>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "CA1819:Properties should not return arrays",
    Justification = "A 16-byte id read from an envelope; ReadHeader hands out its own copy.")]
public sealed record SyncEnvelopeHeader(
    ushort Version,
    Guid SourceVaultId,
    Guid TargetVaultId,
    long SinceClock,
    long SourceClock,
    DateTimeOffset CreatedAtUtc,
    byte[] KeyId);

/// <summary>Why an envelope was refused. Every refusal happens before anything is written.</summary>
public enum SyncEnvelopeRefusal
{
    /// <summary>Too short, not an envelope, or a header no sealer writes.</summary>
    Malformed = 1,

    /// <summary>A format version this build does not read.</summary>
    UnsupportedVersion,

    /// <summary>Sealed under another sync key (its header names another key id).</summary>
    WrongKey,

    /// <summary>The tag does not verify: altered anywhere, header swapped, or a forged key id.</summary>
    NotAuthentic,

    /// <summary>Authentic, but sealed for another vault.</summary>
    WrongTarget,

    /// <summary>Authentic, but its payload is not a change set, or names vaults or cursors its header does not.</summary>
    PayloadMismatch,

    /// <summary>This vault already has everything up to the envelope's source clock: a replay, or an older envelope.</summary>
    Replayed,

    /// <summary>The envelope starts after this vault's cursor for its source: applying it would skip changes.</summary>
    Gap,
}

/// <summary>
/// An envelope that was not opened or not applied (post-audit plan item 3.1d). Nothing was written. The message
/// never quotes the payload.
/// </summary>
public sealed class SyncEnvelopeRefusedException : InvalidOperationException
{
    public SyncEnvelopeRefusedException()
    {
    }

    public SyncEnvelopeRefusedException(string message)
        : base(message)
    {
    }

    public SyncEnvelopeRefusedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public SyncEnvelopeRefusedException(SyncEnvelopeRefusal reason, string message)
        : base(message)
    {
        Reason = reason;
    }

    /// <summary>Why it was refused.</summary>
    public SyncEnvelopeRefusal Reason { get; }
}
