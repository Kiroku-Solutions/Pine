using Pine.Core.Security;

namespace Pine.Application.Security;

/// <summary>
/// Brings the unlocked vault's schema up to date, taking a pre-migration backup first
/// whenever there is existing data that a failed migration could destroy.
/// </summary>
public interface IVaultMigrator
{
    /// <summary>
    /// Applies any pending schema migrations. Cheap and safe to call on every unlock:
    /// when nothing is pending it performs no writes and takes no backup.
    /// </summary>
    /// <exception cref="VaultMigrationException">Migration failed; the exception carries the backup path.</exception>
    Task<VaultMigrationResult> MigrateAsync(CancellationToken cancellationToken);
}
