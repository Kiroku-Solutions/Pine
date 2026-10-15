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
    Manual = 1,

    /// <summary>
    /// Taken automatically immediately before a backup is restored over the vault, so the state
    /// being replaced is still on disk afterwards.
    /// </summary>
    /// <remarks>
    /// This is what makes a restore reversible rather than a one-way door: undoing a restore is
    /// restoring the <c>PreRestore</c> copy the restore itself left behind. It is a distinct reason
    /// rather than a <see cref="Manual"/> backup because the two answer different questions in the
    /// list the user reads — "I asked for this" against "the application saved this for me before
    /// doing what I asked" — and because retention must never treat them alike.
    /// </remarks>
    PreRestore = 2
}
