using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pine.Core.Security;

namespace Pine.Infrastructure.Data;

/// <summary>
/// Produces encrypted point-in-time copies of the vault database using SQLite VACUUM INTO.
/// </summary>
/// <remarks>
/// <para>
/// WHY VACUUM INTO: the statement is executed on the <em>already-keyed</em> SQLCipher connection
/// owned by <see cref="PineDbContext"/>. SQLCipher writes the destination database through the
/// same cipher context as the source, so the copy is encrypted with the very same key and the
/// vault contents NEVER touch the disk in plaintext. A naive File.Copy of pine.db would also stay
/// encrypted, but it can capture a torn page image while a write is in flight; VACUUM INTO always
/// produces a transactionally consistent database. This encryption property is proven empirically
/// by VaultBackupServiceTests.CreateBackupAsync_ShouldProduceBackupEncryptedWithSameKey, which
/// opens the produced file with the correct key and then with a wrong key.
/// </para>
/// <para>
/// A partially written destination is always deleted before an exception leaves this class:
/// a truncated backup is worse than no backup, because it still looks like a backup.
/// </para>
/// </remarks>
public sealed partial class VaultBackupService : IVaultBackupService
{
    /// <summary>
    /// How many backups are retained by default. There is no user-facing configuration system
    /// yet, so this is a constant; making retention configurable is tracked separately as TD-27.
    /// </summary>
    public const int DefaultBackupsToKeep = 5;

    /// <summary>Sub-directory of the vault directory that holds the backups.</summary>
    public const string BackupsDirectoryName = "backups";

    /// <summary>The database file name inside a vault directory.</summary>
    public const string DatabaseFileName = "pine.db";

    private const string TimestampFormat = @"yyyyMMdd\THHmmss";
    private const int SqliteFull = 13;      // SQLITE_FULL: database or disk is full
    private const int SqliteCantOpen = 14;  // SQLITE_CANTOPEN: unable to open the destination file
    private const int SqliteIoErr = 10;     // SQLITE_IOERR: host I/O error while writing

    private readonly PineDbContext _context;

    public VaultBackupService(PineDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <summary>
    /// Matches pine-20260826T101500Z-PreMigration.db and its de-duplicated siblings
    /// pine-20260826T101500Z-PreMigration-1.db, -2.db, ...
    /// </summary>
    [GeneratedRegex(@"^pine-(?<ts>\d{8}T\d{6})Z-(?<reason>[A-Za-z]+)(?:-(?<seq>\d+))?\.db$",
        RegexOptions.CultureInvariant)]
    private static partial Regex BackupFileNamePattern();

    /// <inheritdoc />
    public async Task<VaultBackupInfo> CreateBackupAsync(
        string vaultDirectory,
        VaultBackupReason reason,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultDirectory);
        cancellationToken.ThrowIfCancellationRequested();

        // Programming-error guard: the context we back up must be the vault we were pointed at.
        // Validated before anything is created on disk so a wiring mistake has no side effects.
        var connection = _context.Database.GetDbConnection();
        var vaultRoot = Path.GetFullPath(vaultDirectory);
        EnsureDatabaseBelongsToVault(connection, vaultRoot, nameof(vaultDirectory));

        // Read the current schema version first: after the migration runs it is no longer "before".
        var schemaVersionBefore = await GetCurrentSchemaVersionAsync(cancellationToken).ConfigureAwait(false);

        var backupsDirectory = CreateBackupsDirectory(vaultRoot);
        var createdAtUtc = DateTimeOffset.UtcNow;
        var destination = ResolveFreeDestinationPath(backupsDirectory, createdAtUtc, reason);

        var openedByUs = false;
        try
        {
            if (connection.State != ConnectionState.Open)
            {
                await _context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
                openedByUs = true;
            }

            // VACUUM INTO runs on the keyed connection, so SQLCipher encrypts the destination with
            // the same key. The path is bound as a parameter: SQLite evaluates the INTO target as
            // an expression, which keeps quoting and escaping out of the picture.
            await using var command = connection.CreateCommand();
            command.CommandText = "VACUUM INTO $destination;";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "$destination";
            parameter.Value = destination;
            command.Parameters.Add(parameter);

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await DeletePartialBackupAsync(destination).ConfigureAwait(false);
            throw;
        }
        catch (SqliteException ex)
        {
            await DeletePartialBackupAsync(destination).ConfigureAwait(false);
            throw new VaultBackupFailedException(DescribeSqliteFailure(ex, destination), destination, ex);
        }
        catch (IOException ex)
        {
            await DeletePartialBackupAsync(destination).ConfigureAwait(false);
            throw new VaultBackupFailedException(
                $"Failed to write the vault backup to '{destination}'. The disk may be full or the file may be in use.",
                destination,
                ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            await DeletePartialBackupAsync(destination).ConfigureAwait(false);
            throw new VaultBackupFailedException(
                $"Access to the vault backup path '{destination}' was denied.", destination, ex);
        }
        finally
        {
            if (openedByUs)
            {
                await _context.Database.CloseConnectionAsync().ConfigureAwait(false);
            }
        }

        var sizeBytes = new FileInfo(destination).Length;
        return new VaultBackupInfo(destination, createdAtUtc, reason, sizeBytes, schemaVersionBefore);
    }

    /// <inheritdoc />
    public IReadOnlyList<VaultBackupInfo> ListBackups(string vaultDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultDirectory);

        var backupsDirectory = Path.Combine(Path.GetFullPath(vaultDirectory), BackupsDirectoryName);
        if (!Directory.Exists(backupsDirectory))
        {
            return Array.Empty<VaultBackupInfo>();
        }

        var found = new List<(VaultBackupInfo Info, int Sequence)>();
        foreach (var path in Directory.EnumerateFiles(backupsDirectory, "pine-*.db"))
        {
            // Files we did not write (or that a user renamed) are not backups we manage.
            if (TryDescribeBackup(path, out var info, out var sequence))
            {
                found.Add((info, sequence));
            }
        }

        // Newest first. Within the same second the de-duplication sequence is the tie-breaker,
        // and a higher sequence was necessarily written later.
        return found
            .OrderByDescending(x => x.Info.CreatedAtUtc)
            .ThenByDescending(x => x.Sequence)
            .Select(x => x.Info)
            .ToList();
    }

