using System;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pine.Infrastructure.Data;

namespace Pine.Tests.Integration.Fixtures;

/// <summary>
/// One encrypted, in-memory vault for one test class.
/// </summary>
/// <remarks>
/// <para>
/// EVERY FIXTURE USED TO BUILD THE SAME CONNECTION STRING: <c>":memory:"</c> with an all-zero key,
/// byte-for-byte identical in every instance. Microsoft.Data.Sqlite pools by connection string, so
/// that is the shape of a shared-state hazard -- but it was a LATENT one, and the first version of
/// this comment overstated it. Two things, both measured by the independent review and both checked
/// again here: a private <c>":memory:"</c> database is not pooled at all, and exactly one test class
/// takes this fixture. There was no cross-class sharing to suffer from.
/// </para>
/// <para>
/// Each fixture now names its own in-memory database and carries its own key, so the pool cannot be
/// shared however many classes come to take it. <c>DatabaseFixtureIsolationTests</c> holds both
/// assertions: that two fixtures get two databases, and -- the one that matters more -- that a
/// disposed fixture leaves nothing behind. A URI-NAMED in-memory database IS pooled, unlike
/// <c>":memory:"</c>, so this change added a pooled resource to the one harness that had none, and
/// the first version of it never released that resource.
/// </para>
/// </remarks>
public class DatabaseFixture : IDisposable
{
    private readonly string _connectionString;

    public PineDbContext DbContext { get; private set; }

    public DatabaseFixture()
    {
        // SQLCipher provider registration; must happen before any SQLite usage.
        SQLitePCL.Batteries_V2.Init();

        // A named, private in-memory database: unique per fixture, so the connection string -- and
        // therefore the connection pool -- is unique per test class too.
        // The URI form is what makes it a NAMED in-memory database; PineDbContext always asks for
        // Mode=ReadWriteCreate, and "mode=memory" in the URI is what overrides that. Unique name per
        // fixture, so no two test classes share a database or a pool.
        DatabaseName = $"file:pine-tests-{Guid.NewGuid():N}?mode=memory&cache=shared";
        var key = RandomNumberGenerator.GetBytes(32);

        // The same string PineDbContext.OnConfiguring builds, so Dispose clears the pool the
        // context actually used. SqliteConnectionStringBuilder is order-insensitive, so this is
        // equal to the context's own string rather than merely similar to it.
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabaseName,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Password = Convert.ToHexString(key),
        }.ToString();

        DbContext = new PineDbContext(DatabaseName, key);

        DbContext.Database.OpenConnection();
        DbContext.Database.EnsureCreated();
    }

    /// <summary>The in-memory database this fixture opened. Unique to this fixture.</summary>
    public string DatabaseName { get; }

    public void Dispose()
    {
        DbContext.Database.CloseConnection();
        DbContext.Dispose();

        // AND RELEASE THE POOLED HANDLE, OR THE DATABASE OUTLIVES THE FIXTURE (TD-140, adversarial
        // pass). A private ":memory:" database is not pooled at all -- closing the connection
        // destroys it -- but a URI-NAMED in-memory database IS pooled, so naming it to separate the
        // pools added a pooled resource to the one harness that had none, and every fixture left a
        // live, schema'd, encrypted database resident for the whole test process. This is scoped to
        // this fixture's own connection string, which is unique to it.
        // DatabaseFixtureIsolationTests.TheFixturesDatabaseDoesNotOutliveTheFixture is the assertion.
        using (var forThisFixture = new SqliteConnection(_connectionString))
        {
            SqliteConnection.ClearPool(forThisFixture);
        }

        GC.SuppressFinalize(this);
    }
}
