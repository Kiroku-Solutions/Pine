using System;
using System.Security.Cryptography;
using System.Text;
using Pine.Core.Sync;

namespace Pine.Infrastructure.Sync;

/// <summary>
/// The user's sync key: the 32 bytes every <see cref="SyncEnvelope"/> is sealed under (post-audit plan item 3.1d;
/// `14-Synchronization` §5, `15-Security` §7).
/// </summary>
/// <remarks>
/// <para>
/// ONE KEY PER USER, NOT PER VAULT, AND DRAWN FROM THE CSPRNG RATHER THAN DERIVED FROM A DATA KEY. `15-Security` §7
/// says the sync key "is derived from the data key". Taken literally that cannot work between two vaults: each
/// vault's data key is its own 32 random bytes (<c>AesGcmVaultKeyService</c>), so a key derived from one vault's
/// data key is a key the other vault can never compute, and the envelope would open only where it was sealed. It
/// would also change the day the data key is really rotated (TD-86), orphaning every envelope still in flight. So
/// the sync key is generated once, by the first vault, and held by every vault of the user — and the part of §7
/// that can be kept is kept: each vault holds it only sealed under a key derived from ITS data key
/// (<see cref="VaultSyncKeyStore"/>), so reaching it still requires unlocking that vault. How the second vault is
/// given it (pairing) is not built; no transport is.
/// </para>
/// <para>
/// NEVER USED DIRECTLY AS A CIPHER KEY. Each envelope is sealed under <c>HKDF-SHA256(sync key, salt = the envelope's
/// own 32 random bytes, info = "pine:sync-envelope:v1")</c>, so no two envelopes share an AES-GCM key and the
/// random-nonce bound on how many messages one key may seal never comes into play. The same reasoning
/// <c>AesGcmVaultFileCipher</c> gives for never encrypting under the data key itself.
/// </para>
/// <para>
/// ITS ID IS PUBLIC. <see cref="KeyId"/> is <c>HKDF-SHA256(sync key, info = "pine:sync-key-id:v1")</c>, 16 bytes,
/// and travels in every envelope's header so that the wrong key is refused as the wrong key rather than as a
/// damaged envelope. It reveals nothing an observer could not compute from a guess of the key, which is the only
/// thing HKDF's output ever reveals.
/// </para>
/// <para>
/// ZEROED ON DISPOSE. The material is cleared with <see cref="CryptographicOperations.ZeroMemory(Span{byte})"/>; any
/// use afterwards throws. <see cref="ToString"/> names the key id and never the material, so a key that reaches a
/// log line by accident says which key it was and nothing more.
/// </para>
/// </remarks>
public sealed class SyncKey : IDisposable
{
    /// <summary>Length of the key in bytes: AES-256's key size, and HKDF's input here.</summary>
    public const int LengthBytes = 32;

    /// <summary>Length of the public key id in bytes.</summary>
    public const int KeyIdLengthBytes = 16;

    /// <summary>Length of the verification fingerprint in bytes: 80 bits, shown as 16 characters.</summary>
    public const int FingerprintLengthBytes = 10;

    private static readonly byte[] KeyIdInfo = Encoding.ASCII.GetBytes("pine:sync-key-id:v1");
    private static readonly byte[] EnvelopeKeyInfo = Encoding.ASCII.GetBytes("pine:sync-envelope:v1");
    private static readonly byte[] FingerprintInfo = Encoding.ASCII.GetBytes("pine:sync-key-fingerprint:v1");

    private readonly byte[] _material;
    private readonly byte[] _keyId;
    private bool _disposed;

    private SyncKey(byte[] material)
    {
        _material = material;
        _keyId = new byte[KeyIdLengthBytes];
        HKDF.DeriveKey(HashAlgorithmName.SHA256, _material, _keyId, salt: ReadOnlySpan<byte>.Empty, KeyIdInfo);

        // Computed once, here, so it survives Dispose: a fingerprint is shown to compare two devices, and the
        // comparison outlives the key object the screen was given.
        var fingerprint = new byte[FingerprintLengthBytes];
        try
        {
            HKDF.DeriveKey(HashAlgorithmName.SHA256, _material, fingerprint, salt: ReadOnlySpan<byte>.Empty, FingerprintInfo);
            Fingerprint = FormatFingerprint(fingerprint);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(fingerprint);
        }
    }

