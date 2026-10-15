using System.Globalization;
using Microsoft.Data.Sqlite;
using Pine.Application.Security;

namespace Pine.Infrastructure.Data;

/// <summary>
/// <see cref="IVaultDatabaseRekeyer"/> over SQLCipher's <c>PRAGMA rekey</c>, performed on a copy and
/// swapped in atomically (TD-86). Keys use the same convention as <see cref="PineDbContext"/>: the
/// upper-case hex of the 32-byte data key supplied as the connection <c>Password</c>.
/// </summary>
public sealed class SqlCipherVaultRekeyer : IVaultDatabaseRekeyer
{
    private const int KeyLengthBytes = 32;
    private const string DatabaseFileName = "pine.db";

    /// <inheritdoc />
    public void Rekey(string vaultDirectory, byte[] currentKey, byte[] newKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultDirectory);
        ArgumentNullException.ThrowIfNull(currentKey);
        ArgumentNullException.ThrowIfNull(newKey);

        if (currentKey.Length != KeyLengthBytes || newKey.Length != KeyLengthBytes)
        {
            throw new ArgumentException("Vault data keys must be 32 bytes.");
        }

        if (currentKey.AsSpan().SequenceEqual(newKey))
        {
            throw new ArgumentException("The new data key must differ from the current one.", nameof(newKey));
        }

        var databasePath = Path.Combine(vaultDirectory, DatabaseFileName);
        if (!File.Exists(databasePath))
        {
            throw new VaultRekeyException("The vault database does not exist.");
        }

        var workPath = databasePath + ".rekey-work";
        var replacedPath = databasePath + ".pre-rekey";
        var swapped = false;

        try
        {
            // 1. Prove the current key BEFORE copying or changing anything, and fold any WAL into the
            //    main file so the copy is complete.
            var originalEntries = ReadSchemaEntryCount(databasePath, currentKey, SqliteOpenMode.ReadWrite, checkpoint: true)
                ?? throw new VaultRekeyException("The current key does not open the vault database.");

            DeleteIfPresent(workPath);
            File.Copy(databasePath, workPath);

            // 2. Rekey the copy.
            ApplyRekey(workPath, currentKey, newKey);

            // 3. Verify the copy: opens under the new key with the same schema, and the old key is refused.
            var rekeyedEntries = ReadSchemaEntryCount(workPath, newKey, SqliteOpenMode.ReadOnly, checkpoint: false);
            if (rekeyedEntries != originalEntries)
            {
                throw new VaultRekeyException("Verification of the rekeyed database failed.");
            }

            if (ReadSchemaEntryCount(workPath, currentKey, SqliteOpenMode.ReadOnly, checkpoint: false) is not null)
            {
                throw new VaultRekeyException("The rekeyed database still opens under the old key.");
            }

            // 4. Swap atomically. The old-key file is kept only until the swap is confirmed, then removed:
            //    leaving it would let a leaked old key still open the data this rotation exists to protect.
            ReleaseCallerPool(databasePath, currentKey);
            File.Replace(workPath, databasePath, replacedPath);
            swapped = true;

            // The old file's WAL index must not be paired with the new file. The checkpoint above left the
            // WAL empty and every connection is closed, so removing them loses nothing.
            DeleteIfPresent(databasePath + "-wal");
            DeleteIfPresent(databasePath + "-shm");
        }
        catch (VaultRekeyException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Deliberately generic: nothing here needs the underlying text, and none of it may carry a key.
            throw new VaultRekeyException("The vault database could not be rekeyed; it was left unchanged.", ex);
        }
        finally
        {
            ReleaseCallerPool(databasePath, currentKey);
            DeleteIfPresent(workPath);
            if (swapped)
            {
                DeleteIfPresent(replacedPath);
            }
        }
    }

    /// <inheritdoc />
    public bool CanOpen(string vaultDirectory, byte[] key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultDirectory);
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length != KeyLengthBytes)
        {
            return false;
        }

        var databasePath = Path.Combine(vaultDirectory, DatabaseFileName);
        if (!File.Exists(databasePath))
        {
            return false;
        }

        try
        {
            return ReadSchemaEntryCount(databasePath, key, SqliteOpenMode.ReadWrite, checkpoint: false) is not null;
        }
        finally
        {
            ReleaseCallerPool(databasePath, key);
        }
    }

    private static void ApplyRekey(string path, byte[] currentKey, byte[] newKey)
    {
        using var connection = Open(path, currentKey, SqliteOpenMode.ReadWrite);
        connection.Open();
        using var command = connection.CreateCommand();

        // Hex digits only, so interpolation cannot inject. Same passphrase convention as the connection Password.
        command.CommandText = $"PRAGMA rekey = '{Convert.ToHexString(newKey)}';";
        command.ExecuteNonQuery();
    }

    /// <summary>Returns the schema entry count, or null when the key does not open the database.</summary>
    private static long? ReadSchemaEntryCount(string path, byte[] key, SqliteOpenMode mode, bool checkpoint)
    {
        try
        {
            using var connection = Open(path, key, mode);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT count(*) FROM sqlite_master;";
            var count = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);

            if (checkpoint)
            {
                using var wal = connection.CreateCommand();
                wal.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                wal.ExecuteNonQuery();
            }

            return count;
        }
        catch (SqliteException)
        {
            return null;
        }
    }

    private static SqliteConnection Open(string path, byte[] key, SqliteOpenMode mode) =>
        new(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = mode,
            Password = Convert.ToHexString(key),
            Pooling = false,
        }.ToString());

    /// <summary>
    /// Releases the pool a <see cref="PineDbContext"/> over this vault (same string it builds) may still hold, so
    /// the swap is not blocked by an idle pooled handle. Scoped to this one database; never process-wide.
    /// </summary>
    private static void ReleaseCallerPool(string databasePath, byte[] currentKey)
    {
        using var pooled = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Password = Convert.ToHexString(currentKey),
        }.ToString());
        SqliteConnection.ClearPool(pooled);
    }

    private static void DeleteIfPresent(string path)
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
