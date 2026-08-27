namespace Pine.Core.Security;

/// <summary>
/// Thrown when applying vault schema migrations failed. Deliberately carries the recovery
/// information as typed properties so the UI can tell the user exactly which backup file to
/// restore instead of showing a generic "corrupted vault" message.
/// </summary>
public sealed class VaultMigrationException : Exception
{
    public VaultMigrationException(
        string message,
        IReadOnlyList<string> pendingMigrations,
        string? backupPath,
        Exception innerException)
        : base(message, innerException)
    {
        PendingMigrations = pendingMigrations;
        BackupPath = backupPath;
    }

    /// <summary>The migrations that were pending when the failure occurred.</summary>
    public IReadOnlyList<string> PendingMigrations { get; }

    /// <summary>
    /// The pre-migration backup that can be restored, or <c>null</c> when no backup existed
    /// (a brand-new vault, where there was no data to lose).
    /// </summary>
    public string? BackupPath { get; }
}
