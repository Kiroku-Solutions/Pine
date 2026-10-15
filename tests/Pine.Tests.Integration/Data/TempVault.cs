using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Pine.Infrastructure.Data;

namespace Pine.Tests.Integration.Data;

/// <summary>
/// A real, file-based, SQLCipher-encrypted vault in a throwaway temp directory.
/// The shared <c>DatabaseFixture</c> uses ":memory:", which cannot be backed up to a file, so
/// backup/migration tests need this instead.
/// </summary>
internal sealed class TempVault : IDisposable
{
    private readonly List<PineDbContext> _contexts = [];
    private readonly List<SqliteConnection> _connections = [];

    /// <summary>
    /// Every connection string this vault has handed out, so <see cref="Dispose"/> can empty the
    /// pool behind each one.
    /// </summary>
    /// <remarks>
    /// A SET OF STRINGS RATHER THAN ONE STRING, BECAUSE THERE IS NEVER ONLY ONE (TD-140). The vault
    /// database, a raw connection to a backup file, a context opened on a backup file -- each is a
    /// different connection string and Microsoft.Data.Sqlite pools each one separately. The first
    /// version of this fix cleared the vault database's pool and nothing else, and left encrypted
    /// backup files held open for the rest of the process.
    /// </remarks>
    private readonly HashSet<string> _pooledConnectionStrings = new(StringComparer.Ordinal);

    static TempVault()
    {
        // SQLCipher provider registration; must happen before any SQLite usage.
        SQLitePCL.Batteries_V2.Init();
    }

    public TempVault()
    {
        VaultDirectory = Path.Combine(Path.GetTempPath(), "pine-tests", Guid.NewGuid().ToString("N"));
        CreateDirectoryWithRetries(VaultDirectory);
        Key = RandomNumberGenerator.GetBytes(32);
    }

    /// <summary>The vault directory, i.e. what <c>IVaultSession.DbPath</c> holds in production.</summary>
    public string VaultDirectory { get; }

    public byte[] Key { get; }

    public string DatabasePath => Path.Combine(VaultDirectory, "pine.db");

    public string BackupsDirectory => Path.Combine(VaultDirectory, "backups");

    /// <summary>Creates a keyed context for this vault; disposed automatically with the vault.</summary>
    public PineDbContext CreateContext() => CreateContext(DatabasePath);

    /// <summary>
    /// Creates a keyed context over an arbitrary database file in this vault -- a backup, say --
    /// disposed automatically with the vault, and with its connection pool released with it.
    /// </summary>
    /// <remarks>
    /// USE THIS RATHER THAN <c>new PineDbContext(path, key)</c> IN A TEST (TD-140). A context built
    /// directly holds a connection string this vault knows nothing about, so its pooled handle
    /// outlives the vault and Windows then refuses to delete the directory. That is not
    /// hypothetical: <c>AttachmentStorageTests.AReceipt_SurvivesABackup</c> did exactly that, and it
    /// leaked one encrypted vault per suite run until this overload existed.
    /// </remarks>
    public PineDbContext CreateContext(string databasePath)
    {
        Track(ConnectionStringFor(databasePath, Key, SqliteOpenMode.ReadWriteCreate));

        var context = new PineDbContext(databasePath, Key);
        _contexts.Add(context);
        return context;
    }

    /// <summary>Opens an arbitrary encrypted database file with an arbitrary key.</summary>
    public SqliteConnection OpenRaw(string databasePath, byte[] key)
    {
        var connectionString = ConnectionStringFor(databasePath, key, SqliteOpenMode.ReadWrite);
        Track(connectionString);

        var connection = new SqliteConnection(connectionString);
        _connections.Add(connection);
        connection.Open();
        return connection;
    }

