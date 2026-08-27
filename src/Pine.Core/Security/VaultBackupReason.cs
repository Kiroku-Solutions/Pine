namespace Pine.Core.Security;

/// <summary>
/// Why a vault backup was taken. Recorded in the backup file name so the reason
/// survives on disk without any sidecar metadata file.
/// </summary>
public enum VaultBackupReason
{
    /// <summary>Taken automatically immediately before schema migrations are applied.</summary>
    PreMigration = 0,

    /// <summary>Taken because the user explicitly asked to export/back up the vault.</summary>
    Manual = 1
}
