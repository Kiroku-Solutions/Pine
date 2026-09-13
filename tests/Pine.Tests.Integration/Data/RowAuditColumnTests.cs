using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Pine.Core.Accounts;
using Pine.Core.Budgets;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Transactions;
using Pine.Infrastructure.Data;
using Xunit;

namespace Pine.Tests.Integration.Data;

/// <summary>
/// The audit columns Phase 3 gates synchronisation behind: when a row was written, and when it was
/// last written.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS IS AN INTEGRATION TEST AND NOT A UNIT ONE. The columns are shadow properties nothing in
/// the domain can see, stamped inside an override of <c>SaveChanges</c>. There is no object to
/// inspect and no method to call: the only way to find out whether a stamp was written is to read
/// the row back out of a real, keyed SQLite file, which is what every test here does — several of
/// them over a raw connection, so that not even EF's own mapping stands between the assertion and
/// the bytes.
/// </para>
/// <para>
/// THE THING THESE TESTS ARE REALLY DEFENDING is that the stamp cannot be forgotten. The hand-written
/// stamps that already existed are exactly the evidence for why: <c>Account</c> restamps
/// <c>UpdatedAt</c> in four separate mutators, <c>Transaction</c> has a <c>CreatedAt</c> and no
/// updated stamp at all, and <c>Category</c>, <c>Setting</c> and <c>Budget</c> have neither. Every
/// one of those is the same omission at a different call site.
/// </para>
/// </remarks>
public class RowAuditColumnTests
{
    private static readonly CurrencyCode Usd = new("USD");

    /// <summary>Every table that holds an aggregate root carries both columns.</summary>
    /// <remarks>
    /// Enumerated against the model rather than a hand-written list, so a root added later is
    /// covered the day it is mapped rather than the day somebody remembers this file.
    /// </remarks>
    [Fact]
    public async Task EveryRootTable_CarriesBothAuditColumns()
    {
        using var vault = new TempVault();
        await using var context = vault.CreateContext();
        await context.Database.MigrateAsync(CancellationToken.None);

        var rootTables = context.Model.GetEntityTypes()
            .Where(entityType => !entityType.IsOwned())
            .Select(entityType => entityType.GetTableName())
            .Where(table => table is not null)
            .Distinct()
            .ToList();

        rootTables.Should().HaveCount(7, "the model maps seven aggregate roots -- Category was the eighth until plan item 2.3 deleted it (TD-132)");

        foreach (var table in rootTables)
        {
            var columns = ColumnsOf(vault, table!);
            columns.Should().Contain(PineDbContext.CreatedAtColumnName, "{0} is a root table", table);
            columns.Should().Contain(PineDbContext.UpdatedAtColumnName, "{0} is a root table", table);
        }
    }

    /// <summary>Owned children do not carry their own stamps; the aggregate's stamp is theirs.</summary>
    [Fact]
    public async Task OwnedTables_DoNotCarryTheirOwnAuditColumns()
    {
        using var vault = new TempVault();
        await using var context = vault.CreateContext();
        await context.Database.MigrateAsync(CancellationToken.None);

        var splitColumns = ColumnsOf(vault, "TransactionSplits");

        splitColumns.Should().NotContain(PineDbContext.CreatedAtColumnName);
        splitColumns.Should().NotContain(PineDbContext.UpdatedAtColumnName);
    }

