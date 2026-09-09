using System;
using System.Globalization;
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
/// Round-trip persistence proof for the two <c>OwnsMany</c> collections on
/// <see cref="Transaction"/> (TD-13).
/// </summary>
/// <remarks>
/// <para>
/// EVERY ASSERTION IS MADE AGAINST A DIFFERENT CONNECTION THAN THE ONE THAT WROTE. A test that
/// re-reads through the same <see cref="PineDbContext"/> that staged the change proves nothing:
/// EF's identity map hands back the in-memory graph whatever SQL did or did not run, so a repository
/// that silently dropped the owned collections would still pass. Row counts here come from a raw
/// SQLCipher connection issuing <c>SELECT COUNT(*)</c> against the real tables, and the aggregate is
/// re-materialised through a brand-new context with an empty change tracker.
/// </para>
/// <para>
/// A FILE VAULT, NOT <c>":memory:"</c>: an in-memory SQLite database dies with its connection, so a
/// second connection would see a different, empty database and every assertion would pass for the
/// wrong reason. This mirrors the reasoning already documented on <c>AtomicityHost</c>.
/// </para>
/// <para>
/// The write path is production-shaped: one scope means one context, one repository and one
/// <see cref="EfCoreUnitOfWork"/> that begins, flushes and commits — exactly what
/// <c>TransactionBehavior</c> does. Repositories do not call <c>SaveChangesAsync</c> (TD-01), so
/// flushing behind the repository's back would test a path that does not exist.
/// </para>
/// </remarks>
public class TransactionOwnedCollectionRoundTripTests
{
    private static CurrencyCode Eur => new("EUR");

    private static Money Euros(decimal amount) => new(amount, Eur);

    /// <summary>
    /// Grow, shrink and empty are three different EF code paths for an owned collection, and the
    /// shrinking direction is the one a naive update gets wrong: it inserts the new children and
    /// leaves the orphans behind, so the row count only ever climbs.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Splits_GrowThenShrinkThenClear_AreEachVisibleFromAFreshContext()
    {
        using var vault = new TempVault();
        await MigrateAsync(vault);

        var id = await SeedAsync(vault);
        CountRows(vault, "TransactionSplits").Should().Be(2, "the insert path must write both splits");

        // 2 -> 3 (grow)
        await MutateAsync(vault, id, transaction => transaction.ReplaceSplits(
        [
            new SplitDefinition(Euros(40m), "Groceries"),
            new SplitDefinition(Euros(30m), "Household"),
            new SplitDefinition(Euros(30m), "Transport")
        ]));

        CountRows(vault, "TransactionSplits").Should().Be(3);
        (await ReloadSplitCategoriesAsync(vault, id)).Should().BeEquivalentTo(["Groceries", "Household", "Transport"]);

        // 3 -> 1 (shrink)
        await MutateAsync(vault, id, transaction => transaction.ReplaceSplits(
        [
            new SplitDefinition(Euros(100m), "Everything")
        ]));

        CountRows(vault, "TransactionSplits").Should().Be(1, "the two removed splits must be deleted, not orphaned");
        (await ReloadSplitCategoriesAsync(vault, id)).Should().BeEquivalentTo(["Everything"]);

        // 1 -> 0 (empty)
        await MutateAsync(vault, id, transaction => transaction.ReplaceSplits([]));

        CountRows(vault, "TransactionSplits").Should().Be(0);
        (await ReloadSplitCategoriesAsync(vault, id)).Should().BeEmpty();
    }

