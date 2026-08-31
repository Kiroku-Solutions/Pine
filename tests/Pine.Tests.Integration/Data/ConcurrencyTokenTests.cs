using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Pine.Core.Accounts;
using Pine.Core.Common;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Transactions;
using Pine.Infrastructure.Data;
using Xunit;

namespace Pine.Tests.Integration.Data;

/// <summary>
/// TD-17: the optimistic concurrency token that turns a silent lost update into a refused write.
/// </summary>
/// <remarks>
/// <para>
/// THE DEFECT, AND THE CORRECTION TO HOW IT WAS RECORDED. The register said the lost update came
/// from "the background trainer contending with user saves". It does not:
/// <c>CategorizationTrainingBackgroundService</c> only reads, and its single write is one
/// <c>Setting</c> row holding a training watermark. The real contention is structural — every
/// request goes through <c>ScopedMediator</c>, which opens its own scope, its own
/// <c>PineDbContext</c> and its own database transaction, so two requests in flight read, mutate and
/// write the same row with no knowledge of each other.
/// </para>
/// <para>
/// Each test below uses two contexts over ONE vault file, which is exactly the shape two concurrent
/// scopes have. An in-memory or single-context test cannot reproduce this: the change tracker would
/// hand both callers the same instance and the second write would have nothing to conflict with.
/// </para>
/// </remarks>
public class ConcurrencyTokenTests
{
    private static readonly CurrencyCode Usd = new("USD");

    /// <summary>
    /// The headline case: two overlapping balance updates, and the second is refused rather than
    /// overwriting the first.
    /// </summary>
    [Fact]
    public async Task TwoOverlappingWritesToOneAccount_RefuseTheSecond_InsteadOfLosingTheFirst()
    {
        // Arrange
        using var vault = new TempVault();
        var accountId = await SeedAccountAsync(vault);

        await using var first = new PineDbContext(vault.DatabasePath, vault.Key);
        await using var second = new PineDbContext(vault.DatabasePath, vault.Key);

        // Both read the same row before either writes — the read-modify-write window.
        var asFirstSawIt = await first.Accounts.SingleAsync(a => a.Id == accountId, CancellationToken.None);
        var asSecondSawIt = await second.Accounts.SingleAsync(a => a.Id == accountId, CancellationToken.None);

        asFirstSawIt.Deposit(new Money(100m, Usd), TimeProvider.System);
        asSecondSawIt.Deposit(new Money(50m, Usd), TimeProvider.System);

        // Act
        await using var firstUnitOfWork = new EfCoreUnitOfWork(first);
        await firstUnitOfWork.SaveChangesAsync(CancellationToken.None);

        await using var secondUnitOfWork = new EfCoreUnitOfWork(second);
        var secondWrite = async () => await secondUnitOfWork.SaveChangesAsync(CancellationToken.None);

        // Assert
        await secondWrite.Should().ThrowAsync<ConcurrencyConflictException>();

        // The first writer's money is still there. Without the token the row would read 50 and the
        // 100 would be gone, with nothing anywhere reporting it.
        await using var reader = new PineDbContext(vault.DatabasePath, vault.Key);
        var reloaded = await reader.Accounts.AsNoTracking().SingleAsync(a => a.Id == accountId, CancellationToken.None);
        reloaded.Balance.Amount.Should().Be(100m);
    }

    /// <summary>
    /// The other half of the same claim: a token that refused everything would also pass the test
    /// above. Sequential writes — read, write, read again, write again — must be untouched.
    /// </summary>
    [Fact]
    public async Task SequentialWritesToOneAccount_AllSucceed()
    {
        // Arrange
        using var vault = new TempVault();
        var accountId = await SeedAccountAsync(vault);

        // Act: three separate read-modify-write cycles, each seeing the previous one's result.
        for (var i = 0; i < 3; i++)
        {
            await using var context = new PineDbContext(vault.DatabasePath, vault.Key);
            await using var unitOfWork = new EfCoreUnitOfWork(context);

            var account = await context.Accounts.SingleAsync(a => a.Id == accountId, CancellationToken.None);
            account.Deposit(new Money(10m, Usd), TimeProvider.System);

            await unitOfWork.SaveChangesAsync(CancellationToken.None);
        }

        // Assert
        await using var reader = new PineDbContext(vault.DatabasePath, vault.Key);
        var reloaded = await reader.Accounts.AsNoTracking().SingleAsync(a => a.Id == accountId, CancellationToken.None);
        reloaded.Balance.Amount.Should().Be(30m);
    }

