using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Accounts;
using Pine.Core.Transactions;
using Pine.Infrastructure.Data;
using Xunit;

namespace Pine.Tests.Integration.Data;

/// <summary>
/// TD-60: a split keeps its identity when the transaction it belongs to is edited.
/// </summary>
/// <remarks>
/// <para>
/// THE DEFECT, STATED AS THE REGISTER STATED IT: "<c>ReplaceSplits</c>/<c>ReplaceLineItems</c> mint
/// fresh <c>Guid</c> ids on every detached save." Editing a payee deleted every split row and
/// inserted a replacement set carrying new primary keys, so a split's id was not an identifier at
/// all — it was a value that happened to be unique, and happened to change every time anyone saved.
/// </para>
/// <para>
/// WHY IT IS BEING CLOSED HERE RATHER THAN WHEN IT WAS FOUND. Phase 3 gates synchronisation behind
/// "stable natural keys", and this was the one place in the schema where a row's identity provably
/// did not survive its own edit. A merge that has to decide whether two vaults hold the same split
/// cannot do it by id if the id is re-minted locally on every save, and the row's own Impact column
/// named exactly that: "any future reference to a split id (rules, attachments, audit trail) would
/// dangle."
/// </para>
/// <para>
/// Every test here writes through <c>SqliteTransactionRepository</c> against a real keyed file,
/// because the detached-save path is the defect's habitat: it is the repository that re-describes
/// materialised children, and the ids either survive that round trip or they do not.
/// </para>
/// </remarks>
public class StableChildIdentityTests
{
    private static readonly CurrencyCode Usd = new("USD");

    /// <summary>
    /// The headline case: edit something else about the transaction, and the split ids are the same
    /// ids afterwards.
    /// </summary>
    [Fact]
    public async Task EditingATransaction_KeepsItsSplitIds()
    {
        using var vault = new TempVault();
        var transactionId = await SeedAsync(vault, splitCount: 2);

        var before = await SplitIdsAsync(vault, transactionId);

        await MutateAsync(vault, transactionId, transaction =>
        {
            var splits = transaction.Splits.ToList();
            transaction.ReplaceSplits(splits.Select(split =>
                new SplitDefinition(split.Amount, split.Category, "edited", split.Id)));
        });

        var after = await SplitIdsAsync(vault, transactionId);

        after.Should().BeEquivalentTo(before, "a split that survives an edit is the same split");
    }

    /// <summary>
    /// The round trip the repository actually performs: load detached, save back unchanged.
    /// </summary>
    /// <remarks>
    /// This is the one that would have failed before the fix WITHOUT anyone asking for a change at
    /// all. Loading an aggregate and saving it straight back deleted and re-inserted every child,
    /// so the ids moved on a save that changed nothing whatsoever.
    /// </remarks>
    [Fact]
    public async Task LoadingAndSavingWithNoChanges_DoesNotMoveASingleId()
    {
        using var vault = new TempVault();
        var transactionId = await SeedAsync(vault, splitCount: 3);

        var before = await SplitIdsAsync(vault, transactionId);

        await using (var context = new PineDbContext(vault.DatabasePath, vault.Key))
        {
            var repository = new SqliteTransactionRepository(context);
            var detached = await LoadDetachedAsync(vault, transactionId);
            await repository.SaveAsync(detached, CancellationToken.None);
            await context.SaveChangesAsync(CancellationToken.None);
        }

        var after = await SplitIdsAsync(vault, transactionId);

        after.Should().BeEquivalentTo(before);
    }

    /// <summary>
    /// A split dropped from the set is deleted; the ones that remain keep their ids; a new one gets
    /// an id of its own.
    /// </summary>
    [Fact]
    public async Task ReplacingTheSet_KeepsSurvivors_DeletesTheAbsent_AndMintsOnlyTheNew()
    {
        using var vault = new TempVault();
        var transactionId = await SeedAsync(vault, splitCount: 2);

        var before = await SplitIdsAsync(vault, transactionId);
        var survivorId = before[0];

        await MutateAsync(vault, transactionId, transaction =>
        {
            var survivor = transaction.Splits.Single(split => split.Id == survivorId);

            // The survivor keeps its id; the other is dropped; a third arrives with none.
            transaction.ReplaceSplits(
            [
                new SplitDefinition(new Money(-40m, Usd), survivor.Category, survivor.Notes, survivor.Id),
                new SplitDefinition(new Money(-60m, Usd), "New category"),
            ]);
        });

        var after = await SplitIdsAsync(vault, transactionId);

        after.Should().HaveCount(2);
        after.Should().Contain(survivorId, "the split that was named again is the same split");
        after.Should().NotContain(before[1], "the split that was not named again is gone");
        after.Except(before).Should().ContainSingle("only the genuinely new split gets a new id");
    }

