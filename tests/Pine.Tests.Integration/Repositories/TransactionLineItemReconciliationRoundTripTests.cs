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
/// Persistence proof for the line item reconciliation model (TD-19): the kind survives the round
/// trip, a taxed receipt stores and reloads, and a vault written before the model existed still
/// opens AND can still be re-saved.
/// </summary>
/// <remarks>
/// <para>
/// EVERY ASSERTION IS MADE FROM A DIFFERENT CONNECTION THAN THE ONE THAT WROTE, and the column
/// contents are read over a raw SQLCipher connection that bypasses EF entirely. Re-reading through
/// the writing context would hand back the in-memory graph whatever SQL ran, so a mapping that
/// dropped the new column would still pass. A file vault, not <c>":memory:"</c>, for the reason
/// documented on <see cref="TransactionOwnedCollectionRoundTripTests"/>.
/// </para>
/// <para>
/// THE LEGACY TEST IS THE IMPORTANT ONE. Making the invariant exact without a legacy seam would have
/// made every scanned receipt already in a vault impossible to void or correct -- TD-02 silently
/// undone by a new invariant. That claim is only worth anything if the legacy shape is planted the
/// way a previous build would have written it, which here means raw SQL over a keyed connection,
/// with a NULL <c>Kind</c>, and products that do not add up.
/// </para>
/// </remarks>
public class TransactionLineItemReconciliationRoundTripTests
{
    private static CurrencyCode Eur => new("EUR");

    private static Money Euros(decimal amount) => new(amount, Eur);

    /// <summary>
    /// A taxed receipt -- products that do not add up, plus the tax line that makes them -- persists
    /// and comes back with its kinds intact.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task ATaxedReceipt_RoundTrips_WithItsAdjustmentLineDistinguishable()
    {
        using var vault = new TempVault();
        await MigrateAsync(vault);

        var id = await SeedTaxedReceiptAsync(vault);

        await using var fresh = new PineDbContext(vault.DatabasePath, vault.Key);
        var reloaded = await fresh.Transactions.AsNoTracking().SingleAsync(t => t.Id == id);

        reloaded.LineItems.Should().HaveCount(3);
        reloaded.LineItems.Sum(li => li.TotalPrice.Amount).Should().Be(-121m);
        reloaded.LineItems.Single(li => li.Kind == LineItemKind.Tax).TotalPrice.Should().Be(Euros(-21m));
        reloaded.LineItems.Count(li => li.Kind == LineItemKind.Product).Should().Be(2);
        reloaded.HasLineItemsPredatingKinds.Should().BeFalse();

        // The reloaded aggregate must satisfy the invariant it was written under; if it did not,
        // voiding it -- which re-saves it -- would be impossible.
        reloaded.Invoking(t => t.EnsureValid()).Should().NotThrow();

        // And the kind really is on disk, not merely in the change tracker.
        ReadKindsRaw(vault).Should().BeEquivalentTo(["Product", "Product", "Tax"]);
    }

    /// <summary>
    /// A transaction whose line items were written before <see cref="LineItemKind"/> existed still
    /// opens, and can still be saved again.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task ALegacyReceiptWhoseProductsDoNotAddUp_StillOpensAndStillReSaves()
    {
        using var vault = new TempVault();
        await MigrateAsync(vault);

        var id = await SeedTaxedReceiptAsync(vault);

        // Turn the persisted receipt into the shape a previous build would have written: no kinds at
        // all, and no tax line, so the products fall 21 EUR short of the total.
        vault.ExecuteSql(@"DELETE FROM ""TransactionLineItems"" WHERE ""Kind"" = 'Tax';");
        vault.ExecuteSql(@"UPDATE ""TransactionLineItems"" SET ""Kind"" = NULL;");

        await using (var afterPlanting = new PineDbContext(vault.DatabasePath, vault.Key))
        {
            var legacy = await afterPlanting.Transactions.AsNoTracking().SingleAsync(t => t.Id == id);

            legacy.LineItems.Should().HaveCount(2);
            legacy.LineItems.Should().OnlyContain(li => li.Kind == null);
            legacy.HasLineItemsPredatingKinds.Should().BeTrue();

            // Anti-vacuity: the same numbers WOULD be refused if the kinds were known, so this test
            // cannot pass merely because the sum happens to work out.
            legacy.LineItemDifference().Should().Be(Euros(-21m));
            legacy.Invoking(t => t.EnsureValid()).Should().NotThrow();
        }

        // The re-save path, which is what voiding and every correction command runs. Before the
        // legacy seam existed this threw, and a pre-existing scanned receipt became uncorrectable.
        await MutateAsync(vault, id, transaction => transaction.Void("Wrong card", TimeProvider.System));

        await using var afterVoid = new PineDbContext(vault.DatabasePath, vault.Key);
        var voided = await afterVoid.Transactions.AsNoTracking().SingleAsync(t => t.Id == id);
        voided.IsVoided.Should().BeTrue();
        voided.LineItems.Should().HaveCount(2, "voiding must not disturb the itemisation it could not validate");
        ReadKindsRaw(vault).Should().BeEquivalentTo([null, null]);
    }