    /// <summary>
    /// Two overlapping split edits conflict — but read the remarks before treating this as proof
    /// that the owner's token covers its children, because it is not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A falsification mutation removing the owner-promotion step left this test green, which is how
    /// its real mechanism came to light: <c>ReplaceSplits</c> discards the existing splits, so the
    /// second writer issues a DELETE for a row the first writer has already deleted, that DELETE
    /// affects zero rows, and EF reports a conflict on the CHILD. The owner's token is never
    /// consulted. The test is kept because the scenario is real and its outcome is correct, and its
    /// description is corrected because it was claiming credit for the wrong mechanism.
    /// </para>
    /// <para>
    /// The test below it — two writers each ADDING a line item — is the one that actually exercises
    /// the owner's token, because nothing is deleted for a child-level conflict to catch.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TwoOverlappingSplitEdits_Conflict_ThoughOnTheChildRowNotTheOwnersToken()
    {
        // Arrange
        using var vault = new TempVault();
        var (accountId, transactionId) = await SeedTransactionAsync(vault);

        await using var first = new PineDbContext(vault.DatabasePath, vault.Key);
        await using var second = new PineDbContext(vault.DatabasePath, vault.Key);

        // Through the repository, because that is the production write path and because it is what
        // corrects the state EF assigns to a newly added owned child (see its ReconcileOwnedChildren
        // remarks). Adding splits straight through the context hits that defect instead of this one.
        var firstRepository = new SqliteTransactionRepository(first);
        var secondRepository = new SqliteTransactionRepository(second);

        var asFirstSawIt = await firstRepository.GetByIdAsync(transactionId, CancellationToken.None);
        var asSecondSawIt = await secondRepository.GetByIdAsync(transactionId, CancellationToken.None);

        // Both re-describe the splits, and neither touches a scalar on the transaction itself.
        asFirstSawIt!.ReplaceSplits([new SplitDefinition(new Money(100m, Usd), "Groceries")]);
        asSecondSawIt!.ReplaceSplits([new SplitDefinition(new Money(100m, Usd), "Household")]);

        // Act
        await firstRepository.SaveAsync(asFirstSawIt, CancellationToken.None);
        await using var firstUnitOfWork = new EfCoreUnitOfWork(first);
        await firstUnitOfWork.SaveChangesAsync(CancellationToken.None);

        await secondRepository.SaveAsync(asSecondSawIt, CancellationToken.None);
        await using var secondUnitOfWork = new EfCoreUnitOfWork(second);
        var secondWrite = async () => await secondUnitOfWork.SaveChangesAsync(CancellationToken.None);

        // Assert
        await secondWrite.Should().ThrowAsync<ConcurrencyConflictException>();

        await using var reader = new PineDbContext(vault.DatabasePath, vault.Key);
        var reloaded = await reader.Transactions.AsNoTracking()
            .SingleAsync(t => t.Id == transactionId, CancellationToken.None);
        reloaded.Splits.Select(s => s.Category).Should().Equal("Groceries");

        _ = accountId;
    }

