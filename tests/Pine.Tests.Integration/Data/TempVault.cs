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

    static TempVault()
    {
        // SQLCipher provider registration; must happen before any SQLite usage.
        SQLitePCL.Batteries_V2.Init();
    }

    public TempVault()
    {
        VaultDirectory = Path.Combine(Path.GetTempPath(), "pine-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(VaultDirectory);
        Key = RandomNumberGenerator.GetBytes(32);
    }

    /// <summary>The vault directory, i.e. what <c>IVaultSession.DbPath</c> holds in production.</summary>
    public string VaultDirectory { get; }

    public byte[] Key { get; }

    public string DatabasePath => Path.Combine(VaultDirectory, "pine.db");

    public string BackupsDirectory => Path.Combine(VaultDirectory, "backups");

    /// <summary>Creates a keyed context for this vault; disposed automatically with the vault.</summary>
    public PineDbContext CreateContext()
    {
        var context = new PineDbContext(DatabasePath, Key);
        _contexts.Add(context);
        return context;
    }

    /// <summary>Opens an arbitrary encrypted database file with an arbitrary key.</summary>
    public SqliteConnection OpenRaw(string databasePath, byte[] key)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Password = Convert.ToHexString(key)
        }.ToString();

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
        SqliteConnection.ClearAllPools();
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

        // Windows will not let the directory go while pooled handles are still open.
        SqliteConnection.ClearAllPools();
        DeleteDirectoryWithRetries(VaultDirectory);
    }

    private SqliteConnection OpenExclusive()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Password = Convert.ToHexString(Key)
        }.ToString();

        var connection = new SqliteConnection(connectionString);
        connection.Open();
        return connection;
    }

    private static void DeleteDirectoryWithRetries(string directory)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }

                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(100);
            }
        }
    }
}
