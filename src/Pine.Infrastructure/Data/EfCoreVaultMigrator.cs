using Microsoft.EntityFrameworkCore;
using Pine.Application.Security;
using Pine.Core.Security;
using Microsoft.Extensions.Options;
using Pine.Core.Configuration;

namespace Pine.Infrastructure.Data;

/// <summary>
/// Applies EF Core migrations to the unlocked vault, taking an encrypted pre-migration backup
/// whenever the vault already holds data that a failed migration could destroy.
/// </summary>
/// <remarks>
/// This type replaces the former <c>PineDbInitializer.Migrate</c>, which called
/// <c>Database.Migrate()</c> unconditionally with no backup and no recovery information.
/// EF Core migrations remain the single source of truth for the schema (never EnsureCreated,
/// which cannot evolve an existing database).
/// </remarks>
public sealed class EfCoreVaultMigrator : IVaultMigrator
{
    private readonly PineDbContext _context;
    private readonly IVaultBackupService _backupService;
    private readonly int _backupsToKeep;

    /// <param name="context">The keyed context for the unlocked vault.</param>
    /// <param name="backupService">Produces the pre-migration backup.</param>
    /// <param name="securityOptions">
    /// Carries how many backups survive a prune. It was
    /// <c>VaultBackupService.DefaultBackupsToKeep</c>, a constant whose own doc comment named TD-27
    /// as the reason it was one.
    /// </param>
    public EfCoreVaultMigrator(
        PineDbContext context,
        IVaultBackupService backupService,
        IOptions<PineSecurityOptions> securityOptions)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(backupService);
        ArgumentNullException.ThrowIfNull(securityOptions);

        _context = context;
        _backupService = backupService;
        _backupsToKeep = securityOptions.Value.BackupsToKeep;
    }

    /// <summary>
    /// The vault directory, i.e. the directory that contains the open pine.db. Derived from the
    /// context rather than injected as a separate string so the migrator can never be pointed at
    /// a directory that does not match the database it is about to migrate.
    /// </summary>
    private string VaultDirectory
    {
        get
        {
            var databasePath = _context.Database.GetDbConnection().DataSource;
            if (string.IsNullOrWhiteSpace(databasePath))
            {
                throw new InvalidOperationException(
                    "The vault context has no file-based data source, so it cannot be migrated safely.");
            }

            return Path.GetDirectoryName(Path.GetFullPath(databasePath))
                   ?? throw new InvalidOperationException(
                       $"Could not determine the vault directory for the database '{databasePath}'.");
        }
    }

    /// <inheritdoc />
    public async Task<VaultMigrationResult> MigrateAsync(CancellationToken cancellationToken)
    {
        var pending = (await _context.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false))
            .ToList();

        // Unlock happens constantly, and the overwhelmingly common case is "schema already current".
        // Returning here means a normal unlock performs no writes and burns no disk on backups.
        if (pending.Count == 0)
        {
            return VaultMigrationResult.UpToDate;
        }

        var backupPath = await CreatePreMigrationBackupIfNeededAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await _context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The vault is now in an unknown state. Surface exactly what was being applied and
            // where the recoverable copy lives, instead of a generic "corrupted vault" message.
            throw new VaultMigrationException(
                $"Failed to apply {pending.Count} pending vault migration(s): {string.Join(", ", pending)}.",
                pending,
                backupPath,
                ex);
        }

        if (backupPath is not null)
        {
            await PruneQuietlyAsync(cancellationToken).ConfigureAwait(false);
        }

        return new VaultMigrationResult(true, pending, backupPath);
    }

    /// <summary>
    /// Backs the vault up before migrating, unless this is a first run.
    /// </summary>
    /// <remarks>
    /// First run: a brand-new vault has no applied migrations (no __EFMigrationsHistory table) and
    /// its pine.db may not even exist yet. There is no user data to lose, so a backup would only
    /// copy an empty database, and backing up a file that does not exist can fail outright.
    /// Skipping it here is the difference between "no data to protect" and "not protecting data".
    /// </remarks>
    private async Task<string?> CreatePreMigrationBackupIfNeededAsync(CancellationToken cancellationToken)
    {
        var applied = await _context.Database.GetAppliedMigrationsAsync(cancellationToken).ConfigureAwait(false);
        if (!applied.Any())
        {
            return null;
        }

        var backup = await _backupService
            .CreateBackupAsync(VaultDirectory, VaultBackupReason.PreMigration, cancellationToken)
            .ConfigureAwait(false);

        return backup.Path;
    }

    /// <summary>
    /// Prunes stale backups after a successful migration. A failure to delete an OLD backup is
    /// deliberately not propagated: the schema upgrade already succeeded, and reporting it as a
    /// migration failure would send the user down a recovery path they do not need. The worst
    /// case is that a few extra backups stay on disk, which loses nothing.
    /// </summary>
    private async Task PruneQuietlyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _backupService
                .PruneAsync(VaultDirectory, _backupsToKeep, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (VaultBackupFailedException)
        {
            // Intentionally ignored; see the summary above.
        }
    }
}
