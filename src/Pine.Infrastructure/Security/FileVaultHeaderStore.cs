using System.Text.Json;
using System.Text.Json.Serialization;
using Pine.Core.Security;

namespace Pine.Infrastructure.Security;

/// <summary>
/// Stores the vault header as <c>pine.vault.json</c> inside the vault directory.
/// </summary>
/// <remarks>
/// <para>
/// THIS CLASS IS PURE PERSISTENCE, AND IT BECAME SO WITH TD-06. It used to mint headers
/// (<c>Create</c>) and adopt pre-header vaults (<c>ReadOrAdoptLegacy</c>), which is why it once
/// needed a default KDF profile injected into its constructor. Neither survives envelope encryption:
/// a header now carries a <see cref="VaultKeyWrap"/>, and producing one requires the master
/// password - which a file store has no business receiving, holding, or being tempted to log.
/// Minting moved to <see cref="IVaultKeyService.CreateVault"/>; this type only reads and writes what
/// it is handed, and needs no configuration at all.
/// </para>
/// <para>
/// A HEADER IS NEVER INVENTED, ONLY REJECTED. Every branch below that cannot fully trust the file
/// throws. Substituting a default for a missing or unreadable field would derive the wrong key, and
/// the only symptom - deep inside SQLCipher, long after the fact - would look exactly like data loss.
/// </para>
/// </remarks>
public sealed class FileVaultHeaderStore : IVaultHeaderStore
{
    /// <summary>Name of the versioned header file.</summary>
    public const string HeaderFileName = "pine.vault.json";

    /// <summary>Name of the pre-TD-05 raw salt file that older vaults still carry.</summary>
    public const string LegacySaltFileName = "pine.salt";

    private const string TempFileSuffix = ".tmp";

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
            // A directory with no header but WITH pine.salt is not an empty directory - it is a
            // pre-header vault whose database is real and whose key is derived straight from that
            // salt. Reporting it as "not found" would invite the caller to create a new vault on top
            // of it, which is the one irreversible mistake available here.
            if (File.Exists(Path.Combine(vaultDirectory, LegacySaltFileName)))
            {
                throw new LegacyVaultFormatException(vaultDirectory, foundHeaderVersion: null);
            }

