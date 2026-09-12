using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pine.Core.Accounts;
using Pine.Core.Common.ValueObjects;
using Pine.Tests.Integration.Fixtures;
using Xunit;

namespace Pine.Tests.Integration.Data;

/// <summary>
/// Two test classes taking <see cref="DatabaseFixture"/> get two databases (TD-140).
/// </summary>
/// <remarks>
/// Every fixture used to build the identical connection string -- ":memory:" with an all-zero key --
/// so the pool, which keys on that string, was shared by every class that took the fixture. This is
/// the assertion that they are separate now; without it, "unique per fixture" is a comment.
/// </remarks>
public class DatabaseFixtureIsolationTests
{
    [Fact]
    public async Task InMemoryDatabasesAreNotShared()
    {
        var alreadyThere = Directory.GetFiles(Directory.GetCurrentDirectory(), "*pine-tests-*");

        using var first = new DatabaseFixture();
        using var second = new DatabaseFixture();

        first.DatabaseName.Should().NotBe(second.DatabaseName, "a shared name is a shared database and a shared pool");

        first.DbContext.Accounts.Add(Account.Create(
            "Only in the first", AccountType.Bank, new Money(1m, new CurrencyCode("USD")), TimeProvider.System));
        await first.DbContext.SaveChangesAsync();

        (await first.DbContext.Accounts.CountAsync()).Should().Be(1);
        (await second.DbContext.Accounts.CountAsync()).Should().Be(0,
            "one class's rows must never appear in another class's vault");

        // And it is genuinely in memory: drop "mode=memory" from the URI and SQLite creates the
        // name as a FILE (it parses the URI and keeps the path part, so the "?" and "&" do not make
        // it an illegal Windows name). Only files that appeared DURING this test count -- the first
        // version scanned the whole directory and went red on debris a previous mutation run had
        // left there, which is a test failing for a reason that has nothing to do with its subject.
        Directory.GetFiles(Directory.GetCurrentDirectory(), "*pine-tests-*").Except(alreadyThere)
            .Should().BeEmpty("the fixture claims an in-memory database; a new file here means the URI was ignored");
    }

    /// <summary>
    /// The fixture's database does not outlive the fixture (TD-140, adversarial pass).
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS IS THE ASSERTION THE FIRST VERSION OF THIS CHANGE NEEDED AND DID NOT HAVE, and the
    /// independent review is what found it. Microsoft.Data.Sqlite does NOT pool a private
    /// <c>":memory:"</c> database -- closing the connection destroys it -- but it DOES pool a
    /// URI-NAMED in-memory database. So moving the fixture from <c>":memory:"</c> to
    /// <c>file:...?mode=memory</c> to separate the pools ADDED a pooled resource to the one harness
    /// that had none, and <c>Dispose</c> did not release it: every fixture left a live, schema'd,
    /// encrypted database resident for the whole test process. That is the same shape as the defect
    /// this row is about, introduced while fixing it.
    /// </para>
    /// <para>
    /// Reopening the same name and finding no tables is the proof: if the pooled handle had been
    /// kept, the schema would still be there.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheFixturesDatabaseDoesNotOutliveTheFixture()
    {
        string name;

        using (var fixture = new DatabaseFixture())
        {
            name = fixture.DatabaseName;
            fixture.DbContext.Accounts.Add(Account.Create(
                "Gone after dispose", AccountType.Bank, new Money(1m, new CurrencyCode("USD")), TimeProvider.System));
            fixture.DbContext.SaveChanges();
        }

        using var reopened = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = name,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString());
        reopened.Open();

        using var tables = reopened.CreateCommand();
        tables.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table';";

        Convert.ToInt32(tables.ExecuteScalar(), CultureInfo.InvariantCulture).Should().Be(0,
            "a disposed fixture must leave nothing behind; a surviving schema means its connection "
            + "is still sitting in the pool with the database alive inside it");
    }
}