    /// <summary>An insert stamps both columns with the same instant.</summary>
    [Fact]
    public async Task AnInsert_StampsBothColumns()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero));
        using var vault = new TempVault();
        await MigrateAsync(vault);

        await using (var context = new PineDbContext(vault.DatabasePath, vault.Key, clock))
        {
            context.Accounts.Add(NewAccount());
            await context.SaveChangesAsync(CancellationToken.None);
        }

        var (created, updated) = ReadStamps(vault, "Accounts");

        created.Should().Be(new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Unspecified));
        updated.Should().Be(created, "an insert is a write, so the row was created and last written at the same instant");
    }

    /// <summary>
    /// An update moves the second stamp and leaves the first alone.
    /// </summary>
    /// <remarks>
    /// This is the assertion that makes two columns worth having rather than one. If an update
    /// rewrote both, the pair would carry exactly as much information as either half, and the
    /// question a merge asks first — has this row been touched since it was written — would have no
    /// answer at all.
    /// </remarks>
    [Fact]
    public async Task AnUpdate_MovesOnlyTheUpdatedStamp()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero));
        using var vault = new TempVault();
        await MigrateAsync(vault);

        Guid accountId;
        await using (var context = new PineDbContext(vault.DatabasePath, vault.Key, clock))
        {
            var account = NewAccount();
            accountId = account.Id;
            context.Accounts.Add(account);
            await context.SaveChangesAsync(CancellationToken.None);
        }

        var (createdOnInsert, _) = ReadStamps(vault, "Accounts");

        clock.Advance(TimeSpan.FromDays(30));

        await using (var context = new PineDbContext(vault.DatabasePath, vault.Key, clock))
        {
            var account = await context.Accounts.SingleAsync(a => a.Id == accountId, CancellationToken.None);
            account.Deposit(new Money(25m, Usd), clock);
            await context.SaveChangesAsync(CancellationToken.None);
        }

        var (createdAfterUpdate, updatedAfterUpdate) = ReadStamps(vault, "Accounts");

        createdAfterUpdate.Should().Be(createdOnInsert, "the row was not created again");
        updatedAfterUpdate.Should().Be(new DateTime(2026, 3, 31, 12, 0, 0, DateTimeKind.Unspecified));
        updatedAfterUpdate.Should().BeAfter(createdAfterUpdate);
    }

    /// <summary>
    /// A root nothing ever stamped by hand is stamped anyway.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>Budget</c> has no <c>CreatedAt</c>, no <c>UpdatedAt</c> and no <c>TimeProvider</c>
    /// anywhere in its constructor — it is one of the roots that had no audit trail of any kind.
    /// That is precisely why the mechanism is a shadow property in the context rather than a
    /// convention entities are asked to follow.
    /// </para>
    /// <para>
    /// This test used to use <c>Category</c>, which plan item 2.3 deleted (TD-132). <c>Budget</c>
    /// was the other root in the same position and is the honest replacement: it is not written by
    /// any production path either, which is the point — the stamping must not depend on the entity
    /// cooperating.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ARootWithNoDomainTimestamps_IsStampedAnyway()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 5, 4, 9, 30, 0, TimeSpan.Zero));
        using var vault = new TempVault();
        await MigrateAsync(vault);

        await using (var context = new PineDbContext(vault.DatabasePath, vault.Key, clock))
        {
            context.Budgets.Add(Budget.Create(
                "Groceries",
                new Money(100m, new CurrencyCode("EUR")),
                BudgetPeriod.Monthly));
            await context.SaveChangesAsync(CancellationToken.None);
        }

        var (created, updated) = ReadStamps(vault, "Budgets");

        created.Should().Be(new DateTime(2026, 5, 4, 9, 30, 0, DateTimeKind.Unspecified));
        updated.Should().Be(created);
    }

    /// <summary>
    /// Editing only a transaction's splits moves the TRANSACTION's stamp.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The aggregate is the unit, so a change anywhere inside it is a change to it. Without the
    /// owner-promotion the token bump already relies on, a split edit would change rows in
    /// <c>TransactionSplits</c>, leave <c>Transactions</c> untouched, and the owner's stamp would
    /// still claim the aggregate had not been modified since it was written.
    /// </para>
    /// <para>
    /// THE WRITE GOES THROUGH THE REPOSITORY, and it has to. Calling <c>ReplaceSplits</c> on a
    /// tracked aggregate and saving the context directly fails with a zero-rows-affected concurrency
    /// error — EF infers Modified for a new owned child because its client-assigned key is set, and
    /// <c>SqliteTransactionRepository</c> is the only thing that corrects that guess. This is not
    /// new and was verified against the unmodified code; it is simply that a bare context is not a
    /// write path this application has.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task EditingOnlyTheSplits_MovesTheOwnersStamp()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero));
        using var vault = new TempVault();
        await MigrateAsync(vault);

        var (_, transactionId) = await SeedTransactionAsync(vault, clock);

        var (createdOnInsert, updatedOnInsert) = ReadStamps(vault, "Transactions");

        clock.Advance(TimeSpan.FromHours(6));

        await using (var context = new PineDbContext(vault.DatabasePath, vault.Key, clock))
        {
            var repository = new SqliteTransactionRepository(context);
            var transaction = await repository.GetByIdAsync(transactionId, CancellationToken.None);
            transaction!.ReplaceSplits([new SplitDefinition(new Money(-100m, Usd), "Groceries", "recategorised")]);
            await repository.SaveAsync(transaction, CancellationToken.None);
            await context.SaveChangesAsync(CancellationToken.None);
        }

        var (createdAfter, updatedAfter) = ReadStamps(vault, "Transactions");

        createdAfter.Should().Be(createdOnInsert);
        updatedAfter.Should().BeAfter(updatedOnInsert, "a change to an owned child is a change to its aggregate");
    }

    /// <summary>
    /// The stamps are invisible to the domain.
    /// </summary>
    /// <remarks>
    /// The same guarantee the concurrency token carries: when the row was written is a fact about
    /// this store, not about money, and a domain property would also have to be defended against
    /// callers assigning it.
    /// </remarks>
    [Fact]
    public void TheStamps_AreShadowProperties_AndAreNotOnTheDomainTypes()
    {
        typeof(Account).GetProperty(PineDbContext.CreatedAtColumnName).Should().BeNull();
        typeof(Account).GetProperty(PineDbContext.UpdatedAtColumnName).Should().BeNull();
        typeof(Transaction).GetProperty(PineDbContext.UpdatedAtColumnName).Should().BeNull();
    }

    /// <summary>
    /// The shadow names do not collide with the domain stamps that already exist.
    /// </summary>
    /// <remarks>
    /// <c>Attachment</c> and <c>Rule</c> already have a <c>CreatedAtUtc</c> of their own. Had the
    /// shadow property been given that name, EF would have bound to the domain property instead and
    /// this whole mechanism would have silently done nothing on two of the eight roots — while
    /// every test above still passed, because they use <c>Account</c> and <c>Category</c>.
    /// </remarks>
    [Fact]
    public async Task OnARootThatAlreadyHasItsOwnCreatedStamp_BothExistSeparately()
    {
        using var vault = new TempVault();
        await MigrateAsync(vault);

        var ruleColumns = ColumnsOf(vault, "Rules");

        ruleColumns.Should().Contain("CreatedAtUtc", "the domain's own stamp");
        ruleColumns.Should().Contain(PineDbContext.CreatedAtColumnName, "the row's stamp");
        PineDbContext.CreatedAtColumnName.Should().NotBe("CreatedAtUtc");
    }

    // ------------------------------------------------------------------ helpers

    private static Account NewAccount() =>
        Account.Create("Checking", AccountType.Bank, new Money(0m, Usd), TimeProvider.System);

    private static async Task MigrateAsync(TempVault vault)
    {
        await using var context = new PineDbContext(vault.DatabasePath, vault.Key);
        await context.Database.MigrateAsync(CancellationToken.None);
    }

    private static async Task<(Guid AccountId, Guid TransactionId)> SeedTransactionAsync(
        TempVault vault, TimeProvider clock)
    {
        await using var context = new PineDbContext(vault.DatabasePath, vault.Key, clock);

        var account = NewAccount();
        context.Accounts.Add(account);

        var transaction = Transaction.Create(
            account.Id,
            new DateTimeOffset(2026, 5, 30, 0, 0, 0, TimeSpan.Zero),
            "Supermarket",
            new Money(-100m, Usd),
            TransactionType.Expense,
            TimeProvider.System);
        transaction.ReplaceSplits([new SplitDefinition(new Money(-100m, Usd), "Unassigned")]);
        context.Transactions.Add(transaction);

        await context.SaveChangesAsync(CancellationToken.None);
        return (account.Id, transaction.Id);
    }

    private static List<string> ColumnsOf(TempVault vault, string table)
    {
        using var connection = vault.OpenRaw(vault.DatabasePath, vault.Key);
        using var command = connection.CreateCommand();
        command.CommandText = $@"PRAGMA table_info(""{table}"");";

        var columns = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            columns.Add(reader.GetString(1));
        }

        return columns;
    }

    private static (DateTime Created, DateTime Updated) ReadStamps(TempVault vault, string table)
    {
        using var connection = vault.OpenRaw(vault.DatabasePath, vault.Key);
        using var command = connection.CreateCommand();
        command.CommandText =
            $@"SELECT ""{PineDbContext.CreatedAtColumnName}"", ""{PineDbContext.UpdatedAtColumnName}"" " +
            $@"FROM ""{table}"" LIMIT 1;";

        using var reader = command.ExecuteReader();
        reader.Read().Should().BeTrue("the row should be there to read");

        return (reader.GetDateTime(0), reader.GetDateTime(1));
    }
}