            throw new VaultHeaderNotFoundException(vaultDirectory);
        }

        // IOException / UnauthorizedAccessException are deliberately allowed to propagate: a
        // locked or unreadable file is an environment problem, NOT header corruption, and
        // conflating the two would push the caller towards "recreate the vault".
        return Parse(File.ReadAllText(path), path, vaultDirectory);
    }

    internal static VaultHeader Parse(string json, string path, string vaultDirectory)
    {
        VaultHeaderDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize(json, VaultHeaderJsonContext.Default.VaultHeaderDto);
        }
        catch (JsonException ex)
        {
            throw new VaultHeaderCorruptException(path, "The vault header is not valid JSON.", ex);
        }

        return FromDto(dto, path, vaultDirectory);
    }

    internal static VaultHeader FromDto(VaultHeaderDto? dto, string path, string vaultDirectory)
    {
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

        // The same discipline pointing the other way. A version-1 header is perfectly well-formed;
        // it simply describes a vault whose database key IS the Argon2id output, with no wrap to
        // read. Continuing would fail on a missing keyWrap section and report "corrupt", sending the
        // user hunting for a backup of a file that is not damaged at all.
        if (headerVersion < VaultHeader.MinimumSupportedVersion)
        {
            throw new LegacyVaultFormatException(vaultDirectory, headerVersion);
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

        var keyWrap = ReadKeyWrap(path, dto.KeyWrap);

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
            return new VaultHeader(headerVersion, vaultId, createdAtUtc, parameters, salt, keyWrap);
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

        var dto = ToDto(header);

        try
        {
            // Write to a sibling temp file first and force it all the way to the physical device
            // (Flush(true), not just the OS page cache) before it becomes the header. If we
            // crashed mid-write onto the real path instead, the vault would be left with a
            // truncated header - i.e. an unopenable vault. The rename is atomic, so a reader
            // sees either the entire old header or the entire new one, never a half of either.
            // With envelope encryption this is what makes a password change safe: a crash mid-change
            // leaves the OLD wrap intact and the OLD password still working.
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

    /// <summary>
    /// Rebuilds the key wrap from its wire form, treating every defect as corruption.
    /// </summary>
    /// <remarks>
    /// A missing field, unusable Base64, and a length or algorithm the
    /// <see cref="VaultKeyWrap"/> constructor rejects are all the same class of problem: bytes that
    /// cannot be the wrap this vault was sealed with. Each gets its own message so a support log says
    /// which field failed, but none of them is recoverable - there is no defaulting a ciphertext.
    /// </remarks>
    internal static VaultHeaderDto ToDto(VaultHeader header)
    {
        return new VaultHeaderDto
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

            // The wrapped data key is written as plain Base64 next to everything it is bound to.
            // It is safe here precisely because it is a ciphertext: without the auth key that
            // Argon2id derives from the master password, these bytes are inert.
            KeyWrap = new VaultKeyWrapDto
            {
                Algorithm = header.KeyWrap.Algorithm,
                NonceBase64 = Convert.ToBase64String(header.KeyWrap.Nonce),
                CiphertextBase64 = Convert.ToBase64String(header.KeyWrap.Ciphertext),
                TagBase64 = Convert.ToBase64String(header.KeyWrap.Tag),
            },
        };
    }

    private static VaultKeyWrap ReadKeyWrap(string path, VaultKeyWrapDto? dto)
    {
        if (dto is null)
        {
            throw new VaultHeaderCorruptException(path, "The vault header has no keyWrap section.");
        }

        if (string.IsNullOrWhiteSpace(dto.Algorithm))
        {
            throw new VaultHeaderCorruptException(path, "The vault header keyWrap has no algorithm.");
        }

        var nonce = DecodeRequiredBase64(path, dto.NonceBase64, "keyWrap nonce");
        var ciphertext = DecodeRequiredBase64(path, dto.CiphertextBase64, "keyWrap ciphertext");
        var tag = DecodeRequiredBase64(path, dto.TagBase64, "keyWrap tag");

        try
        {
            return new VaultKeyWrap(dto.Algorithm, nonce, ciphertext, tag);
        }
        catch (ArgumentException ex)
        {
            throw new VaultHeaderCorruptException(
                path,
                "The vault header keyWrap contains invalid values.",
                ex);
        }
    }

    /// <summary>Decodes a required Base64 field, or reports precisely which one was unusable.</summary>
    private static byte[] DecodeRequiredBase64(string path, string? value, string fieldDescription)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new VaultHeaderCorruptException(path, $"The vault header has no {fieldDescription}.");
        }

        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException ex)
        {
            throw new VaultHeaderCorruptException(
                path,
                $"The vault header {fieldDescription} is not valid Base64.",
                ex);
        }
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

    public VaultKeyWrapDto? KeyWrap { get; set; }
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
/// Wire format of the wrapped data key. Every field is nullable, for the same reason as the KDF
/// section: a field that is absent from the file must be distinguishable from one that is present
/// and empty, and a non-nullable string would silently turn the first case into the second.
/// </summary>
internal sealed class VaultKeyWrapDto
{
    public string? Algorithm { get; set; }

    public string? NonceBase64 { get; set; }

    public string? CiphertextBase64 { get; set; }

    public string? TagBase64 { get; set; }
}

/// <summary>
/// Source-generated serialization metadata. Reflection-based System.Text.Json would emit
/// trim/AOT analysis warnings, and warnings are build errors in this solution.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(VaultHeaderDto))]
[JsonSerializable(typeof(VaultKeyWrapDto))]
[JsonSerializable(typeof(RekeyJournalDto))]
internal sealed partial class VaultHeaderJsonContext : JsonSerializerContext
{
}