    /// <inheritdoc />
    public Task PruneAsync(string vaultDirectory, int keepMostRecent, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultDirectory);
        ArgumentOutOfRangeException.ThrowIfLessThan(keepMostRecent, 1);
        cancellationToken.ThrowIfCancellationRequested();

        var backups = ListBackups(vaultDirectory);
        if (backups.Count <= keepMostRecent)
        {
            return Task.CompletedTask;
        }

        var failed = new List<string>();
        Exception? firstFailure = null;

        foreach (var stale in backups.Skip(keepMostRecent))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                File.Delete(stale.Path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed.Add(stale.Path);
                firstFailure ??= ex;
            }
        }

        if (failed.Count > 0)
        {
            throw new VaultBackupFailedException(
                $"Failed to delete {failed.Count} stale vault backup(s): {string.Join(", ", failed)}.",
                failed[0],
                firstFailure);
        }

        return Task.CompletedTask;
    }

    private static void EnsureDatabaseBelongsToVault(DbConnection connection, string vaultRoot, string parameterName)
    {
        var databasePath = connection.DataSource;
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException(
                "The vault database context has no file-based data source, so it cannot be backed up.",
                parameterName);
        }

        var fullDatabasePath = Path.GetFullPath(databasePath);
        var relative = Path.GetRelativePath(vaultRoot, fullDatabasePath);
        if (Path.IsPathRooted(relative) || relative.StartsWith("..", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"The open vault database '{fullDatabasePath}' is not inside the vault directory '{vaultRoot}'.",
                parameterName);
        }
    }

    private async Task<string?> GetCurrentSchemaVersionAsync(CancellationToken cancellationToken)
    {
        // Migration identifiers are timestamp-prefixed, so ordinal order is chronological order.
        // A vault with no __EFMigrationsHistory table yields an empty sequence, i.e. null.
        var applied = await _context.Database.GetAppliedMigrationsAsync(cancellationToken).ConfigureAwait(false);
        return applied.OrderBy(id => id, StringComparer.Ordinal).LastOrDefault();
    }

    private static string CreateBackupsDirectory(string vaultRoot)
    {
        var backupsDirectory = Path.Combine(vaultRoot, BackupsDirectoryName);
        try
        {
            Directory.CreateDirectory(backupsDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new VaultBackupFailedException(
                $"Could not create the vault backup directory '{backupsDirectory}'.", backupsDirectory, ex);
        }

        return backupsDirectory;
    }

    private static string ResolveFreeDestinationPath(
        string backupsDirectory,
        DateTimeOffset createdAtUtc,
        VaultBackupReason reason)
    {
        var stem = $"pine-{createdAtUtc.UtcDateTime.ToString(TimestampFormat, CultureInfo.InvariantCulture)}Z-{reason}";
        var candidate = Path.Combine(backupsDirectory, $"{stem}.db");

        // Two backups within the same second must not overwrite each other: an existing backup is
        // never destroyed, the newcomer deterministically takes the next free -1, -2, ... suffix.
        var sequence = 0;
        while (File.Exists(candidate))
        {
            sequence++;
            candidate = Path.Combine(backupsDirectory, $"{stem}-{sequence}.db");
        }

        return candidate;
    }

    private static bool TryDescribeBackup(string path, out VaultBackupInfo info, out int sequence)
    {
        info = null!;
        sequence = 0;

        var match = BackupFileNamePattern().Match(Path.GetFileName(path));
        if (!match.Success)
        {
            return false;
        }

        if (!DateTimeOffset.TryParseExact(
                match.Groups["ts"].Value,
                TimestampFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var createdAtUtc))
        {
            return false;
        }

        if (!Enum.TryParse<VaultBackupReason>(match.Groups["reason"].Value, ignoreCase: false, out var reason)
            || !Enum.IsDefined(reason))
        {
            return false;
        }

        if (match.Groups["seq"].Success
            && !int.TryParse(match.Groups["seq"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out sequence))
        {
            return false;
        }

        // SchemaVersionBefore is deliberately null here: it lives inside the encrypted backup and
        // recovering it would require the vault key, which listing does not (and should not) need.
        info = new VaultBackupInfo(path, createdAtUtc, reason, new FileInfo(path).Length, null);
        return true;
    }

    private static string DescribeSqliteFailure(SqliteException ex, string destination) => ex.SqliteErrorCode switch
    {
        SqliteFull => $"There is not enough free disk space to write the vault backup '{destination}'.",
        SqliteCantOpen => $"The vault backup file '{destination}' could not be opened for writing.",
        SqliteIoErr => $"A disk I/O error occurred while writing the vault backup '{destination}'.",
        _ => $"SQLite failed to write the vault backup '{destination}' (error {ex.SqliteErrorCode})."
    };

    /// <summary>
    /// Removes a half-written backup. Retried briefly because SQLite may still be releasing the
    /// destination handle; cleanup failures must not mask the original error.
    /// </summary>
    private static async Task DeletePartialBackupAsync(string destination)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (File.Exists(destination))
                {
                    File.Delete(destination);
                }

                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _ = ex;
                await Task.Delay(TimeSpan.FromMilliseconds(50), CancellationToken.None).ConfigureAwait(false);
            }
        }
    }
}