    /// <summary>
    /// The same grow / shrink / clear proof for the second owned collection.
    /// </summary>
    /// <remarks>
    /// This used to cover growth only, because the only way to change the line item set was
    /// <c>AddLineItem</c> and there was no way to remove one. It now drives 2 -> 3 -> 1 -> 0 through
    /// <see cref="Transaction.ReplaceLineItems"/>, which closes the gap §12.7 recorded as
    /// INCOMPLETE. Every step also has to satisfy the TD-19 reconciliation invariant, so the sums
    /// below are not decoration: a set that did not add up to 100 EUR would be refused at the
    /// aggregate boundary before EF ever saw it.
    /// </remarks>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task LineItems_GrowThenShrinkThenClear_AreEachVisibleFromAFreshContext()
    {
        using var vault = new TempVault();
        await MigrateAsync(vault);

        var id = await SeedAsync(vault);
        CountRows(vault, "TransactionLineItems").Should().Be(2, "the insert path must write both line items");

        // 2 -> 3 (grow), with the third line an adjustment: 45 + 45 + 10 = 100.
        await MutateAsync(vault, id, transaction => transaction.ReplaceLineItems(
        [
            new LineItemDefinition("Bread", 2m, Euros(22.50m), Euros(45m)),
            new LineItemDefinition("Milk", 1m, Euros(45m), Euros(45m)),
            new LineItemDefinition("Tax", 1m, Euros(10m), Euros(10m), LineItemKind.Tax)
        ]));

        CountRows(vault, "TransactionLineItems").Should().Be(3);

        await using (var afterGrowth = new PineDbContext(vault.DatabasePath, vault.Key))
        {
            var grown = await afterGrowth.Transactions.AsNoTracking().SingleAsync(t => t.Id == id);
            grown.LineItems.Select(li => li.Description).Should().BeEquivalentTo(["Bread", "Milk", "Tax"]);
            grown.LineItems.Sum(li => li.TotalPrice.Amount).Should().Be(100m);
            grown.LineItems.Single(li => li.Description == "Tax").Kind.Should().Be(LineItemKind.Tax,
                "the kind must survive the round trip, or the adjustment is indistinguishable from a product");
        }

        // 3 -> 1 (shrink)
        await MutateAsync(vault, id, transaction => transaction.ReplaceLineItems(
        [
            new LineItemDefinition("Weekly shop", 1m, Euros(100m), Euros(100m))
        ]));

        CountRows(vault, "TransactionLineItems").Should().Be(1, "the two removed line items must be deleted, not orphaned");

        // 1 -> 0 (empty)
        await MutateAsync(vault, id, transaction => transaction.ReplaceLineItems([]));

        CountRows(vault, "TransactionLineItems").Should().Be(0);

        await using var fresh = new PineDbContext(vault.DatabasePath, vault.Key);
        var reloaded = await fresh.Transactions.AsNoTracking().SingleAsync(t => t.Id == id);
        reloaded.LineItems.Should().BeEmpty();
    }

    /// <summary>
    /// <c>ImportTransactionsCommand</c> builds its AI split from <c>tx.TotalAmount</c> itself, so the
    /// split's amount and the transaction's total are the SAME <see cref="Money"/> instance — and,
    /// via it, the same <see cref="CurrencyCode"/> instance. An owned entity carries identity, so a
    /// value object aliased between two owners can be written for one of them and left NULL for the
    /// other. This pins the real import shape rather than the convenient one where every amount is a
    /// freshly allocated object.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Splits_BuiltFromTheTransactionsOwnMoneyInstance_PersistTheirCurrency()
    {
        using var vault = new TempVault();
        await MigrateAsync(vault);

        var accountId = await SeedAccountAsync(vault);

        Guid id;
        await using (var context = new PineDbContext(vault.DatabasePath, vault.Key))
        {
            var repository = new SqliteTransactionRepository(context);
            await using var unitOfWork = new EfCoreUnitOfWork(context);

            var transaction = Transaction.Create(
                accountId,
                DateTimeOffset.UtcNow.AddDays(-1),
                "Supermarket",
                Euros(100m),
                TransactionType.Income,
                TimeProvider.System);

            // Exactly what ImportTransactionsCommand does.
            transaction.ReplaceSplits([new SplitDefinition(transaction.TotalAmount, "Groceries", "AI")]);
            transaction.EnsureValid();

            await unitOfWork.BeginTransactionAsync();
            await repository.SaveAsync(transaction);
            await unitOfWork.SaveChangesAsync();
            await unitOfWork.CommitAsync();
            id = transaction.Id;
        }

        await using var fresh = new PineDbContext(vault.DatabasePath, vault.Key);
        var reloaded = await fresh.Transactions.AsNoTracking().SingleAsync(t => t.Id == id);
        var split = reloaded.Splits.Single();
        split.Amount.Amount.Should().Be(100m);
        split.Amount.Currency.Value.Should().Be("EUR");
        reloaded.TotalAmount.Currency.Value.Should().Be("EUR");
    }