    /// <summary>
    /// The case that only the owner's token can catch, and the one that proves the aggregate — not
    /// the row — is the unit of concurrency.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two writers each ADD one line item to the same transaction. Nothing is deleted and nothing on
    /// the <c>Transactions</c> row itself changes, so there is no child-level conflict to fall back
    /// on: each writer simply INSERTs a row into <c>TransactionLineItems</c>.
    /// </para>
    /// <para>
    /// AND THE RESULT WOULD BE CORRUPT, WHICH IS WHY THIS MATTERS. Each writer's own set is
    /// internally valid — one line of 100 against a total of 100 — so
    /// <c>Transaction.EnsureValid</c> passes for both. Land them both and the transaction holds two
    /// line items summing to 200 against a total of 100, a state the aggregate forbids, reached
    /// without a single error anywhere. The owner's token is the only thing standing between those
    /// two writers.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TwoWritersEachAddingALineItem_RefuseTheSecond_BecauseTheAggregateIsTheUnitOfConcurrency()
    {
        // Arrange
        using var vault = new TempVault();
        var (_, transactionId) = await SeedTransactionAsync(vault);

        await using var first = new PineDbContext(vault.DatabasePath, vault.Key);
        await using var second = new PineDbContext(vault.DatabasePath, vault.Key);

        var firstRepository = new SqliteTransactionRepository(first);
        var secondRepository = new SqliteTransactionRepository(second);

        var asFirstSawIt = await firstRepository.GetByIdAsync(transactionId, CancellationToken.None);
        var asSecondSawIt = await secondRepository.GetByIdAsync(transactionId, CancellationToken.None);

        // Pure inserts. No deletion, so no child-level conflict can rescue this.
        asFirstSawIt!.AddLineItem("Bread", 1m, new Money(100m, Usd), new Money(100m, Usd));
        asSecondSawIt!.AddLineItem("Milk", 1m, new Money(100m, Usd), new Money(100m, Usd));

        // Act
        await firstRepository.SaveAsync(asFirstSawIt, CancellationToken.None);
        await using var firstUnitOfWork = new EfCoreUnitOfWork(first);
        await firstUnitOfWork.SaveChangesAsync(CancellationToken.None);

        await secondRepository.SaveAsync(asSecondSawIt, CancellationToken.None);
        await using var secondUnitOfWork = new EfCoreUnitOfWork(second);
        var secondWrite = async () => await secondUnitOfWork.SaveChangesAsync(CancellationToken.None);

        // Assert
        await secondWrite.Should().ThrowAsync<ConcurrencyConflictException>();

        // The aggregate still reconciles: one line of 100 against a total of 100.
        await using var reader = new PineDbContext(vault.DatabasePath, vault.Key);
        var reloaded = await reader.Transactions.AsNoTracking()
            .SingleAsync(t => t.Id == transactionId, CancellationToken.None);

        reloaded.LineItems.Should().HaveCount(1);
        reloaded.LineItems.Single().Description.Should().Be("Bread");
    }

    /// <summary>
    /// Deleting a row someone else has already changed is refused too. EF puts the token in the
    /// DELETE's WHERE clause, so this needs no separate mechanism — but it needs asserting, because
    /// nothing else would notice if it stopped working.
    /// </summary>
    [Fact]
    public async Task DeletingARowThatWasChangedAfterItWasRead_IsRefused()
    {
        // Arrange
        using var vault = new TempVault();
        var accountId = await SeedAccountAsync(vault);

        await using var deleter = new PineDbContext(vault.DatabasePath, vault.Key);
        await using var updater = new PineDbContext(vault.DatabasePath, vault.Key);

        var toDelete = await deleter.Accounts.SingleAsync(a => a.Id == accountId, CancellationToken.None);
        var toUpdate = await updater.Accounts.SingleAsync(a => a.Id == accountId, CancellationToken.None);

        toUpdate.Deposit(new Money(25m, Usd), TimeProvider.System);
        await using (var updaterUnitOfWork = new EfCoreUnitOfWork(updater))
        {
            await updaterUnitOfWork.SaveChangesAsync(CancellationToken.None);
        }

        // Act
        deleter.Accounts.Remove(toDelete);
        await using var deleterUnitOfWork = new EfCoreUnitOfWork(deleter);
        var deleting = async () => await deleterUnitOfWork.SaveChangesAsync(CancellationToken.None);

        // Assert
        await deleting.Should().ThrowAsync<ConcurrencyConflictException>();

        await using var reader = new PineDbContext(vault.DatabasePath, vault.Key);
        (await reader.Accounts.AsNoTracking().CountAsync(CancellationToken.None)).Should().Be(1);
    }

