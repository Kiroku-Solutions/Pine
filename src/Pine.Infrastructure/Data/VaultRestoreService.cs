using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pine.Core.Security;

namespace Pine.Infrastructure.Data;

/// <summary>
/// Restores one of the vault's own backups over <c>pine.db</c> (TD-56, plan item 1.1).
/// </summary>
/// <remarks>
/// <para>
/// A RESTORE IS THE ONLY DESTRUCTIVE OPERATION IN PINE THAT A USER CAN ASK FOR ON PURPOSE. Everything
/// else either adds a row or voids one, and both are recoverable by reading the ledger. This replaces
/// the ledger. So the order below is fixed, and each step exists because of a specific way the
/// operation can hurt someone:
/// </para>
/// <list type="number">
///   <item><description><b>The backup must be one of this vault's own.</b> Anything else is a file
///   the application was handed, and copying an arbitrary file over someone's ledger on their say-so
///   is not a feature. This is a containment check on the path, done before the file is read.</description></item>
///   <item><description><b>The backup must open with this vault's key, and look like a Pine vault.</b>
///   SQLCipher gives no "wrong password" error at open time — it reports a malformed database on the
///   first read — so the probe is a real query, not a connection. This is what stops a truncated,
///   corrupt or foreign file from becoming the ledger.</description></item>
///   <item><description><b>The backup must not be from a newer Pine.</b> EF Core applies PENDING
///   migrations on open and is silent about migrations it has never heard of, so a vault written by a
///   later build opens without complaint and is then written to by a model that does not match it.
///   The comparison is against this assembly's own migration list.</description></item>
///   <item><description><b>The current vault is copied first.</b> `VACUUM INTO`, through the same
///   service that writes every other backup, tagged <see cref="VaultBackupReason.PreRestore"/>. This
///   is what makes the restore reversible: undoing it is restoring that file. If this copy cannot be
///   taken, the restore does not happen.</description></item>
///   <item><description><b>Then, and only then, the file is replaced.</b></description></item>
/// </list>
/// <para>
/// THE KEY IS NEVER PASSED IN. It is read from the live context's own connection string, with the
/// data source swapped for the backup path. That is deliberate: a restore that accepted a key could
/// be asked to open a backup with the WRONG key, and the only correct key here is the one the vault
/// is already open with — the backup was written by <c>VACUUM INTO</c> on that same keyed connection.
/// It also keeps the key out of one more signature.
/// </para>
/// <para>
/// THE REPLACEMENT IS NOT A COPY OVER THE LIVE FILE. It is a copy to a neighbouring temporary file
/// followed by a move, so an interrupted write leaves the vault untouched rather than truncated. See
/// <see cref="ReplaceDatabase"/> — that method's comment is the one worth reading, because the naive
/// spelling was written first and it is the failure this class exists to prevent.
/// </para>
/// <para>
/// WHY THE SIDECARS ARE DELETED RATHER THAN COPIED. A SQLite database in WAL mode is three files:
/// <c>pine.db</c>, <c>pine.db-wal</c> and <c>pine.db-shm</c>. The backup is a single self-contained
/// file, because that is what <c>VACUUM INTO</c> produces. Leaving the old write-ahead log beside the
/// new database would hand SQLite a log belonging to a different database — the one case where this
/// operation could produce a vault that is neither the old contents nor the new ones.
/// </para>
/// </remarks>
public sealed class VaultRestoreService : IVaultRestoreService
{
    private readonly PineDbContext _context;
    private readonly IVaultBackupService _backupService;
    private readonly TimeProvider _timeProvider;

