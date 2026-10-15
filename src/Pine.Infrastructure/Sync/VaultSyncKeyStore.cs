using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using Pine.Core.Security;

namespace Pine.Infrastructure.Sync;

/// <summary>
/// Keeps the user's <see cref="SyncKey"/> beside one vault, sealed under that vault's data key (post-audit plan item
/// 3.1d; `15-Security` §4 and §7).
/// </summary>
/// <remarks>
/// <para>
/// NEVER ON DISK IN PLAINTEXT. The file holds only what <see cref="IVaultFileCipher"/> seals under the purpose
/// <see cref="VaultFilePurposes.SyncKey"/>: AES-256-GCM under <c>HKDF-SHA256(data key, "pine:file-cipher:v1:sync.key")</c>,
/// the same boundary the categoriser's model crosses (TD-16). No new cryptography; the key is reachable only by
/// someone who has unlocked this vault.
/// </para>
/// <para>
/// OUTSIDE THE DATABASE ON PURPOSE. A table would be read by the merge's export if it were ever added to the synced
/// set by mistake, and it would ride along in every backup; a file beside <c>pine.db</c> is neither. A restore replaces
/// <c>pine.db</c> and leaves this file, which is right: the restored vault is the same vault, under the same data key,
/// and belongs to the same user.
/// </para>
/// <para>
/// NEVER SILENTLY REPLACED, for the reason <c>FileDeviceIdentity</c> gives for its id: a vault that quietly took a new
/// sync key would seal envelopes no peer can open. Saving a different key over an existing one is refused
/// (<see cref="SyncKeyConflictException"/>); a damaged file is refused (<see cref="VaultFileCipherException"/>) rather
/// than read as absent. Written to a temporary file, flushed, and moved into place without overwriting.
/// </para>
/// <para>
/// NOT IN THE CONTAINER. It is bound to one vault directory, which is known only once a vault is open, so the
/// services that need it — <see cref="VaultSyncPairing"/> and, since item 3.1f, <see cref="VaultSyncRunner"/> —
/// construct it over the open vault (and so do the tests).
/// </para>
/// </remarks>
public sealed class VaultSyncKeyStore
{
    /// <summary>The file's name inside the vault directory.</summary>
    public const string FileName = "sync.key";

    private readonly IVaultFileCipher _cipher;

    /// <summary>Creates the store for one vault directory.</summary>
    /// <param name="cipher">The cipher over that vault's data key.</param>
    /// <param name="vaultDirectory">The vault's directory (where <c>pine.db</c> is).</param>
    public VaultSyncKeyStore(IVaultFileCipher cipher, string vaultDirectory)
    {
        ArgumentNullException.ThrowIfNull(cipher);
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultDirectory);
        _cipher = cipher;
        FilePath = Path.Combine(vaultDirectory, FileName);
    }

    /// <summary>Where the sealed key is kept.</summary>
    public string FilePath { get; }

    /// <summary>The vault's sync key, or null when it has none. The caller disposes it.</summary>
    /// <exception cref="VaultFileCipherException">The file is damaged, or sealed by another vault.</exception>
    public SyncKey? Load()
    {
        if (!File.Exists(FilePath))
        {
            return null;
        }

        var material = _cipher.Decrypt(File.ReadAllBytes(FilePath), VaultFilePurposes.SyncKey);
        try
        {
            return SyncKey.FromBytes(material);
        }
        catch (ArgumentException)
        {
            // Authentic but not a key: refused the same way a damaged file is.
            throw new VaultFileCipherException(VaultFilePurposes.SyncKey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(material);
        }
    }

    /// <summary>The vault's sync key, generated and kept the first time it is asked for. The caller disposes it.</summary>
    public SyncKey GetOrCreate()
    {
        if (Load() is { } existing)
        {
            return existing;
        }

        var key = SyncKey.Generate();
        try
        {
            Save(key);
            return key;
        }
        catch
        {
            key.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Keeps <paramref name="key"/> for this vault — the first vault's own, or the one a second vault is given.
    /// Saving the key it already has does nothing.
    /// </summary>
    /// <exception cref="SyncKeyConflictException">The vault already has a different sync key.</exception>
    public void Save(SyncKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        using (var existing = Load())
        {
            if (existing is not null)
            {
                if (existing.HasId(key.KeyId))
                {
                    return;
                }

                throw new SyncKeyConflictException();
            }
        }

        var material = new byte[SyncKey.LengthBytes];
        byte[] sealedKey;
        try
        {
            key.CopyMaterialTo(material);
            sealedKey = _cipher.Encrypt(material, VaultFilePurposes.SyncKey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(material);
        }

        var temporary = FilePath + "." + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            stream.Write(sealedKey, 0, sealedKey.Length);
            stream.Flush(flushToDisk: true);
        }

        try
        {
            File.Move(temporary, FilePath, overwrite: false);
        }
        catch (IOException) when (File.Exists(FilePath))
        {
            // Another writer won the race; theirs stands, and a different key is still refused.
            File.Delete(temporary);
            using var winner = Load();
            if (winner is null || !winner.HasId(key.KeyId))
            {
                throw new SyncKeyConflictException();
            }
        }
    }
}

/// <summary>The vault already holds a different sync key, and one is never replaced silently (item 3.1d).</summary>
public sealed class SyncKeyConflictException : InvalidOperationException
{
    public SyncKeyConflictException()
        : base("This vault already has a different sync key. It is not replaced, because envelopes sealed under the new key could not be opened by the vaults that hold the old one.")
    {
    }

    public SyncKeyConflictException(string message)
        : base(message)
    {
    }

    public SyncKeyConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