    /// <summary>
    /// The invariant really is enforced at the persistence boundary, not only in the aggregate.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task SaveAsync_RefusesATransactionWhoseLineItemsDoNotSumToTheTotal()
    {
        using var vault = new TempVault();
        await MigrateAsync(vault);

        var accountId = await SeedAccountAsync(vault);

        await using var context = new PineDbContext(vault.DatabasePath, vault.Key);
        var repository = new SqliteTransactionRepository(context);

        var transaction = NewReceipt(accountId);
        transaction.AddLineItem("Bread", 1m, Euros(-100m), Euros(-100m));

        var act = async () => await repository.SaveAsync(transaction);

        await act.Should().ThrowAsync<InvalidOperationException>();
        CountRows(vault, "TransactionLineItems").Should().Be(0, "a refused save must not have staged anything");
    }

    /// <summary>
    /// The repository's DETACHED merge path preserves each line's kind. This is a different branch
    /// of <see cref="SqliteTransactionRepository.SaveAsync"/> than every other test in this file
    /// exercises, and until this test existed nothing covered it.
    /// </summary>
    /// <remarks>
    /// WHY THIS IS A SEPARATE TEST AND NOT A VARIATION. <c>SaveAsync</c> branches on
    /// <c>ReferenceEquals(existing, transaction)</c>. Loading through the repository inside the same
    /// context hands back the TRACKED instance, so mutating it takes the equal-reference path and
    /// never rebuilds a single <see cref="LineItemDefinition"/>. Only an aggregate read on one
    /// context and saved on another reaches the branch that re-describes the children -- which is
    /// the only place in the solution where a kind could be silently dropped, because it is the only
    /// place a line item is reconstructed from one that already exists. Dropping the kind argument
    /// there flattens every line to <see cref="LineItemKind.Product"/>, and nothing else in the
    /// suite would notice.
    /// </remarks>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task TheDetachedMergePath_PreservesEachLinesKind()
    {
        using var vault = new TempVault();
        await MigrateAsync(vault);

        var id = await SeedTaxedReceiptAsync(vault);

        Transaction detached;
        await using (var reading = new PineDbContext(vault.DatabasePath, vault.Key))
        {
            detached = await reading.Transactions.AsNoTracking().SingleAsync(t => t.Id == id);
        }

        // Anti-vacuity: the aggregate about to be merged really does carry a non-default kind, so a
        // green result cannot mean "everything was Product all along".
        detached.LineItems.Should().ContainSingle(li => li.Kind == LineItemKind.Tax);

        await using (var writing = new PineDbContext(vault.DatabasePath, vault.Key))
        {
            var repository = new SqliteTransactionRepository(writing);
            await using var unitOfWork = new EfCoreUnitOfWork(writing);

            // Proves the detached branch is the one under test: the context doing the writing has
            // never seen this row, so FindAsync must query and return a different instance.
            var tracked = await writing.Transactions.SingleAsync(t => t.Id == id);
            ReferenceEquals(tracked, detached).Should().BeFalse();

            detached.Void("Wrong card", TimeProvider.System);

            await unitOfWork.BeginTransactionAsync();
            await repository.SaveAsync(detached);
            await unitOfWork.SaveChangesAsync();
            await unitOfWork.CommitAsync();
        }

        ReadKindsRaw(vault).Should().BeEquivalentTo(
            ["Product", "Product", "Tax"],
            "a merge re-describes the children it just read, and must not relabel them");
    }

    /// <summary>
    /// A legacy receipt merged through the DETACHED path keeps its absent kinds absent, and so stays
    /// exempt from the reconciliation invariant it was never able to satisfy.
    /// </summary>
    /// <remarks>
    /// This is the expensive half of the same defect. Flattening a null kind to
    /// <see cref="LineItemKind.Product"/> on the merge path does not merely mislabel a row: it
    /// asserts the transaction reconciles, and a legacy receipt whose products fall short of its
    /// total then throws on save. A correction path that worked would start failing on exactly the
    /// data the legacy seam exists to protect -- TD-02 undone by TD-19, on one branch only.
    /// </remarks>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task TheDetachedMergePath_KeepsALegacyReceiptExemptRatherThanRefusingIt()
    {
        using var vault = new TempVault();
        await MigrateAsync(vault);

        var id = await SeedTaxedReceiptAsync(vault);

        vault.ExecuteSql(@"DELETE FROM ""TransactionLineItems"" WHERE ""Kind"" = 'Tax';");
        vault.ExecuteSql(@"UPDATE ""TransactionLineItems"" SET ""Kind"" = NULL;");

        Transaction detached;
        await using (var reading = new PineDbContext(vault.DatabasePath, vault.Key))
        {
            detached = await reading.Transactions.AsNoTracking().SingleAsync(t => t.Id == id);
        }

        detached.HasLineItemsPredatingKinds.Should().BeTrue();
        detached.LineItemDifference().Should().Be(Euros(-21m), "the planted rows must not add up, or this proves nothing");

        await using (var writing = new PineDbContext(vault.DatabasePath, vault.Key))
        {
            var repository = new SqliteTransactionRepository(writing);
            await using var unitOfWork = new EfCoreUnitOfWork(writing);

            await writing.Transactions.SingleAsync(t => t.Id == id);
            detached.Void("Wrong card", TimeProvider.System);

            await unitOfWork.BeginTransactionAsync();

            var act = async () => await repository.SaveAsync(detached);
            await act.Should().NotThrowAsync("a merge must carry an absent kind through, not invent one");

            await unitOfWork.SaveChangesAsync();
            await unitOfWork.CommitAsync();
        }

        ReadKindsRaw(vault).Should().BeEquivalentTo([null, null]);

        await using var afterVoid = new PineDbContext(vault.DatabasePath, vault.Key);
        var voided = await afterVoid.Transactions.AsNoTracking().SingleAsync(t => t.Id == id);
        voided.IsVoided.Should().BeTrue();
    }

