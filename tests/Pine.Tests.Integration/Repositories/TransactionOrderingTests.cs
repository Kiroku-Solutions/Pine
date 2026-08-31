using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Pine.Core.Accounts;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Transactions;
using Pine.Infrastructure.Data;
using Pine.Tests.Integration.Data;
using Xunit;

namespace Pine.Tests.Integration.Repositories;

/// <summary>
/// The transaction list's ordering, which is the only <c>ORDER BY</c> in Pine that SQLite evaluates.
/// </summary>
/// <remarks>
/// <para>
/// <c>Transaction.Date</c> is a <see cref="DateTimeOffset"/>, and there are TWO separate defects in
/// having ordered by it, one stacked on the other.
/// </para>
/// <para>
/// The first is the one that actually reached users: the SQLite provider REFUSES to translate an
/// ORDER BY over a <see cref="DateTimeOffset"/> and throws <see cref="NotSupportedException"/>. So
/// the transaction list and the recent-transactions widget did not show a badly sorted ledger, they
/// threw. That is TD-94, and it had shipped.
/// </para>
/// <para>
/// The second is what would have been wrong had it worked. The provider stores the value as text
/// with the offset appended — <c>2026-05-01 23:00:00+02:00</c> — and sorting that text sorts by
/// local wall clock rather than by instant. Pine writes both kinds of offset: the add-transaction
/// screen defaults to <c>DateTimeOffset.Now</c> and the CSV importer parses without one, while other
/// paths write UTC. This is why TD-30's "add an index on Date" could not be taken literally: the
/// index would have made a wrong order fast.
/// </para>
/// <para>
/// These tests run against a real encrypted vault on the real schema, because neither defect exists
/// until the comparison reaches SQLite. Sorting the same objects in memory has always been correct,
/// which is precisely how a suite of 156 tests stayed green over a screen that could not load.
/// </para>
/// </remarks>
public class TransactionOrderingTests
{
    /// <summary>21:00Z, from a +02:00 clock.</summary>
    private static readonly DateTimeOffset EarlierInstant =
        new(2026, 5, 1, 23, 0, 0, TimeSpan.FromHours(2));

    /// <summary>23:00Z, from a -05:00 clock: two hours later, but 18:00 on the wall.</summary>
    private static readonly DateTimeOffset LaterInstant =
        new(2026, 5, 1, 18, 0, 0, TimeSpan.FromHours(-5));

    [Fact]
    public async Task GetPagedAsync_OrdersByInstant_WhenTheOffsetsDiffer()
    {
        // Arrange
        using var vault = new TempVault();
        var accountId = await SeedAsync(vault);

        // Act
        await using var context = new PineDbContext(vault.DatabasePath, vault.Key);
        var repository = new SqliteTransactionRepository(context);
        var page = await repository.GetPagedAsync(1, 10, CancellationToken.None);

        // Assert
        page.Items.Select(t => t.Payee).Should().Equal(
            "later",
            "earlier");
    }

    /// <summary>
    /// Ordering by the offset-bearing column through EF does not merely sort wrongly — the provider
    /// refuses to translate it at all. This is what the transaction list did until TD-94.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS TEST PINS A DEFECT THAT WAS LIVE AND SHIPPED. <c>GetPagedAsync</c> ordered by
    /// <c>Transaction.Date</c>, a <see cref="DateTimeOffset"/>, and the SQLite provider throws
    /// <see cref="NotSupportedException"/> for that rather than sorting it. So the transaction list
    /// screen and the recent-transactions dashboard widget — the only two callers — threw every time
    /// they loaded. No test caught it because no test had ever executed <c>GetPagedAsync</c> against
    /// a real SQLite database; the one unit test that names the method asserts, with a substitute,
    /// that it is NOT called.
    /// </para>
    /// <para>
    /// It is asserted here rather than deleted along with the defect, because the fix is a column
    /// choice that a future refactor could quietly undo. Anyone who points an ORDER BY back at
    /// <c>Date</c> gets this test, with this explanation, instead of a crash in front of a user.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task OrderingByTheOffsetBearingColumn_IsRefusedByTheProvider_NotMerelyMisSorted()
    {
        // Arrange
        using var vault = new TempVault();
        await SeedAsync(vault);

        // Act
        await using var context = new PineDbContext(vault.DatabasePath, vault.Key);
        var ordering = async () => await context.Transactions
            .AsNoTracking()
            .OrderByDescending(t => t.Date)
            .Select(t => t.Payee)
            .ToListAsync(CancellationToken.None);

        // Assert
        (await ordering.Should().ThrowAsync<NotSupportedException>())
            .WithMessage("*DateTimeOffset*ORDER BY*");
    }

