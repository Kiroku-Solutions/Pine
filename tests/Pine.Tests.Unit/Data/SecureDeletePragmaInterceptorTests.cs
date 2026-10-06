using System;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using FluentAssertions;
using Pine.Infrastructure.Data;
using Xunit;

namespace Pine.Tests.Unit.Data;

/// <summary>
/// Unit-level isolation of <see cref="SecureDeletePragmaInterceptor"/> itself, decoupled from
/// <see cref="PineDbContext"/> and from SQLCipher.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS FILE EXISTS, AND WHAT IT FOUND. The first version of this change's integration test
/// opened a real, keyed <c>PineDbContext</c> and asserted <c>PRAGMA secure_delete</c> read back 1.
/// It passed -- and kept passing after the interceptor's pragma string was typo'd, after it was set
/// to OFF, and after <c>AddInterceptors</c> was deleted from <c>OnConfiguring</c> entirely. The
/// reason is SQLCipher's own default: a keyed SQLCipher connection in this process already reports
/// <c>secure_delete = 1</c> with no application code involved at all, so a test built only on a
/// keyed connection cannot tell "this codebase turned it on" from "the vendor already had". That
/// finding does not make <see cref="SecureDeletePragmaInterceptor"/> pointless -- it is the only
/// thing in this codebase that SAYS the guarantee is required rather than relying on an undocumented
/// vendor default that a future SQLCipher build is free to change -- but it does mean the only way
/// to prove the interceptor itself does something is to exercise it on a connection SQLCipher has no
/// opinion about: a plain, unkeyed one.
/// </para>
/// <para>
/// So this file registers the exact same interceptor instance <c>PineDbContext</c> uses, on a
/// throwaway, unkeyed, in-memory <c>DbContext</c>, and checks the pragma there. SQLite's own default
/// for <c>secure_delete</c> on a connection nothing has touched is OFF
/// (<see cref="SecureDelete_WithoutTheInterceptor_DefaultsToOff"/> pins that baseline), so a reading
/// of ON on the same kind of connection WITH the interceptor registered
/// (<see cref="SecureDelete_WithTheInterceptor_IsOnOnAPlainUnkeyedConnection"/>) is attributable to
/// this codebase and to nothing else.
/// </para>
/// </remarks>
public sealed class SecureDeletePragmaInterceptorTests
{
    [Fact]
    public void SecureDelete_WithoutTheInterceptor_DefaultsToOff()
    {
        using var context = new PlainSqliteContext(withInterceptor: false);
        context.Database.OpenConnection();

        ReadSecureDelete(context.Database.GetDbConnection()).Should().Be(0,
            "SQLite's own default for a connection nothing has configured is secure_delete OFF -- " +
            "the baseline the interceptor test below is measured against");
    }

    [Fact]
    public void SecureDelete_WithTheInterceptor_IsOnOnAPlainUnkeyedConnection()
    {
        using var context = new PlainSqliteContext(withInterceptor: true);
        context.Database.OpenConnection();

        ReadSecureDelete(context.Database.GetDbConnection()).Should().Be(1,
            "SecureDeletePragmaInterceptor, and nothing SQLCipher contributes on an unkeyed " +
            "connection, must be what turns this on");
    }

    /// <summary>
    /// The async open path, separately from the sync one above. <c>DbConnectionInterceptor</c>
    /// exposes <c>ConnectionOpened</c> and <c>ConnectionOpenedAsync</c> as two independently
    /// overridable methods, and <c>PineDbContext</c> uses both -- <c>NextClock</c> calls
    /// <c>Database.OpenConnection()</c>, <c>NextClockAsync</c> calls <c>OpenConnectionAsync</c>, and
    /// ordinary EF query execution goes through whichever the caller used. A fix that only overrides
    /// one of the two leaves the other path silently uncovered, which is exactly the shape of gap
    /// this test exists to catch -- and did catch, during this change's own falsification pass.
    /// </summary>
    [Fact]
    public async Task SecureDelete_WithTheInterceptor_IsOnAfterOpeningAsync()
    {
        using var context = new PlainSqliteContext(withInterceptor: true);
        await context.Database.OpenConnectionAsync();

        ReadSecureDelete(context.Database.GetDbConnection()).Should().Be(1,
            "the async open path must carry the pragma too, not only the synchronous one");
    }

    /// <summary>
    /// A source scan, and it is named as one -- the same move <c>SqliteProviderInitialisationTests</c>
    /// makes for the same underlying reason.
    /// </summary>
    /// <remarks>
    /// NO BEHAVIOURAL TEST CAN CATCH <c>PineDbContext.OnConfiguring</c> LOSING ITS
    /// <c>AddInterceptors</c> CALL. This was checked, not assumed: deleting that one line and running
    /// every test in this file and in <c>SecureDeletePragmaTests</c> (integration) left every one of
    /// them green, because SQLCipher's own default already reports <c>secure_delete = 1</c> on a
    /// keyed connection with no application code involved at all -- the same finding this file's
    /// class-level remarks describe for the interceptor's pragma text. There is no seam at which the
    /// wiring can be observed to matter on THIS vendor's current default, so the strongest available
    /// instrument is the one <c>TheSharedRoot_ShouldArmTheProvider_AndNoHeadShould</c> already uses
    /// for an analogous gap: read the source and assert the call is there. It catches the realistic
    /// regression -- someone removing the registration while refactoring <c>OnConfiguring</c> -- even
    /// though it cannot catch a hypothetical future SQLCipher release that stops defaulting to ON.
    /// </remarks>
    [Fact]
    public void PineDbContext_ShouldRegisterTheSecureDeletePragmaInterceptor()
    {
        var source = ReadFromRepository("src/Pine.Infrastructure/Data/PineDbContext.cs");

        source.Should().Contain("AddInterceptors(SecureDeletePragmaInterceptor.Instance)",
            "every connection PineDbContext opens must register this interceptor -- not conditionally, " +
            "not only for some code paths -- so that the guarantee does not depend on a vendor default " +
            "this codebase never asserted and that a future SQLCipher release is free to change");
    }

    private static string ReadFromRepository(string relativePath)
    {
        var path = System.IO.Path.Combine(RepositoryRoot(), relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
        System.IO.File.Exists(path).Should().BeTrue($"'{relativePath}' is expected to exist");
        return System.IO.File.ReadAllText(path);
    }

    private static string RepositoryRoot()
    {
        var directory = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !System.IO.File.Exists(System.IO.Path.Combine(directory.FullName, "Pine.slnx")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the tests must be able to find the repository root");
        return directory!.FullName;
    }

    private static long ReadSecureDelete(System.Data.Common.DbConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA secure_delete;";
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    /// <summary>A throwaway, in-memory, unkeyed SQLite context -- no SQLCipher password, ever.</summary>
    private sealed class PlainSqliteContext : DbContext
    {
        private readonly bool _withInterceptor;

        public PlainSqliteContext(bool withInterceptor)
        {
            _withInterceptor = withInterceptor;
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            // In-memory, not file-based: this class exists only to probe a pragma, and a shared
            // in-memory connection string would collide with every other instance in the same
            // process (SQLite shares ":memory:" databases of the SAME name across connections).
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = $"file:{Guid.NewGuid():N}?mode=memory&cache=shared",
            }.ToString();

            optionsBuilder.UseSqlite(connectionString);

            if (_withInterceptor)
            {
                optionsBuilder.AddInterceptors(SecureDeletePragmaInterceptor.Instance);
            }
        }
    }
}