    public VaultRestoreService(
        PineDbContext context,
        IVaultBackupService backupService,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(backupService);

        _context = context;
        _backupService = backupService;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task<VaultRestoreResult> RestoreAsync(
        string vaultDirectory,
        string backupPath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(backupPath);
        cancellationToken.ThrowIfCancellationRequested();

        var vaultRoot = Path.GetFullPath(vaultDirectory);
        var databasePath = Path.Combine(vaultRoot, VaultBackupService.DatabaseFileName);
        var backupsDirectory = Path.Combine(vaultRoot, VaultBackupService.BackupsDirectoryName);
        var source = Path.GetFullPath(backupPath);

        // Step 1: containment. Compared as directories, so `backups-elsewhere` cannot pass as
        // `backups`, and a path that climbs out with `..` is resolved before it is compared.
        if (!string.Equals(
                Path.GetDirectoryName(source),
                Path.GetFullPath(backupsDirectory).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new VaultRestoreRefusedException(
                $"Refused to restore '{source}': it is not inside this vault's backups directory '{backupsDirectory}'.",
                "That file is not one of this vault's backups, so it was not restored. Your vault is unchanged.");
        }

        if (!File.Exists(source))
        {
            throw new VaultRestoreRefusedException(
                $"Refused to restore '{source}': the file no longer exists.",
                "That backup is no longer on disk, so nothing was restored. Your vault is unchanged.");
        }

        if (!File.Exists(databasePath))
        {
            throw new VaultRestoreRefusedException(
                $"Refused to restore into '{vaultRoot}': it holds no {VaultBackupService.DatabaseFileName}.",
                "The vault could not be found on disk, so nothing was restored.");
        }

        // Step 2 and 3: the backup must open with this vault's key and must not be from a newer
        // build. Both are read through one connection, which is closed before anything is replaced.
        var appliedInBackup = await ReadAppliedMigrationsAsync(source, cancellationToken).ConfigureAwait(false);

        var known = _context.Database.GetMigrations().ToHashSet(StringComparer.Ordinal);
        var unknown = appliedInBackup.Where(id => !known.Contains(id)).OrderBy(id => id, StringComparer.Ordinal).ToArray();

        if (unknown.Length > 0)
        {
            throw new VaultRestoreRefusedException(
                $"Refused to restore '{source}': it carries {unknown.Length} migration(s) this build does not know ({string.Join(", ", unknown)}).",
                "That backup was made by a newer version of Pine than the one you are running. "
                + "Restoring it could damage your data, so nothing was changed. Update Pine and try again.");
        }

        var backupCreatedAtUtc = DescribeCreationTime(source);

        // Step 4: the undo. Taken through the same service that writes every other backup, so a
        // restore's safety copy is a real backup that can itself be listed and restored.
        var safety = await _backupService
            .CreateBackupAsync(vaultRoot, VaultBackupReason.PreRestore, cancellationToken)
            .ConfigureAwait(false);

        // Step 5: the point of no return. Cancellation is not honoured past here on purpose — see
        // the interface contract. Every handle on the vault has to be gone first: Microsoft.Data
        // .Sqlite pools connections, and on Windows a pooled handle keeps the file locked even
        // though no context is using it.
        //
        // ClearPool, NOT ClearAllPools, AND THE DIFFERENCE WAS MEASURED. The first version cleared
        // every pool in the process, which is a bigger hammer than this needs — it releases handles
        // to databases that have nothing to do with this vault. In the integration suite, where
        // xUnit runs test classes in parallel and several of them hold pooled connections to their
        // own temp vaults, that showed up as two unrelated migration tests failing in a full run and
        // passing in isolation: the same shape as TD-140, made worse by this file. Clearing the pool
        // for THIS connection string releases exactly the handles on the file about to be replaced.
        await _context.Database.CloseConnectionAsync().ConfigureAwait(false);

        if (_context.Database.GetDbConnection() is SqliteConnection sqlite)
        {
            SqliteConnection.ClearPool(sqlite);
        }

        ReplaceDatabase(source, databasePath);

        DeleteSidecar(databasePath + "-wal");
        DeleteSidecar(databasePath + "-shm");

        return new VaultRestoreResult(
            source,
            backupCreatedAtUtc,
            safety.Path,
            _timeProvider.GetUtcNow());
    }

    /// <summary>
    /// Puts the backup's bytes in place of the vault, without ever leaving a half-written vault.
    /// </summary>
    /// <remarks>
    /// <para>
    /// COPY TO A NEIGHBOUR, THEN REPLACE. THE OBVIOUS SPELLING IS WRONG, AND AN ADVERSARIAL PASS
    /// CAUGHT IT. This was <c>File.Copy(source, databasePath, overwrite: true)</c>, which opens the
    /// user's live vault for writing and streams into it. A disk that fills, a process that is
    /// killed, a power cut or an antivirus lock partway through then leaves <c>pine.db</c> as a
    /// truncated hybrid: not the old vault, which has already been overwritten, and not the backup,
    /// whose copy never finished. That is the one outcome this whole class is arranged to prevent,
    /// and the class comment claimed it could not happen while the code allowed it.
    /// </para>
    /// <para>
    /// The copy now lands on a temporary file in the same directory — the same volume, so the
    /// replace is a metadata operation rather than a second copy — and only a COMPLETE temporary
    /// file is moved over the vault. An interrupted copy leaves the vault untouched and a stray
    /// <c>.restore-tmp</c> beside it. A failure in either step is reported as a refusal, and the
    /// sentence the user reads — "nothing was changed and your vault is as it was" — is then TRUE,
    /// which it was not before.
    /// </para>
    /// <para>
    /// NO TEST HERE DISTINGUISHES THIS FROM THE NAIVE SPELLING, AND THAT IS STATED RATHER THAN LEFT
    /// TO BE FOUND. Reverting these three lines to <c>File.Copy(source, databasePath, overwrite:
    /// true)</c> leaves the whole suite green, because every failure the suite can INDUCE — a
    /// read-only destination, a locked file, a missing source — fails before either version has
    /// written a byte, so both end with the vault intact. The difference appears only when a write
    /// that has begun does not finish, which needs the process to die mid-copy. The two tests around
    /// this method assert what they can honestly assert: that a failed replacement refuses, leaves
    /// the vault readable and unchanged, and leaves no staging file behind.
    /// </para>
    /// </remarks>
    private static void ReplaceDatabase(string source, string databasePath)
    {
        var staging = databasePath + ".restore-tmp";

        try
        {
            File.Copy(source, staging, overwrite: true);

            // Windows maps this to MoveFileEx with MOVEFILE_REPLACE_EXISTING: the directory entry is
            // swapped, so the vault is either entirely the old file or entirely the new one.
            File.Move(staging, databasePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DeleteSidecar(staging);

            throw new VaultRestoreRefusedException(
                $"Failed to put '{source}' in place of '{databasePath}': {ex.Message}",
                "The backup could not be written into place, so nothing was changed and your vault "
                + "is as it was. Check that the disk is not full and try again.",
                ex);
        }
    }

    /// <summary>
    /// Opens the backup with this vault's key and reads its applied-migration list.
    /// </summary>
    /// <remarks>
    /// THE PROBE IS A QUERY, NOT A CONNECTION, AND THAT IS NOT PEDANTRY. SQLCipher does not verify a
    /// key at open time; it decrypts the first page when something reads, and a wrong key surfaces as
    /// "file is not a database". So opening the connection proves nothing, and reading proves
    /// everything: a wrong key, a truncated file, a file that is not SQLite at all and a SQLite file
    /// that is not a Pine vault all fail here, before any of them can become the ledger.
    /// </remarks>
    private async Task<IReadOnlyList<string>> ReadAppliedMigrationsAsync(
        string backupPath,
        CancellationToken cancellationToken)
    {
        var live = _context.Database.GetDbConnection();

        var connectionString = new SqliteConnectionStringBuilder(live.ConnectionString)
        {
            DataSource = backupPath,

            // Read-only, because nothing here is allowed to modify the backup. It also means a
            // missing or unreadable file fails rather than being created as an empty database,
            // which ReadWriteCreate would do and which would then pass every check below.
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString();

        await using var connection = new SqliteConnection(connectionString);

        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory;";

            var applied = new List<string>();

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                applied.Add(reader.GetString(0));
            }

            if (applied.Count == 0)
            {
                throw new VaultRestoreRefusedException(
                    $"Refused to restore '{backupPath}': it has an empty __EFMigrationsHistory, so it is not a Pine vault.",
                    "That file is not a usable Pine backup, so nothing was restored. Your vault is unchanged.");
            }

            return applied;
        }
        catch (SqliteException ex)
        {
            // The one place a wrong key, a corrupt file and a foreign file all arrive. They are
            // reported as one sentence on purpose: the user cannot act differently on any of them,
            // and the engineer's version below carries the SQLite error itself.
            throw new VaultRestoreRefusedException(
                $"Refused to restore '{backupPath}': it could not be read with this vault's key ({ex.Message}).",
                "That backup could not be opened with this vault's password, or the file is damaged. "
                + "Nothing was restored and your vault is unchanged.",
                ex);
        }
        finally
        {
            // AND RELEASE THE POOLED HANDLE ON THE BACKUP FILE (TD-140).
            //
            // Reading a backup to verify it leaves a pooled connection to that FILE, under its own
            // connection string, which `await using` returns to the pool rather than closing. The
            // consequence is not theoretical: on Windows the file then cannot be deleted, and
            // VaultBackupService.PruneAsync deletes backups -- so a restore, INCLUDING A REFUSED
            // ONE, could make pruning that backup fail for the rest of the process.
            //
            // This was invisible until TD-140 was diagnosed, because the integration harness called
            // the process-wide SqliteConnection.ClearAllPools() in teardown and cleaned up after
            // this method on every run. Scoping that call to each harness's own database is what
            // exposed it: nine vault directories survived a full suite run, each held open by a
            // backup file this method had opened.
            SqliteConnection.ClearPool(connection);
        }
    }

    /// <summary>
    /// When the backup was taken, from the service that named it; the file's own timestamp otherwise.
    /// </summary>
    private DateTimeOffset DescribeCreationTime(string backupPath)
    {
        var directory = Path.GetDirectoryName(backupPath);

        if (directory is not null)
        {
            var listed = _backupService
                .ListBackups(Path.GetFullPath(Path.Combine(directory, "..")))
                .FirstOrDefault(backup => string.Equals(backup.Path, backupPath, StringComparison.OrdinalIgnoreCase));

            if (listed is not null)
            {
                return listed.CreatedAtUtc;
            }
        }

        return new DateTimeOffset(File.GetLastWriteTimeUtc(backupPath), TimeSpan.Zero);
    }

    /// <summary>
    /// Removes a write-ahead-log sidecar belonging to the database that was just replaced.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A failure here is swallowed deliberately, and it is the only swallowed failure in this file.
    /// The database has already been replaced, so throwing would report a failed restore that in
    /// fact succeeded — and SQLite recreates both sidecars on the next open. The bad outcome this
    /// guards against is a STALE log being replayed, and a log that cannot be deleted because it is
    /// still open is one SQLite is already treating as live.
    /// </para>
    /// <para>
    /// THIS IS INSURANCE, AND THE TESTS CANNOT FALSIFY IT — said here rather than left to be
    /// discovered. SQLite removes both sidecars itself when the last connection closes cleanly,
    /// which is what the step above does, so removing these two calls leaves the whole suite green.
    /// What they cover is the non-clean case: a crash or a kill between closing the connection and
    /// copying the file, which leaves a log on disk that belongs to the database being replaced.
    /// The integration suite cannot construct that, so this code is defended by argument rather than
    /// by a test, and <c>RestoreAsync_ShouldYieldTheBackupsData_EvenWithStaleSidecarsOnDisk</c> says
    /// so where a reader of the tests will find it.
    /// </para>
    /// </remarks>
    private static void DeleteSidecar(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