    /// <summary>
    /// The token changes on every write. If it did not, a stale reader would be accepted the second
    /// time round.
    /// </summary>
    [Fact]
    public async Task TheStoredToken_ChangesOnEveryUpdate()
    {
        // Arrange
        using var vault = new TempVault();
        var accountId = await SeedAccountAsync(vault);

        var afterInsert = ReadToken(vault);

        // Act
        await using (var context = new PineDbContext(vault.DatabasePath, vault.Key))
        await using (var unitOfWork = new EfCoreUnitOfWork(context))
        {
            var account = await context.Accounts.SingleAsync(a => a.Id == accountId, CancellationToken.None);
            account.Deposit(new Money(1m, Usd), TimeProvider.System);
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
        }

        var afterUpdate = ReadToken(vault);

        // Assert
        afterInsert.Should().NotBe(Guid.Empty, "a row inserted by this build must carry a real token");
        afterUpdate.Should().NotBe(afterInsert);
    }

    /// <summary>
    /// The token is a fact about the store, not about the domain. It must not have leaked into
    /// <c>Pine.Core</c> as a property callers can see or set.
    /// </summary>
    [Fact]
    public void TheToken_IsAShadowProperty_AndIsNotOnTheDomainTypes()
    {
        typeof(Account).GetProperty(PineDbContext.ConcurrencyTokenName).Should().BeNull();
        typeof(Transaction).GetProperty(PineDbContext.ConcurrencyTokenName).Should().BeNull();
    }

    /// <summary>
    /// Owned types are not roots and must not carry their own token — their owner's covers them.
    /// </summary>
    [Fact]
    public async Task OwnedTables_DoNotCarryTheirOwnToken()
    {
        using var vault = new TempVault();
        await SeedTransactionAsync(vault);

        ColumnNames(vault, "TransactionSplits").Should().NotContain(PineDbContext.ConcurrencyTokenName);
        ColumnNames(vault, "Transactions").Should().Contain(PineDbContext.ConcurrencyTokenName);
    }

    // ------------------------------------------------------------------ seeding

    private static async Task<Guid> SeedAccountAsync(TempVault vault)
    {
        await using var context = new PineDbContext(vault.DatabasePath, vault.Key);
        await context.Database.MigrateAsync(CancellationToken.None);

        var account = Account.Create("Current", AccountType.Bank, new Money(0m, Usd), TimeProvider.System);
        context.Accounts.Add(account);
        await context.SaveChangesAsync(CancellationToken.None);

        return account.Id;
    }

    private static async Task<(Guid AccountId, Guid TransactionId)> SeedTransactionAsync(TempVault vault)
    {
        var accountId = await SeedAccountAsync(vault);

        await using var context = new PineDbContext(vault.DatabasePath, vault.Key);
        var transaction = Transaction.Create(
            accountId,
            DateTimeOffset.UtcNow.AddDays(-1),
            "Supermarket",
            new Money(100m, Usd),
            TransactionType.Income,
            TimeProvider.System);
        transaction.ReplaceSplits([new SplitDefinition(new Money(100m, Usd), "Unassigned")]);

        context.Transactions.Add(transaction);
        await context.SaveChangesAsync(CancellationToken.None);

        return (accountId, transaction.Id);
    }

    // ------------------------------------------------------------------ raw reads

    private static Guid ReadToken(TempVault vault)
    {
        using var connection = vault.OpenRaw(vault.DatabasePath, vault.Key);
        using var command = connection.CreateCommand();
        command.CommandText = @"SELECT ""Version"" FROM ""Accounts"" LIMIT 1;";
        return Guid.Parse((string)command.ExecuteScalar()!);
    }

    private static System.Collections.Generic.List<string> ColumnNames(TempVault vault, string table)
    {
        using var connection = vault.OpenRaw(vault.DatabasePath, vault.Key);
        using var command = connection.CreateCommand();
        command.CommandText = $@"SELECT name FROM pragma_table_info('{table}');";

        var names = new System.Collections.Generic.List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }
}
