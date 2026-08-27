namespace Pine.Core.Security;

/// <summary>
/// Metadata describing a single vault backup file on disk.
/// </summary>
/// <param name="Path">Absolute path of the backup file.</param>
/// <param name="CreatedAtUtc">When the backup was taken (UTC), derived from the file name.</param>
/// <param name="Reason">Why the backup was taken.</param>
/// <param name="SizeBytes">Size of the backup file in bytes.</param>
/// <param name="SchemaVersionBefore">
/// The last schema (migration) identifier that was applied to the vault at the moment the backup was
/// taken, or <c>null</c> when the vault had no applied schema yet. Note that this is only known to the
/// process that <em>created</em> the backup: enumerating existing backups cannot recover it, because the
/// backup file is encrypted and reading it would require the vault key. Listing therefore reports
/// <c>null</c> here.
/// </param>
public sealed record VaultBackupInfo(
    string Path,
    DateTimeOffset CreatedAtUtc,
    VaultBackupReason Reason,
    long SizeBytes,
    string? SchemaVersionBefore);
