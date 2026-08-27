using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Pine.Core.Security;

namespace Pine.Infrastructure.Security;

/// <summary>
/// Stores the vault header as <c>pine.vault.json</c> inside the vault directory.
/// </summary>
public sealed class FileVaultHeaderStore : IVaultHeaderStore
{
    /// <summary>Name of the versioned header file.</summary>
    public const string HeaderFileName = "pine.vault.json";

    /// <summary>Name of the pre-TD-05 raw salt file that older vaults still carry.</summary>
    public const string LegacySaltFileName = "pine.salt";

    private const string TempFileSuffix = ".tmp";

    private readonly KdfParameters _defaultParameters;

    /// <summary>Creates a store that stamps new vaults with the current production Argon2id profile.</summary>
    public FileVaultHeaderStore()
        : this(KdfParameters.Argon2idV1)
    {
    }

    /// <summary>Creates a store that stamps new vaults with the supplied profile.</summary>
    public FileVaultHeaderStore(KdfParameters defaultParameters)
    {
        ArgumentNullException.ThrowIfNull(defaultParameters);
        _defaultParameters = defaultParameters;
    }

    /// <inheritdoc />
    public bool Exists(string vaultDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultDirectory);
        return File.Exists(GetHeaderPath(vaultDirectory));
    }

    /// <inheritdoc />
    public VaultHeader Read(string vaultDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultDirectory);

        var path = GetHeaderPath(vaultDirectory);
        if (!File.Exists(path))
        {
            throw new VaultHeaderNotFoundException(vaultDirectory);
        }

        // IOException / UnauthorizedAccessException are deliberately allowed to propagate: a
        // locked or unreadable file is an environment problem, NOT header corruption, and
        // conflating the two would push the caller towards "recreate the vault".
        var json = File.ReadAllText(path);

        VaultHeaderDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize(json, VaultHeaderJsonContext.Default.VaultHeaderDto);
        }
        catch (JsonException ex)
        {
            throw new VaultHeaderCorruptException(path, "The vault header is not valid JSON.", ex);
        }

        if (dto is null)
        {
            throw new VaultHeaderCorruptException(path, "The vault header is empty.");
        }

        if (!string.Equals(dto.Magic, VaultHeader.Magic, StringComparison.Ordinal))
        {
            throw new VaultHeaderCorruptException(
                path,
                $"The vault header is missing the '{VaultHeader.Magic}' marker.");
        }

        if (dto.HeaderVersion is not { } headerVersion || headerVersion < 1)
        {
            throw new VaultHeaderCorruptException(path, "The vault header has no valid headerVersion.");
        }

        // Refuse what we do not understand BEFORE looking at anything else: a newer schema may
        // mean the remaining fields do not mean what this build thinks they mean.
        if (headerVersion > VaultHeader.CurrentVersion)
        {
            throw UnsupportedVaultHeaderException.ForHeaderVersion(headerVersion, VaultHeader.CurrentVersion);
        }

        if (dto.Kdf is not { } kdf)
        {
            throw new VaultHeaderCorruptException(path, "The vault header has no kdf section.");
        }

        if (string.IsNullOrWhiteSpace(kdf.KdfId))
        {
            throw new VaultHeaderCorruptException(path, "The vault header has no kdfId.");
        }

        if (!string.Equals(kdf.KdfId, KdfParameters.Argon2id, StringComparison.OrdinalIgnoreCase))
        {
            throw UnsupportedVaultHeaderException.ForKdfId(kdf.KdfId);
        }

        if (kdf.MemoryKib is not { } memoryKib ||
            kdf.Iterations is not { } iterations ||
            kdf.Parallelism is not { } parallelism ||
            kdf.KeyLengthBytes is not { } keyLengthBytes ||
            kdf.SaltLengthBytes is not { } saltLengthBytes)
        {
            throw new VaultHeaderCorruptException(path, "The vault header is missing one or more kdf parameters.");
        }

        if (dto.CreatedAtUtc is not { } createdAtUtc)
        {
            throw new VaultHeaderCorruptException(path, "The vault header has no createdAtUtc.");
        }

        if (!Guid.TryParse(dto.VaultId, out var vaultId))
        {
            throw new VaultHeaderCorruptException(path, "The vault header has no valid vaultId.");
        }

        if (string.IsNullOrEmpty(dto.SaltBase64))
        {
            throw new VaultHeaderCorruptException(path, "The vault header has no salt.");
        }

        byte[] salt;
        try
        {
            salt = Convert.FromBase64String(dto.SaltBase64);
        }
        catch (FormatException ex)
        {
            throw new VaultHeaderCorruptException(path, "The vault header salt is not valid Base64.", ex);
        }

        try
        {
            var parameters = new KdfParameters(
                kdf.KdfId,
                memoryKib,
                iterations,
                parallelism,
                keyLengthBytes,
                saltLengthBytes);

            // The VaultHeader constructor also enforces salt length == SaltLengthBytes, which is
            // what catches a truncated or tampered salt.
            return new VaultHeader(headerVersion, vaultId, createdAtUtc, parameters, salt);
        }
        catch (ArgumentException ex)
        {
            // A stored value outside its valid range is corruption. We do NOT substitute a
            // default - deriving with guessed parameters is the silent-corruption failure mode
            // this whole header exists to eliminate.
            throw new VaultHeaderCorruptException(path, "The vault header contains invalid values.", ex);
        }
    }

    /// <inheritdoc />
    public void Write(string vaultDirectory, VaultHeader header)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultDirectory);
        ArgumentNullException.ThrowIfNull(header);

        Directory.CreateDirectory(vaultDirectory);

        var path = GetHeaderPath(vaultDirectory);
        var tempPath = path + TempFileSuffix;

        var dto = new VaultHeaderDto
        {
            Magic = VaultHeader.Magic,
            HeaderVersion = header.HeaderVersion,
            VaultId = header.VaultId.ToString("D"),
            CreatedAtUtc = header.CreatedAtUtc,
            Kdf = new KdfParametersDto
            {
                KdfId = header.KdfParameters.KdfId,
                MemoryKib = header.KdfParameters.MemoryKib,
                Iterations = header.KdfParameters.Iterations,
                Parallelism = header.KdfParameters.Parallelism,
                KeyLengthBytes = header.KdfParameters.KeyLengthBytes,
                SaltLengthBytes = header.KdfParameters.SaltLengthBytes,
            },
            SaltBase64 = Convert.ToBase64String(header.Salt),
        };

        try
        {
            // Write to a sibling temp file first and force it all the way to the physical device
            // (Flush(true), not just the OS page cache) before it becomes the header. If we
            // crashed mid-write onto the real path instead, the vault would be left with a
            // truncated header - i.e. an unopenable vault. The rename is atomic, so a reader
            // sees either the entire old header or the entire new one, never a half of either.
            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, dto, VaultHeaderJsonContext.Default.VaultHeaderDto);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(path))
            {
                // File.Replace keeps the destination's identity and throws if it is missing,
                // hence the branch below for the first-ever write.
                File.Replace(tempPath, path, destinationBackupFileName: null);
            }
            else
            {
                File.Move(tempPath, path, overwrite: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDeleteTempFile(tempPath);
            throw;
        }
    }

    /// <inheritdoc />
    public VaultHeader Create(string vaultDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultDirectory);

        var salt = RandomNumberGenerator.GetBytes(_defaultParameters.SaltLengthBytes);
        var header = new VaultHeader(
            VaultHeader.CurrentVersion,
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            _defaultParameters,
            salt);

        Write(vaultDirectory, header);
        return header;
    }

    /// <inheritdoc />
    public VaultHeader ReadOrAdoptLegacy(string vaultDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultDirectory);

        if (Exists(vaultDirectory))
        {
            return Read(vaultDirectory);
        }

        var legacySaltPath = Path.Combine(vaultDirectory, LegacySaltFileName);
        if (!File.Exists(legacySaltPath))
        {
            throw new VaultHeaderNotFoundException(vaultDirectory);
        }

        var legacySalt = File.ReadAllBytes(legacySaltPath);
        if (legacySalt.Length == 0)
        {
            throw new VaultHeaderCorruptException(legacySaltPath, "The legacy salt file is empty.");
        }

        // Pre-TD-05 vaults were always created with the Argon2idV1 profile, so adopting them with
        // those parameters reproduces the same key bit-for-bit. The salt length is taken from the
        // file rather than assumed, so the adopted header round-trips even if that vault's salt
        // is not the current default length.
        var parameters = KdfParameters.Argon2idV1.WithSaltLengthBytes(legacySalt.Length);

        var header = new VaultHeader(
            VaultHeader.CurrentVersion,
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            parameters,
            legacySalt);

        Write(vaultDirectory, header);

        // pine.salt is deliberately LEFT IN PLACE. Pine has zero tolerance for irreversible data
        // operations, and this file is the only copy of the material that unlocks the vault: if
        // anything about the new header were ever wrong, the legacy file is the fallback.
        return header;
    }

    private static string GetHeaderPath(string vaultDirectory) =>
        Path.Combine(vaultDirectory, HeaderFileName);

    private static void TryDeleteTempFile(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort cleanup of a scratch file only. The original failure is about to be
            // rethrown and is the one that matters; masking it with a cleanup error would hide
            // the real cause. No vault state depends on this temp file.
        }
    }
}

/// <summary>Wire format of the header file. Internal so the on-disk shape stays an implementation detail.</summary>
internal sealed class VaultHeaderDto
{
    public string? Magic { get; set; }

    public int? HeaderVersion { get; set; }

    public string? VaultId { get; set; }

    public DateTimeOffset? CreatedAtUtc { get; set; }

    public KdfParametersDto? Kdf { get; set; }

    public string? SaltBase64 { get; set; }
}

/// <summary>Wire format of the KDF section. Every field is nullable so a missing one is detectable.</summary>
internal sealed class KdfParametersDto
{
    public string? KdfId { get; set; }

    public int? MemoryKib { get; set; }

    public int? Iterations { get; set; }

    public int? Parallelism { get; set; }

    public int? KeyLengthBytes { get; set; }

    public int? SaltLengthBytes { get; set; }
}

/// <summary>
/// Source-generated serialization metadata. Reflection-based System.Text.Json would emit
/// trim/AOT analysis warnings, and warnings are build errors in this solution.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(VaultHeaderDto))]
internal sealed partial class VaultHeaderJsonContext : JsonSerializerContext
{
}