    /// <summary>
    /// Pins which aggregates have owned collections, because TD-13 is a defect of owned-collection
    /// UPDATES and every one of them needs a round-trip proof of its own.
    /// </summary>
    /// <remarks>
    /// <c>Transaction</c> was the only one until the Phase 3 rule engine gave <c>Rule</c> a
    /// <c>Conditions</c> collection. Its proof is
    /// <c>RuleStorageTests.TogglingARule_KeepsItsConditions</c> — the TD-13 shape exactly: the
    /// parent is updated while its owned children are loaded, which is where splits used to be
    /// deleted and re-inserted. <c>Account.Balance</c> and <c>Budget.Limit</c> are
    /// <c>ComplexProperty</c>, not navigations, and are therefore not affected.
    /// </remarks>
    [Fact]
    public void EfModel_DeclaresOwnedCollectionsOnTransactionOnly()
    {
        using var context = new PineDbContext(":memory:", new byte[32]);

        var ownedCollections = context.Model.GetEntityTypes()
            .Where(entityType => !entityType.IsOwned())
            .SelectMany(entityType => entityType.GetNavigations())
            .Where(navigation => navigation.IsCollection && navigation.TargetEntityType.IsOwned())
            .Select(navigation => $"{navigation.DeclaringEntityType.ClrType.Name}.{navigation.Name}")
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        ownedCollections.Should().Equal("Rule.Conditions", "Transaction.LineItems", "Transaction.Splits");
    }

    /// <summary>
    /// The split-sum invariant is no longer enforced eagerly by <c>AddSplit</c> (TD-04), so the
    /// repository is the last gate before an inconsistent aggregate reaches the database. This
    /// proves the gate lives on the write path itself and not merely on the aggregate: a
    /// half-allocated transaction is refused, and the vault stays empty afterwards.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task SaveAsync_RefusesATransactionWhoseSplitsDoNotSumToTheTotal()
    {
        using var vault = new TempVault();
        await MigrateAsync(vault);

        var accountId = await SeedAccountAsync(vault);

        await using var context = new PineDbContext(vault.DatabasePath, vault.Key);
        var repository = new SqliteTransactionRepository(context);

        var transaction = Transaction.Create(
            accountId,
            DateTimeOffset.UtcNow.AddDays(-1),
            "Supermarket",
            Euros(100m),
            TransactionType.Income,
            TimeProvider.System);

        // Only half the total is allocated. Under the old eager validation this threw inside
        // AddSplit, which is exactly what made a genuine two-way split impossible to build.
        transaction.AddSplit(Euros(50m), "Groceries");

        var save = async () => await repository.SaveAsync(transaction);

        await save.Should().ThrowAsync<InvalidOperationException>();
        CountRows(vault, "Transactions").Should().Be(0, "a refused aggregate must not reach the database");
    }

    private static async Task MigrateAsync(TempVault vault)
    {
        // The schema is created by the real EF Core migration, exactly as EfCoreVaultMigrator does
        // when a vault is opened in production.
        await using var context = new PineDbContext(vault.DatabasePath, vault.Key);
        await context.Database.MigrateAsync();
    }

    /// <summary>
    /// Writes the account every transaction in this file belongs to, and hands back its identifier.
    /// </summary>
    /// <remarks>
    /// <c>Transactions.AccountId</c> is a real foreign key with <c>DeleteBehavior.Restrict</c>
    /// (TD-30), so a transaction naming an account that was never written is refused by SQLite
    /// itself. The account is denominated in the same currency as the amounts below, which is the
    /// only shape production can reach.
    /// </remarks>
    /// <param name="vault">The throwaway encrypted vault under test.</param>
    /// <returns>The identifier of the persisted account.</returns>
    private static async Task<Guid> SeedAccountAsync(TempVault vault)
    {
        await using var context = new PineDbContext(vault.DatabasePath, vault.Key);
        var account = Account.Create("Current", AccountType.Bank, Euros(0m), TimeProvider.System);
        context.Accounts.Add(account);
        await context.SaveChangesAsync(CancellationToken.None);
        return account.Id;
    }