    /// <summary>
    /// Underneath EF, the stored text really does sort by local wall clock. This is the reason the
    /// ordering column had to be a new one rather than an index on the existing <c>Date</c>.
    /// </summary>
    /// <remarks>
    /// Read over a raw SQLCipher connection, because EF will not issue this ORDER BY at all (see
    /// above). Without this test the pair of rows above proves only that the provider throws, and an
    /// index on <c>Date</c> could still look like a reasonable thing to add. It is not: it would
    /// index an order that puts a later purchase first.
    /// </remarks>
    [Fact]
    public async Task TheStoredDateText_SortsByWallClock_WhichIsWhyItIsNotTheOrderingColumn()
    {
        // Arrange
        using var vault = new TempVault();
        await SeedAsync(vault);

        // Act
        var byStoredDate = ReadPayees(vault, @"SELECT ""Payee"" FROM ""Transactions"" ORDER BY ""Date"" DESC;");
        var byStoredUtc = ReadPayees(vault, @"SELECT ""Payee"" FROM ""Transactions"" ORDER BY ""DateUtc"" DESC;");

        // Assert: the same two rows, opposite orders. Only one of them is chronological.
        byStoredDate.Should().Equal("earlier", "later");
        byStoredUtc.Should().Equal("later", "earlier");
    }

    private static List<string> ReadPayees(TempVault vault, string sql)
    {
        using var connection = vault.OpenRaw(vault.DatabasePath, vault.Key);
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        var payees = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            payees.Add(reader.GetString(0));
        }

        return payees;
    }

    /// <summary>
    /// The ordering column is derived where the date is assigned, so it cannot be left behind by a
    /// write path that forgets about it.
    /// </summary>
    [Fact]
    public async Task EveryPersistedTransaction_CarriesTheUtcInstantOfItsOwnDate()
    {
        // Arrange
        using var vault = new TempVault();
        await SeedAsync(vault);

        // Act
        await using var context = new PineDbContext(vault.DatabasePath, vault.Key);
        var transactions = await context.Transactions.AsNoTracking().ToListAsync(CancellationToken.None);

        // Assert
        transactions.Should().HaveCount(2);
        transactions.Should().OnlyContain(t => t.DateUtc == t.Date.UtcDateTime);
    }

    /// <summary>
    /// Writes an account and the two transactions the offset pair above describes.
    /// </summary>
    private static async Task<Guid> SeedAsync(TempVault vault)
    {
        await using var context = new PineDbContext(vault.DatabasePath, vault.Key);
        await context.Database.MigrateAsync(CancellationToken.None);

        var usd = new CurrencyCode("USD");
        var account = Account.Create("Traveller", AccountType.Bank, new Money(0m, usd), TimeProvider.System);
        context.Accounts.Add(account);

        // Inserted earliest-instant-first, so a result that merely echoes insertion order is the
        // wrong answer rather than accidentally the right one.
        context.Transactions.Add(Transaction.Create(
            account.Id, EarlierInstant, "earlier", new Money(1m, usd), TransactionType.Income, TimeProvider.System));
        context.Transactions.Add(Transaction.Create(
            account.Id, LaterInstant, "later", new Money(1m, usd), TransactionType.Income, TimeProvider.System));

        await context.SaveChangesAsync(CancellationToken.None);
        return account.Id;
    }
}