    /// <summary>
    /// The key's verification fingerprint, grouped for reading aloud: <c>XXXX-XXXX-XXXX-XXXX</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHAT IT IS FOR. Both devices show it after pairing, so that two vaults that ended up with DIFFERENT keys —
    /// a pairing that appeared to work, a vault restored from an older backup, an offer accepted from the wrong
    /// machine — are visibly different rather than silently unable to open each other's envelopes.
    /// </para>
    /// <para>
    /// A SEPARATE DERIVATION FROM <see cref="KeyId"/>, and 80 bits of it, because these two are read by different
    /// readers: the key id is machine routing inside every envelope header, the fingerprint is sixteen characters a
    /// person compares by eye. Sharing one value would mean a change to what people read is a change to the wire
    /// format. Like the key id it is an HKDF output and reveals nothing a guess of the key would not.
    /// </para>
    /// </remarks>
    public string Fingerprint { get; }

    /// <summary>A new key from the CSPRNG.</summary>
    public static SyncKey Generate() => new(RandomNumberGenerator.GetBytes(LengthBytes));

    /// <summary>A key from its 32 bytes, copied; the caller still owns (and should clear) its own buffer.</summary>
    /// <exception cref="ArgumentException">Not 32 bytes, or all zero — a buffer that was cleared, not a key.</exception>
    public static SyncKey FromBytes(ReadOnlySpan<byte> material)
    {
        if (material.Length != LengthBytes)
        {
            throw new ArgumentException($"A sync key is {LengthBytes} bytes.", nameof(material));
        }

        if (material.IndexOfAnyExcept((byte)0) < 0)
        {
            throw new ArgumentException("A sync key of all zero bytes is a cleared buffer, not a key.", nameof(material));
        }

        return new SyncKey(material.ToArray());
    }

    /// <summary>The key's public id (a copy): what an envelope header names.</summary>
    public byte[] KeyId => (byte[])_keyId.Clone();

    /// <summary>Whether <paramref name="keyId"/> is this key's id, compared in constant time.</summary>
    public bool HasId(ReadOnlySpan<byte> keyId) =>
        CryptographicOperations.FixedTimeEquals(_keyId, keyId);

    /// <inheritdoc />
    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(_material);
        _disposed = true;
    }

    /// <summary>The key id, never the material.</summary>
    public override string ToString() => "SyncKey " + Convert.ToHexString(_keyId);

    /// <summary>Copies the material into <paramref name="destination"/>, for sealing it into a vault.</summary>
    internal void CopyMaterialTo(Span<byte> destination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _material.CopyTo(destination);
    }

    /// <summary>Sixteen characters of <see cref="SyncPairingCode.Alphabet"/> in four groups, from 80 bits.</summary>
    private static string FormatFingerprint(ReadOnlySpan<byte> bytes)
    {
        var characters = new char[19];
        var written = 0;
        var bitBuffer = 0;
        var bitCount = 0;
        foreach (var b in bytes)
        {
            bitBuffer = (bitBuffer << 8) | b;
            bitCount += 8;
            while (bitCount >= 5)
            {
                bitCount -= 5;
                if (written is 4 or 9 or 14)
                {
                    characters[written++] = '-';
                }

                characters[written++] = SyncPairingCode.Alphabet[(bitBuffer >> bitCount) & 31];
            }
        }

        return new string(characters);
    }

    /// <summary>One envelope's AES-256-GCM key. The caller zeroes it.</summary>
    internal byte[] DeriveEnvelopeKey(ReadOnlySpan<byte> salt)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var key = new byte[LengthBytes];
        HKDF.DeriveKey(HashAlgorithmName.SHA256, _material, key, salt, EnvelopeKeyInfo);
        return key;
    }
}
