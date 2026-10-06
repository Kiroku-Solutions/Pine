using System;
using System.Globalization;
using System.IO;
using System.Text;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pine.Infrastructure.Data;
using Xunit;

namespace Pine.Tests.Integration.Data;

/// <summary>
/// TD-145's <c>secure_delete</c> half: every connection <see cref="PineDbContext"/> opens must carry
/// <c>PRAGMA secure_delete = ON</c>, and that pragma must actually change what a delete leaves behind.
/// </summary>
/// <remarks>
/// <para>
/// TWO DIFFERENT QUESTIONS, TWO DIFFERENT TESTS. "Is the pragma set on the connection this app
/// actually opens" and "does the pragma do what SQLite's documentation says it does" are not the
/// same claim, and a test that only asserts <c>PRAGMA secure_delete</c> reads back 1 proves neither
/// of them alone -- it would pass equally if the pragma were a no-op in this SQLite build.
/// <see cref="SecureDelete_IsOnForEveryConnectionPineDbContextOpens"/> answers the first question on
/// the actual keyed, SQLCipher-backed connection this application uses.
/// <see cref="SecureDeleteOff_LeavesDeletedContentInFreePages_SecureDeleteOn_ZeroesIt"/> answers the
/// second, empirically, at the raw file level.
/// </para>
/// <para>
/// WHY THE EMPIRICAL TEST USES A PLAIN, UNENCRYPTED DATABASE RATHER THAN A SQLCIPHER-KEYED ONE.
/// SQLCipher encrypts every page of the file, including free ones -- that is the whole point of it,
/// and it is true regardless of <c>secure_delete</c>. A raw byte search for recognisable plaintext
/// in an encrypted <c>pine.db</c> would find nothing whether or not the pragma is set, which would
/// make the test vacuous in exactly the way this file's own remarks warn against elsewhere in this
/// codebase. <c>secure_delete</c> is a property of SQLite's own page-free mechanism, one layer below
/// SQLCipher's page cipher, and that mechanism behaves identically whether or not the pages above it
/// happen to be encrypted afterwards. Demonstrating it on a plain database is therefore not a weaker
/// test of the claim this row makes -- it is the only way to observe the claim directly instead of
/// through a layer that would hide it either way. <see cref="SecureDelete_IsOnForEveryConnectionPineDbContextOpens"/>
/// is what ties that mechanism to the actual encrypted connection this application opens.
/// </para>
/// </remarks>
public sealed class SecureDeletePragmaTests
{
    /// <summary>
    /// The pragma must be ON on the first open AND survive every later open/close cycle of the SAME
    /// connection -- which is what <see cref="PineDbContext.NextClock"/> does on every save, and what
    /// SQLite's documentation says NOT to assume survives on its own: a pragma is per-connection
    /// state, forgotten on close, not a property of the database file.
    /// </summary>
    [Fact]
    public void SecureDelete_IsOnForEveryConnectionPineDbContextOpens()
    {
        using var vault = new TempVault();
        using var context = vault.CreateContext();

        for (var cycle = 0; cycle < 3; cycle++)
        {
            context.Database.OpenConnection();
            try
            {
                ReadSecureDelete(context.Database.GetDbConnection()).Should().Be(1,
                    $"PineDbContext must never open a connection without secure_delete, and this is open #{cycle + 1}");
            }
            finally
            {
                context.Database.CloseConnection();
            }
        }
    }