    private static Transaction NewReceipt(Guid accountId) => Transaction.Create(
        accountId,
        DateTimeOffset.UtcNow.AddDays(-1),
        "Supermarket",
        Euros(-121m),
        TransactionType.Expense,
        TimeProvider.System);

    /// <summary>
    /// Writes the account every receipt in this file belongs to, and hands back its identifier.
    /// </summary>
    /// <remarks>
    /// <c>Transactions.AccountId</c> is a real foreign key with <c>DeleteBehavior.Restrict</c>
    /// (TD-30), so a receipt naming an account that was never written is refused by SQLite itself.
    /// The account is denominated in the same currency as the receipt, which is the only shape
    /// production can reach.
    /// </remarks>
    private static async Task<Guid> SeedAccountAsync(TempVault vault)
    {
        await using var context = new PineDbContext(vault.DatabasePath, vault.Key);
        var account = Account.Create("Current", AccountType.Bank, Euros(0m), TimeProvider.System);
        context.Accounts.Add(account);
        await context.SaveChangesAsync(CancellationToken.None);
        return account.Id;
    }

    private static async Task<Guid> SeedTaxedReceiptAsync(TempVault vault)
    {
        var accountId = await SeedAccountAsync(vault);

        await using var context = new PineDbContext(vault.DatabasePath, vault.Key);
        var repository = new SqliteTransactionRepository(context);
        await using var unitOfWork = new EfCoreUnitOfWork(context);

        var transaction = NewReceipt(accountId);
        transaction.AddSplit(Euros(-121m), "Groceries");
        transaction.AddLineItem("Bread", 2m, Euros(-25m), Euros(-50m));
        transaction.AddLineItem("Milk", 1m, Euros(-50m), Euros(-50m));
        transaction.AddLineItem("VAT 21%", 1m, Euros(-21m), Euros(-21m), LineItemKind.Tax);

        await unitOfWork.BeginTransactionAsync();
        await repository.SaveAsync(transaction);
        await unitOfWork.SaveChangesAsync();
        await unitOfWork.CommitAsync();

        return transaction.Id;
    }

    private static async Task MigrateAsync(TempVault vault)
    {
        await using var context = new PineDbContext(vault.DatabasePath, vault.Key);
        await context.Database.MigrateAsync();
    }

    private static async Task MutateAsync(TempVault vault, Guid id, Action<Transaction> mutate)
    {
        await using var context = new PineDbContext(vault.DatabasePath, vault.Key);
        var repository = new SqliteTransactionRepository(context);
        await using var unitOfWork = new EfCoreUnitOfWork(context);

        await unitOfWork.BeginTransactionAsync();

        var transaction = await repository.GetByIdAsync(id);
        transaction.Should().NotBeNull();

        mutate(transaction!);

        await repository.SaveAsync(transaction!);
        await unitOfWork.SaveChangesAsync();
        await unitOfWork.CommitAsync();
    }

    /// <summary>
    /// Reads the <c>Kind</c> column over a raw SQLCipher connection, bypassing EF and its mapping.
    /// </summary>
    private static string?[] ReadKindsRaw(TempVault vault)
    {
        using var connection = vault.OpenRaw(vault.DatabasePath, vault.Key);
        using var command = connection.CreateCommand();
        command.CommandText = @"SELECT ""Kind"" FROM ""TransactionLineItems"" ORDER BY ROWID;";

        var kinds = new System.Collections.Generic.List<string?>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            kinds.Add(reader.IsDBNull(0) ? null : reader.GetString(0));
        }

        return kinds.ToArray();
    }

    private static int CountRows(TempVault vault, string table)
    {
        using var connection = vault.OpenRaw(vault.DatabasePath, vault.Key);
        using var command = connection.CreateCommand();
        command.CommandText = $@"SELECT COUNT(*) FROM ""{table}"";";
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }
}
