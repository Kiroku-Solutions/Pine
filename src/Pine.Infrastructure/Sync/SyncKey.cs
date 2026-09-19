using System;
using System.Security.Cryptography;
using System.Text;

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

    private static readonly byte[] KeyIdInfo = Encoding.ASCII.GetBytes("pine:sync-key-id:v1");
    private static readonly byte[] EnvelopeKeyInfo = Encoding.ASCII.GetBytes("pine:sync-envelope:v1");

    private readonly byte[] _material;
    private readonly byte[] _keyId;
    private bool _disposed;

    private SyncKey(byte[] material)
    {
        _material = material;
        _keyId = new byte[KeyIdLengthBytes];
        HKDF.DeriveKey(HashAlgorithmName.SHA256, _material, _keyId, salt: ReadOnlySpan<byte>.Empty, KeyIdInfo);
    }

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

    /// <summary>One envelope's AES-256-GCM key. The caller zeroes it.</summary>
    internal byte[] DeriveEnvelopeKey(ReadOnlySpan<byte> salt)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var key = new byte[LengthBytes];
        HKDF.DeriveKey(HashAlgorithmName.SHA256, _material, key, salt, EnvelopeKeyInfo);
        return key;
    }
}