    /// <summary>An edited split's values reach the database, not just its id.</summary>
    /// <remarks>
    /// Keeping the id would be worth nothing if the update it enables were dropped. This is the
    /// assertion that the in-place mutator actually writes: before the fix the row was deleted and
    /// re-inserted, so the values arrived by a different route entirely.
    /// </remarks>
    [Fact]
    public async Task AnEditedSplit_KeepsItsIdAndAlsoSavesItsNewValues()
    {
        using var vault = new TempVault();
        var transactionId = await SeedAsync(vault, splitCount: 1);

        var before = await SplitIdsAsync(vault, transactionId);

        await MutateAsync(vault, transactionId, transaction =>
        {
            var split = transaction.Splits.Single();
            transaction.ReplaceSplits([new SplitDefinition(split.Amount, "Recategorised", "why", split.Id)]);
        });

        await using var context = new PineDbContext(vault.DatabasePath, vault.Key);
        var saved = await context.Transactions
            .AsNoTracking()
            .SingleAsync(t => t.Id == transactionId, CancellationToken.None);

        saved.Splits.Should().ContainSingle();
        saved.Splits.Single().Id.Should().Be(before[0]);
        saved.Splits.Single().Category.Should().Be("Recategorised");
        saved.Splits.Single().Notes.Should().Be("why");
    }

    /// <summary>Line items keep their identity on the same terms as splits.</summary>
    [Fact]
    public async Task EditingATransaction_KeepsItsLineItemIds()
    {
        using var vault = new TempVault();
        var transactionId = await SeedAsync(vault, splitCount: 1, withLineItems: true);

        var before = await LineItemIdsAsync(vault, transactionId);
        before.Should().HaveCount(2, "the seed writes two line items");

        await MutateAsync(vault, transactionId, transaction =>
        {
            transaction.ReplaceLineItems(transaction.LineItems.Select(lineItem =>
                new LineItemDefinition(
                    "renamed",
                    lineItem.Quantity,
                    lineItem.UnitPrice,
                    lineItem.TotalPrice,
                    lineItem.Kind,
                    lineItem.Id)));
        });

        var after = await LineItemIdsAsync(vault, transactionId);

        after.Should().BeEquivalentTo(before);
    }

    /// <summary>
    /// A set naming the same id twice produces two rows rather than one instance tracked twice.
    /// </summary>
    /// <remarks>
    /// EF Core refuses to track two instances under one key, so a caller that repeated an id would
    /// have crashed the save. The second occurrence is treated as a new split, which is the only
    /// reading that keeps the aggregate saveable — and the sum invariant still has to hold.
    /// </remarks>
    [Fact]
    public async Task ASetNamingOneIdTwice_WritesTwoRows()
    {
        using var vault = new TempVault();
        var transactionId = await SeedAsync(vault, splitCount: 1);

        var before = await SplitIdsAsync(vault, transactionId);

        await MutateAsync(vault, transactionId, transaction =>
        {
            var id = transaction.Splits.Single().Id;
            transaction.ReplaceSplits(
            [
                new SplitDefinition(new Money(-50m, Usd), "First", "", id),
                new SplitDefinition(new Money(-50m, Usd), "Second", "", id),
            ]);
        });

        var after = await SplitIdsAsync(vault, transactionId);

        after.Should().HaveCount(2);
        after.Should().Contain(before[0], "the first claim on the id is honoured");
        after.Distinct().Should().HaveCount(2, "the second gets an id of its own rather than colliding");
    }