    /// <summary>
    /// Writes a 100 EUR transaction with two splits and two line items through one production-shaped
    /// unit of work, then lets the context go so nothing it tracked can leak into an assertion.
    /// </summary>
    /// <param name="vault">The throwaway encrypted vault under test.</param>
    /// <returns>The identifier of the persisted transaction.</returns>
    private static async Task<Guid> SeedAsync(TempVault vault)
    {
        var accountId = await SeedAccountAsync(vault);

        await using var context = new PineDbContext(vault.DatabasePath, vault.Key);
        var repository = new SqliteTransactionRepository(context);
        await using var unitOfWork = new EfCoreUnitOfWork(context);

        var transaction = Transaction.Create(
            accountId,
            DateTimeOffset.UtcNow.AddDays(-1),
            "Supermarket",
            Euros(100m),
            TransactionType.Income,
            TimeProvider.System);

        transaction.ReplaceSplits(
        [
            new SplitDefinition(Euros(50m), "Groceries", "half"),
            new SplitDefinition(Euros(50m), "Household", "other half")
        ]);
        transaction.AddLineItem("Bread", 2m, Euros(25m), Euros(50m));
        transaction.AddLineItem("Milk", 1m, Euros(50m), Euros(50m));
        transaction.EnsureValid();

        await unitOfWork.BeginTransactionAsync();
        await repository.SaveAsync(transaction);
        await unitOfWork.SaveChangesAsync();
        await unitOfWork.CommitAsync();

        return transaction.Id;
    }

    /// <summary>
    /// Runs one update the way a transactional request does: a fresh scope loads the aggregate
    /// through the repository, mutates it, hands it back and lets the unit of work commit.
    /// </summary>
    /// <param name="vault">The throwaway encrypted vault under test.</param>
    /// <param name="id">The transaction to update.</param>
    /// <param name="mutate">The mutation to apply to the loaded aggregate.</param>
    /// <returns>A task that completes once the update has been committed.</returns>
    private static async Task MutateAsync(TempVault vault, Guid id, Action<Transaction> mutate)
    {
        await using var context = new PineDbContext(vault.DatabasePath, vault.Key);
        var repository = new SqliteTransactionRepository(context);
        await using var unitOfWork = new EfCoreUnitOfWork(context);

        await unitOfWork.BeginTransactionAsync();

        var transaction = await repository.GetByIdAsync(id);
        transaction.Should().NotBeNull();

        mutate(transaction!);
        transaction!.EnsureValid();

        await repository.SaveAsync(transaction);
        await unitOfWork.SaveChangesAsync();
        await unitOfWork.CommitAsync();
    }

    /// <summary>
    /// Re-materialises the aggregate on a brand-new context, so the categories come from the rows
    /// and not from a change tracker that already knew the answer.
    /// </summary>
    /// <param name="vault">The throwaway encrypted vault under test.</param>
    /// <param name="id">The transaction to reload.</param>
    /// <returns>The persisted split categories.</returns>
    private static async Task<string[]> ReloadSplitCategoriesAsync(TempVault vault, Guid id)
    {
        await using var context = new PineDbContext(vault.DatabasePath, vault.Key);
        var reloaded = await context.Transactions.AsNoTracking().SingleAsync(t => t.Id == id);
        return reloaded.Splits.Select(s => s.Category).ToArray();
    }

    /// <summary>
    /// Counts rows over a raw SQLCipher connection, bypassing EF entirely. The vault holds exactly
    /// one transaction, so an unfiltered count is unambiguous and does not depend on how EF happens
    /// to encode a <see cref="Guid"/> in SQLite.
    /// </summary>
    /// <param name="vault">The throwaway encrypted vault under test.</param>
    /// <param name="table">The table to count.</param>
    /// <returns>The number of rows in the table.</returns>
    private static int CountRows(TempVault vault, string table)
    {
        using var connection = vault.OpenRaw(vault.DatabasePath, vault.Key);
        using var command = connection.CreateCommand();
        command.CommandText = $@"SELECT COUNT(*) FROM ""{table}"";";
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }
}
