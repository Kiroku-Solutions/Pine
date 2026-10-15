using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Pine.Core.Accounts;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Transactions;
using Pine.Infrastructure.Data;
using Xunit;

namespace Pine.Tests.Integration.Data;

/// <summary>
/// TD-59: the repository SETS the state of every owned split/line item from what the database
/// holds, rather than correcting EF's guess. Each test inspects the change tracker after
/// <c>SaveAsync</c> and before the flush, which is the only point at which the state is observable.
/// </summary>
public class OwnedChildStateTests
{
    private static readonly CurrencyCode Usd = new("USD");

    [Fact]
    public async Task ExistingSplit_StaysAModification_AndItsNewValueIsWritten()
    {
        using var vault = new TempVault();
        var (transactionId, splitIds) = await SeedAsync(vault);

        var detached = await LoadDetachedAsync(vault, transactionId);
        detached.ReplaceSplits(detached.Splits
            .Select(s => new SplitDefinition(s.Amount, s.Id == splitIds[0] ? "Edited" : s.Category, s.Notes, s.Id)));

        await using var context = new PineDbContext(vault.DatabasePath, vault.Key);
        await new SqliteTransactionRepository(context).SaveAsync(detached, CancellationToken.None);

        var tracked = context.ChangeTracker.Entries<TransactionSplit>().ToList();
        tracked.Should().NotContain(e => e.State == EntityState.Added, "every split row already exists");
        tracked.Should().NotContain(e => e.State == EntityState.Deleted);
        tracked.Single(e => e.Entity.Id == splitIds[0]).State.Should().Be(EntityState.Modified);

        await context.SaveChangesAsync(CancellationToken.None);

        var saved = await ReloadAsync(vault, transactionId);
        saved.Splits.Select(s => s.Id).Should().BeEquivalentTo(splitIds);
        saved.Splits.Single(s => s.Id == splitIds[0]).Category.Should().Be("Edited");
    }

    [Fact]
    public async Task NewSplit_IsAdded_NotModified()
    {
        using var vault = new TempVault();
        var (transactionId, splitIds) = await SeedAsync(vault);

        var detached = await LoadDetachedAsync(vault, transactionId);
        detached.ReplaceSplits(
        [
            new SplitDefinition(new Money(-30m, Usd), "A", "", splitIds[0]),
            new SplitDefinition(new Money(-30m, Usd), "B", "", splitIds[1]),
            new SplitDefinition(new Money(-40m, Usd), "Brand new"),
        ]);

        await using var context = new PineDbContext(vault.DatabasePath, vault.Key);
        await new SqliteTransactionRepository(context).SaveAsync(detached, CancellationToken.None);

        var added = context.ChangeTracker.Entries<TransactionSplit>().Where(e => e.State == EntityState.Added).ToList();
        added.Should().ContainSingle().Which.Entity.Category.Should().Be("Brand new");

        await context.SaveChangesAsync(CancellationToken.None);

        (await ReloadAsync(vault, transactionId)).Splits.Should().HaveCount(3);
    }

    [Fact]
    public async Task RemovedSplit_IsDeleted()
    {
        using var vault = new TempVault();
        var (transactionId, splitIds) = await SeedAsync(vault);

        var detached = await LoadDetachedAsync(vault, transactionId);
        detached.ReplaceSplits([new SplitDefinition(new Money(-100m, Usd), "Only", "", splitIds[0])]);

        await using var context = new PineDbContext(vault.DatabasePath, vault.Key);
        await new SqliteTransactionRepository(context).SaveAsync(detached, CancellationToken.None);

        context.ChangeTracker.Entries<TransactionSplit>()
            .Single(e => e.Entity.Id == splitIds[1]).State.Should().Be(EntityState.Deleted);

        await context.SaveChangesAsync(CancellationToken.None);

        (await ReloadAsync(vault, transactionId)).Splits.Select(s => s.Id).Should().Equal(splitIds[0]);
    }

    [Fact]
    public async Task LineItems_FollowTheSameRules()
    {
        using var vault = new TempVault();
        var (transactionId, _) = await SeedAsync(vault);
        var items = (await ReloadAsync(vault, transactionId)).LineItems.ToList();
        items.Should().HaveCount(2);

        var detached = await LoadDetachedAsync(vault, transactionId);
        detached.ReplaceLineItems(
        [
            new LineItemDefinition("Renamed", 1m, new Money(-40m, Usd), new Money(-40m, Usd), items[0].Kind, items[0].Id),
            new LineItemDefinition("Fresh", 1m, new Money(-60m, Usd), new Money(-60m, Usd)),
        ]);

        await using var context = new PineDbContext(vault.DatabasePath, vault.Key);
        await new SqliteTransactionRepository(context).SaveAsync(detached, CancellationToken.None);

        var entries = context.ChangeTracker.Entries<TransactionLineItem>().ToList();
        entries.Single(e => e.Entity.Id == items[0].Id).State.Should().Be(EntityState.Modified);
        entries.Single(e => e.Entity.Description == "Fresh").State.Should().Be(EntityState.Added);
        entries.Single(e => e.Entity.Id == items[1].Id).State.Should().Be(EntityState.Deleted);

        await context.SaveChangesAsync(CancellationToken.None);

        var saved = (await ReloadAsync(vault, transactionId)).LineItems;
        saved.Should().HaveCount(2);
        saved.Select(l => l.Description).Should().BeEquivalentTo("Renamed", "Fresh");
    }

    private static async Task<(Guid TransactionId, Guid[] SplitIds)> SeedAsync(TempVault vault)
    {
        await using var context = new PineDbContext(vault.DatabasePath, vault.Key);
        await context.Database.MigrateAsync(CancellationToken.None);

        var account = Account.Create("Checking", AccountType.Bank, new Money(0m, Usd), TimeProvider.System);
        context.Accounts.Add(account);

        var transaction = Transaction.Create(
            account.Id,
            new DateTimeOffset(2026, 4, 2, 0, 0, 0, TimeSpan.Zero),
            "Supermarket",
            new Money(-100m, Usd),
            TransactionType.Expense,
            TimeProvider.System);
        transaction.ReplaceSplits(
        [
            new SplitDefinition(new Money(-60m, Usd), "One"),
            new SplitDefinition(new Money(-40m, Usd), "Two"),
        ]);
        transaction.ReplaceLineItems(
        [
            new LineItemDefinition("Bread", 1m, new Money(-40m, Usd), new Money(-40m, Usd)),
            new LineItemDefinition("Milk", 1m, new Money(-60m, Usd), new Money(-60m, Usd)),
        ]);

        context.Transactions.Add(transaction);
        await context.SaveChangesAsync(CancellationToken.None);

        return (transaction.Id, transaction.Splits.Select(s => s.Id).ToArray());
    }

    private static async Task<Transaction> LoadDetachedAsync(TempVault vault, Guid id)
    {
        await using var context = new PineDbContext(vault.DatabasePath, vault.Key);
        return await context.Transactions.AsNoTracking().SingleAsync(t => t.Id == id, CancellationToken.None);
    }

    private static Task<Transaction> ReloadAsync(TempVault vault, Guid id) => LoadDetachedAsync(vault, id);
}