    /// <summary>
    /// A rejected set leaves the aggregate completely unmutated — including the ids.
    /// </summary>
    /// <remarks>
    /// <c>ReplaceSplits</c> has always promised this, and the TD-60 change is exactly the kind that
    /// could have broken it: the old code built children first and validated afterwards, which was
    /// safe only because it threw before assigning them. The new code updates children in place, so
    /// validating after applying would have left half an edit behind on a throw.
    /// </remarks>
    [Fact]
    public async Task ARejectedSet_LeavesTheSplitsExactlyAsTheyWere()
    {
        using var vault = new TempVault();
        var transactionId = await SeedAsync(vault, splitCount: 2);

        var before = await SplitIdsAsync(vault, transactionId);

        var detached = await LoadDetachedAsync(vault, transactionId);
        var original = detached.Splits.Select(split => (split.Id, split.Category, split.Amount.Amount)).ToList();

        var act = () => detached.ReplaceSplits(
        [
            // Does not sum to the transaction total.
            new SplitDefinition(new Money(-1m, Usd), "Wrong", "", original[0].Id),
        ]);

        act.Should().Throw<InvalidOperationException>();

        detached.Splits.Select(split => (split.Id, split.Category, split.Amount.Amount))
            .Should().BeEquivalentTo(original, "a rejected set changes nothing at all");

        (await SplitIdsAsync(vault, transactionId)).Should().BeEquivalentTo(before);
    }

    // ------------------------------------------------------------------ helpers

    private static async Task<Guid> SeedAsync(TempVault vault, int splitCount, bool withLineItems = false)
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

        // The last split carries the remainder, because the sum invariant is exact: -100 over three
        // splits is not a whole number of cents and would be rejected before it ever reached a test.
        var share = Math.Round(-100m / splitCount, 2, MidpointRounding.ToZero);
        var amounts = Enumerable.Repeat(share, splitCount).ToList();
        amounts[^1] = -100m - (share * (splitCount - 1));

        transaction.ReplaceSplits(amounts
            .Select((amount, index) => new SplitDefinition(new Money(amount, Usd), $"Category {index}")));

        if (withLineItems)
        {
            transaction.ReplaceLineItems(
            [
                new LineItemDefinition("Bread", 1m, new Money(-40m, Usd), new Money(-40m, Usd)),
                new LineItemDefinition("Milk", 1m, new Money(-60m, Usd), new Money(-60m, Usd)),
            ]);
        }

        context.Transactions.Add(transaction);
        await context.SaveChangesAsync(CancellationToken.None);

        return transaction.Id;
    }

    /// <summary>Loads the aggregate into a context that is then thrown away, leaving it detached.</summary>
    private static async Task<Transaction> LoadDetachedAsync(TempVault vault, Guid transactionId)
    {
        await using var context = new PineDbContext(vault.DatabasePath, vault.Key);
        return await context.Transactions
            .AsNoTracking()
            .SingleAsync(t => t.Id == transactionId, CancellationToken.None);
    }

    /// <summary>Loads detached, applies the edit, and saves through the repository.</summary>
    private static async Task MutateAsync(TempVault vault, Guid transactionId, Action<Transaction> edit)
    {
        var detached = await LoadDetachedAsync(vault, transactionId);
        edit(detached);

        await using var context = new PineDbContext(vault.DatabasePath, vault.Key);
        var repository = new SqliteTransactionRepository(context);
        await repository.SaveAsync(detached, CancellationToken.None);
        await context.SaveChangesAsync(CancellationToken.None);
    }

    private static async Task<List<Guid>> SplitIdsAsync(TempVault vault, Guid transactionId)
    {
        await using var context = new PineDbContext(vault.DatabasePath, vault.Key);
        var transaction = await context.Transactions
            .AsNoTracking()
            .SingleAsync(t => t.Id == transactionId, CancellationToken.None);

        return transaction.Splits.Select(split => split.Id).OrderBy(id => id).ToList();
    }

    private static async Task<List<Guid>> LineItemIdsAsync(TempVault vault, Guid transactionId)
    {
        await using var context = new PineDbContext(vault.DatabasePath, vault.Key);
        var transaction = await context.Transactions
            .AsNoTracking()
            .SingleAsync(t => t.Id == transactionId, CancellationToken.None);

        return transaction.LineItems.Select(lineItem => lineItem.Id).OrderBy(id => id).ToList();
    }
}