    /// <summary>
    /// The empirical half: a row's content either survives a DELETE in a free page (OFF, SQLite's
    /// own default) or is overwritten with zeros at DELETE time (ON) -- not merely that a string
    /// reads back as "1".
    /// </summary>
    [Fact]
    public void SecureDeleteOff_LeavesDeletedContentInFreePages_SecureDeleteOn_ZeroesIt()
    {
        // A GUID, not a fixed string: a fixed literal risks colliding with something SQLite itself
        // writes into the file (a page header, a schema string), which would make "found" meaningless.
        var markerText = Guid.NewGuid().ToString("N");
        var marker = Encoding.ASCII.GetBytes(markerText);

        // Tiled across a payload bigger than SQLite's default page size (4096 bytes) so the row's
        // content spills into one or more OVERFLOW pages that become entirely free once the row is
        // deleted -- not a page still partly occupied by other live rows, which secure_delete would
        // leave looking identical either way.
        var payload = new byte[8192];
        for (var offset = 0; offset + marker.Length <= payload.Length; offset += marker.Length)
        {
            marker.CopyTo(payload, offset);
        }

        var offPath = Path.Combine(Path.GetTempPath(), $"pine-secure-delete-off-{Guid.NewGuid():N}.db");
        var onPath = Path.Combine(Path.GetTempPath(), $"pine-secure-delete-on-{Guid.NewGuid():N}.db");

        try
        {
            var bytesWithoutSecureDelete = WriteThenDeleteRow(offPath, secureDelete: false, payload);
            var bytesWithSecureDelete = WriteThenDeleteRow(onPath, secureDelete: true, payload);

            Contains(bytesWithoutSecureDelete, marker).Should().BeTrue(
                "SQLite's own default (secure_delete OFF) leaves a deleted row's bytes sitting in a " +
                "free page until the page is reused -- that is the gap this row closes");

            Contains(bytesWithSecureDelete, marker).Should().BeFalse(
                "with secure_delete ON, SQLite overwrites the freed page with zeros at DELETE time, " +
                "so the row's content must not be recoverable from the raw file afterwards");
        }
        finally
        {
            // ClearPool(connection), not ClearAllPools() -- TestHarnessPoolingTests (TD-140) forbids
            // the process-wide form everywhere in this repository, because xUnit runs test classes in
            // parallel and it would release pooled handles belonging to other classes' databases too.
            ClearPoolFor(offPath);
            ClearPoolFor(onPath);
            File.Delete(offPath);
            File.Delete(onPath);
        }
    }

    /// <summary>
    /// Builds a throwaway, unencrypted database, writes one row carrying <paramref name="content"/>,
    /// deletes it, closes the connection (so the write is flushed out of the OS cache and into the
    /// file this test reads next), and returns the file's raw bytes.
    /// </summary>
    private static byte[] WriteThenDeleteRow(string path, bool secureDelete, byte[] content)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();

        using (var connection = new SqliteConnection(connectionString))
        {
            connection.Open();

            Execute(connection, $"PRAGMA secure_delete = {(secureDelete ? "ON" : "OFF")};");
            Execute(connection, "CREATE TABLE Marker (Id INTEGER PRIMARY KEY, Content BLOB NOT NULL);");

            using (var insert = connection.CreateCommand())
            {
                insert.CommandText = "INSERT INTO Marker (Content) VALUES ($content);";
                var parameter = insert.CreateParameter();
                parameter.ParameterName = "$content";
                parameter.Value = content;
                insert.Parameters.Add(parameter);
                insert.ExecuteNonQuery();
            }

            Execute(connection, "DELETE FROM Marker;");
        }

        // Microsoft.Data.Sqlite pools the native handle: the `using` above returns it to the pool
        // rather than truly closing the file, so without this the read below finds the file still
        // held open (and, on Windows, fails outright with IOException). Pool-specific, not
        // ClearAllPools() -- see the remark on the other call site.
        ClearPoolFor(path);

        return File.ReadAllBytes(path);
    }

    private static void ClearPoolFor(string path)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();

        using var connection = new SqliteConnection(connectionString);
        SqliteConnection.ClearPool(connection);
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static long ReadSecureDelete(System.Data.Common.DbConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA secure_delete;";
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static bool Contains(byte[] haystack, byte[] needle) =>
        haystack.AsSpan().IndexOf(needle) >= 0;
}