    /// <summary>
    /// Records an already-applied migration in <c>__EFMigrationsHistory</c>. This reproduces the
    /// state of a vault created by an earlier Pine build: EF then sees a non-empty applied set and
    /// a non-empty pending set, which is exactly the upgrade path a backup must protect.
    /// </summary>
    public void SeedAppliedMigration(string migrationId)
    {
        using var connection = OpenExclusive();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
                "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
                "ProductVersion" TEXT NOT NULL
            );
            INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion") VALUES ($id, '10.0.11');
            """;
        command.Parameters.AddWithValue("$id", migrationId);
        command.ExecuteNonQuery();
    }

    /// <summary>Runs arbitrary SQL against the vault (used to plant a conflicting object).</summary>
    public void ExecuteSql(string sql)
    {
        using var connection = OpenExclusive();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    /// <summary>Reads a single scalar from an arbitrary encrypted database file.</summary>
    public string? ReadSetting(string databasePath, byte[] key, string settingKey)
    {
        using var connection = OpenRaw(databasePath, key);
        using var command = connection.CreateCommand();
        command.CommandText = @"SELECT ""Value"" FROM ""Settings"" WHERE ""Key"" = $key;";
        command.Parameters.AddWithValue("$key", settingKey);
        return command.ExecuteScalar() as string;
    }

    /// <summary>
    /// Disposes every context handed out so far, so the WAL is checkpointed into the database file.
    /// </summary>
    /// <remarks>
    /// A test that inspects the RAW BYTES of pine.db needs the pages actually to be in pine.db. With
    /// a connection still open, recent writes can be sitting in the write-ahead log instead, and the
    /// test would be reading a file that does not yet contain what it is asserting about.
    /// </remarks>
    public void CloseContexts()
    {
        foreach (var context in _contexts)
        {
            context.Dispose();
        }

        _contexts.Clear();
        ClearThisVaultsPool();
    }

    public void Dispose()
    {
        foreach (var context in _contexts)
        {
            context.Dispose();
        }

        foreach (var connection in _connections)
        {
            connection.Dispose();
        }

        ClearThisVaultsPool();
        DeleteDirectoryWithRetries(VaultDirectory);
    }

    /// <summary>
    /// Releases the pooled handles for every database THIS vault opened, and no others (TD-140).
    /// </summary>
    /// <remarks>
    /// <para>
    /// IT WAS <c>SqliteConnection.ClearAllPools()</c>, WHICH IS PROCESS-WIDE. xUnit runs test classes
    /// in parallel, so this teardown reached into every other class's pooled connections. The
    /// intermittent failure the suite acquired is a pooled connection handed out after its handle was
    /// gone -- <c>SqliteConnection.Open()</c> then <c>SqliteConnectionInternal.Close()</c> then
    /// <c>Deactivate()</c> then <c>ObjectDisposedException</c> on a <c>SQLitePCL.sqlite3</c> -- which
    /// is the stack captured in full for the first time while working this row.
    /// </para>
    /// <para>
    /// EVERY string, not just the vault database's: the independent review measured nine leaked vault
    /// directories per suite run when only the vault database's pool was cleared, because a backup
    /// file opened read-write, or a context opened on a backup, is a different connection string and
    /// therefore a different pool.
    /// </para>
    /// <para>
    /// The same mistake, in production code, was found and fixed in <c>VaultRestoreService</c> while
    /// closing plan item 1.1 -- and again in the same file while closing this row, where verifying a
    /// backup left that file pooled and undeletable.
    /// </para>
    /// </remarks>
    private void ClearThisVaultsPool()
    {
        Track(ConnectionStringFor(DatabasePath, Key, SqliteOpenMode.ReadWriteCreate));

        foreach (var connectionString in _pooledConnectionStrings)
        {
            using var forThisDatabase = new SqliteConnection(connectionString);
            SqliteConnection.ClearPool(forThisDatabase);
        }
    }

    private void Track(string connectionString) => _pooledConnectionStrings.Add(connectionString);

    private static string ConnectionStringFor(string databasePath, byte[] key, SqliteOpenMode mode) =>
        new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = mode,
            Password = Convert.ToHexString(key),
        }.ToString();

    private SqliteConnection OpenExclusive()
    {
        var connectionString = ConnectionStringFor(DatabasePath, Key, SqliteOpenMode.ReadWriteCreate);
        Track(connectionString);

        var connection = new SqliteConnection(connectionString);
        connection.Open();
        return connection;
    }

    /// <remarks>
    /// TWENTY ATTEMPTS, NOT FIVE, AND THE NUMBER WAS MEASURED. With the process-wide
    /// <c>ClearAllPools()</c> in teardown every handle in the process was gone by the time this ran,
    /// so five attempts always sufficed. Clearing only this vault's pools (TD-140) leaves this racing
    /// the operating system's own lazy handle release instead, and under a full parallel run exactly
    /// one directory per run was losing that race -- while no single test class leaked when run
    /// alone, which is what says it is a race and not an uncleaned pool. Two seconds of patience
    /// costs nothing in a teardown and is only ever spent when the race is lost.
    /// </remarks>
    private static void DeleteDirectoryWithRetries(string directory) =>
        WithRetries(
            () =>
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            },
            // AND THEN FAIL, RATHER THAN SHRUG (TD-140). This loop used to give up silently after
            // five attempts, which is why nine encrypted vaults per suite run could accumulate in
            // %TEMP% without a single red test -- found by an independent review counting
            // directories, not by this suite. On Windows a directory that will not delete means a
            // file handle is still open, and the only handles here are pooled connections, so this is
            // the assertion that the vault released everything it opened.
            $"The temp vault at '{directory}' could not be deleted after {MaxRetryAttempts} attempts "
            + "over two seconds, so something still holds a handle inside it. A pooled SQLite "
            + "connection is the usual cause: a test that opened a database in this vault with a "
            + "connection string TempVault does not know about. Open it through "
            + "TempVault.CreateContext(path) or TempVault.OpenRaw(path, key) so the pool is released "
            + "with the vault.");

    /// <remarks>
    /// THE OTHER HALF OF TD-97. Every vault's leaf directory is a fresh GUID, so two vaults never
    /// target the SAME leaf -- but every leaf hangs off the one shared parent,
    /// <c>%TEMP%\pine-tests</c>, and xUnit's default per-class parallelism puts dozens of these
    /// constructors and a comparable number of <see cref="Dispose"/> deletions against that one
    /// parent directory at once. A <c>Directory.CreateDirectory</c> here can observe the shared
    /// parent mid-change -- a sibling vault's own recursive delete passing through it, or Windows
    /// Defender's real-time scan touching a sibling's freshly-created file inside it -- and take a
    /// transient <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> that a lone
    /// test run would never see. That is this row's diagnosis since iteration 15 ("file contention
    /// between TempVault directories under xUnit's default per-class parallelism"), made concrete
    /// instead of argued: thirteen failures in thirteen different, unrelated test classes is what
    /// contention on a directory every one of them shares looks like, not a defect in any one of
    /// them.
    /// </remarks>
    private static void CreateDirectoryWithRetries(string directory) =>
        WithRetries(
            () => Directory.CreateDirectory(directory),
            $"The temp vault directory '{directory}' could not be created after {MaxRetryAttempts} "
            + "attempts over two seconds, so something is holding its shared parent directory "
            + "('%TEMP%\\pine-tests') open. This is TD-97's contention, not a disk-full or "
            + "permissions problem: a sibling TempVault's own create or delete is the usual cause.");

    /// <summary>
    /// Retries <paramref name="action"/> on the transient Windows file-locking exceptions a sibling
    /// <see cref="TempVault"/>'s own create or delete can cause (TD-97), bounded at
    /// <see cref="MaxRetryAttempts"/> attempts over roughly two seconds, and throws with
    /// <paramref name="failureMessage"/> -- wrapping the last caught exception -- if every attempt
    /// fails.
    /// </summary>
    /// <remarks>
    /// Internal, not private: this is also the seam the TD-97 regression tests use to prove the
    /// retry recovers from a simulated transient failure and is bounded rather than infinite, without
    /// needing to win an actual filesystem race to exercise it.
    /// </remarks>
    /// <param name="action">The operation to retry.</param>
    /// <param name="failureMessage">
    /// The message for the <see cref="InvalidOperationException"/> thrown once every attempt has
    /// failed.
    /// </param>
    internal static void WithRetries(Action action, string failureMessage)
    {
        Exception? last = null;

        for (var attempt = 0; attempt < MaxRetryAttempts; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                last = ex;

                if (attempt < MaxRetryAttempts - 1)
                {
                    Thread.Sleep(RetryDelay);
                }
            }
        }

        throw new InvalidOperationException(failureMessage, last);
    }

    /// <summary>Bound on <see cref="WithRetries"/>: twenty attempts, not infinite (TD-97/TD-140).</summary>
    internal const int MaxRetryAttempts = 20;

    /// <summary>Delay between <see cref="WithRetries"/> attempts: ~2 seconds total across the bound.</summary>
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(100);
}
