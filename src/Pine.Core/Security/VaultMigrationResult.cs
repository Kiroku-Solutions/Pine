namespace Pine.Core.Security;

/// <summary>
/// Outcome of a vault schema migration attempt.
/// </summary>
/// <param name="MigrationsApplied">True when at least one migration was applied.</param>
/// <param name="AppliedMigrationIds">The migration identifiers that were applied; empty when there was nothing to do.</param>
/// <param name="BackupPath">
/// Path of the pre-migration backup, or <c>null</c> when no backup was needed
/// (nothing to migrate, or a brand-new vault that contains no data yet).
/// </param>
public sealed record VaultMigrationResult(
    bool MigrationsApplied,
    IReadOnlyList<string> AppliedMigrationIds,
    string? BackupPath)
{
    /// <summary>The "nothing to do" result: schema already current, no backup taken.</summary>
    public static VaultMigrationResult UpToDate { get; } =
        new(false, Array.Empty<string>(), null);
}
