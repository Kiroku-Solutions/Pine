using System.Text.Json;
using Pine.Application.Security;
using Pine.Core.Security;

namespace Pine.Infrastructure.Security;

/// <summary>
/// The write-ahead rekey journal as <c>pine.rekey.journal</c> in the vault folder (TD-86). It holds only wrapped keys
/// (ciphertexts under the old and the new password); written to a temp file, flushed to the device, then moved into place.
/// </summary>
public sealed class FileVaultRekeyJournal : IVaultRekeyJournal
{
    public const string FileName = "pine.rekey.journal";
    private const string JournalMagic = "PINE-REKEY-JOURNAL";

    public bool Exists(string vaultDirectory) => File.Exists(PathFor(vaultDirectory));

    public void Write(string vaultDirectory, VaultRekeyJournalEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var path = PathFor(vaultDirectory);
        var temp = path + ".tmp";
        var dto = new RekeyJournalDto
        {
            Magic = JournalMagic,
            NewHeader = FileVaultHeaderStore.ToDto(entry.NewHeader),
            NewKeyUnderCurrentPassword = ToDto(entry.NewKeyUnderCurrentPassword),
            CurrentKeyUnderNewPassword = ToDto(entry.CurrentKeyUnderNewPassword),
        };

        try
        {
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, dto, VaultHeaderJsonContext.Default.RekeyJournalDto);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch (Exception) { /* best effort */ }
            throw;
        }
    }

    public VaultRekeyJournalEntry? TryRead(string vaultDirectory)
    {
        try
        {
            var path = PathFor(vaultDirectory);
            if (!File.Exists(path)) return null;
            var dto = JsonSerializer.Deserialize(File.ReadAllText(path), VaultHeaderJsonContext.Default.RekeyJournalDto);
            if (dto is null || dto.Magic != JournalMagic || dto.NewHeader is null) return null;

            return new VaultRekeyJournalEntry(
                FileVaultHeaderStore.FromDto(dto.NewHeader, path, vaultDirectory),
                FromDto(dto.NewKeyUnderCurrentPassword),
                FromDto(dto.CurrentKeyUnderNewPassword));
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Delete(string vaultDirectory)
    {
        var path = PathFor(vaultDirectory);
        if (File.Exists(path)) File.Delete(path);
        if (File.Exists(path))
        {
            throw new IOException("The rekey journal could not be removed.");
        }
    }

    private static string PathFor(string vaultDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultDirectory);
        return Path.Combine(vaultDirectory, FileName);
    }

    private static VaultKeyWrapDto ToDto(VaultKeyWrap wrap) => new()
    {
        Algorithm = wrap.Algorithm,
        NonceBase64 = Convert.ToBase64String(wrap.Nonce),
        CiphertextBase64 = Convert.ToBase64String(wrap.Ciphertext),
        TagBase64 = Convert.ToBase64String(wrap.Tag),
    };

    private static VaultKeyWrap FromDto(VaultKeyWrapDto? dto) => new(
        dto!.Algorithm!,
        Convert.FromBase64String(dto.NonceBase64!),
        Convert.FromBase64String(dto.CiphertextBase64!),
        Convert.FromBase64String(dto.TagBase64!));
}

internal sealed class RekeyJournalDto
{
    public string? Magic { get; set; }

    public VaultHeaderDto? NewHeader { get; set; }

    public VaultKeyWrapDto? NewKeyUnderCurrentPassword { get; set; }

    public VaultKeyWrapDto? CurrentKeyUnderNewPassword { get; set; }
}
