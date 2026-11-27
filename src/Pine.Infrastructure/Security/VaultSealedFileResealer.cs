using Pine.Application.Security;
using Pine.Core.Security;

namespace Pine.Infrastructure.Security;

/// <summary>
/// Re-seals <c>sync.key</c> and <c>ai/categorizer.model</c> from the old data key to the new one (TD-86). Both are sealed
/// with a key derived from the data key (<see cref="AesGcmVaultFileCipher"/>), so a rotated data key makes them unreadable
/// until this runs. Idempotent, so recovery can repeat it after a crash.
/// </summary>
public sealed class VaultSealedFileResealer : IVaultSealedFileResealer
{
    private static readonly (string RelativePath, string Purpose, bool DerivedCache)[] Files =
    [
        ("sync.key", VaultFilePurposes.SyncKey, false),
        (Path.Combine("ai", "categorizer.model"), VaultFilePurposes.CategorizerModel, true),
        (Path.Combine("ai", "receipt-layout.model"), VaultFilePurposes.ReceiptLayoutModel, true),
    ];

    public void Reseal(string vaultDirectory, byte[] oldKey, byte[] newKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultDirectory);
        var oldCipher = new AesGcmVaultFileCipher(new FixedKey(vaultDirectory, oldKey));
        var newCipher = new AesGcmVaultFileCipher(new FixedKey(vaultDirectory, newKey));

        foreach (var (relative, purpose, derivedCache) in Files)
        {
            var path = Path.Combine(vaultDirectory, relative);
            if (!File.Exists(path)) continue;

            var blob = File.ReadAllBytes(path);
            if (TryDecrypt(newCipher, blob, purpose) is { } alreadyNew)
            {
                Array.Clear(alreadyNew);
                continue;
            }

            if (TryDecrypt(oldCipher, blob, purpose) is not { } plain)
            {
                // Unreadable under both keys. A derived cache is retrained; a sync key that was already
                // unreadable stays as it was (this must never be the reason a vault cannot open).
                if (derivedCache) File.Delete(path);
                continue;
            }

            try
            {
                var sealedBlob = newCipher.Encrypt(plain, purpose);
                var temp = path + ".reseal.tmp";
                using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    stream.Write(sealedBlob, 0, sealedBlob.Length);
                    stream.Flush(flushToDisk: true);
                }

                File.Move(temp, path, overwrite: true);
            }
            finally
            {
                Array.Clear(plain);
            }
        }
    }

    private static byte[]? TryDecrypt(AesGcmVaultFileCipher cipher, byte[] blob, string purpose)
    {
        try
        {
            return cipher.Decrypt(blob, purpose);
        }
        catch (VaultFileCipherException)
        {
            return null;
        }
    }

    private sealed class FixedKey(string directory, byte[] key) : IVaultDataKeyAccessor
    {
        private readonly byte[] _key = (byte[])key.Clone();

        public bool IsUnlocked => true;

        public byte[]? Key => _key;

        public string? DbPath => directory;
    }
}
