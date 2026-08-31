using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Pine.Core.Accounts;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Importing;
using Pine.Core.Transactions;
using Pine.Infrastructure.Data;
using Pine.Tests.Integration.Data;
using Xunit;

namespace Pine.Tests.Integration.Repositories;

/// <summary>
/// <see cref="SqliteTransactionRepository.GetByImportKeyAsync"/> against a real encrypted vault
/// (TD-20): the lookup the receipt duplicate check is built on.
/// </summary>
/// <remarks>
/// <para>
/// A FILE VAULT, NOT <c>":memory:"</c>, and the lookup runs through a DIFFERENT
/// <see cref="PineDbContext"/> than the one that wrote — the same reasoning
/// <c>TransactionOwnedCollectionRoundTripTests</c> documents: a lookup answered from the writer's own
/// identity map would pass even if the SQL underneath were wrong.
/// </para>
/// <para>
/// No new migration is exercised here on purpose: the <c>(AccountId, ImportKey)</c> index this
/// lookup relies on already exists from migration <c>20260827141109</c>, the same one
/// <c>GetExistingImportKeysAsync</c> uses.
/// </para>
/// </remarks>
public class ReceiptDuplicateLookupTests
{
    private static CurrencyCode Usd => new("USD");

    private static Money Dollars(decimal amount) => new(amount, Usd);

    [Fact]
    public async Task A_transaction_saved_with_a_receipt_key_is_found_by_it()
    {
        using var vault = new TempVault();
        await MigrateAsync(vault);

        var accountId = await SeedAccountAsync(vault, "Current");
        var date = new DateTimeOffset(2026, 3, 5, 0, 0, 0, TimeSpan.Zero);
        var key = ImportKeyFactory.FromReceipt(accountId, date, -24.50m, "USD", "ACME");

        await SeedTransactionAsync(vault, accountId, date, "ACME", -24.50m, key);

        await using var readContext = new PineDbContext(vault.DatabasePath, vault.Key);
        var repository = new SqliteTransactionRepository(readContext);

        var found = await repository.GetByImportKeyAsync(accountId, key, CancellationToken.None);

        found.Should().NotBeNull();
        found!.ImportKey.Should().Be(key);
        found.Payee.Should().Be("ACME");
        found.TotalAmount.Amount.Should().Be(-24.50m);
    }

    [Fact]
    public async Task A_key_no_transaction_carries_is_not_found()
    {
        using var vault = new TempVault();
        await MigrateAsync(vault);

        var accountId = await SeedAccountAsync(vault, "Current");

        await using var readContext = new PineDbContext(vault.DatabasePath, vault.Key);
        var repository = new SqliteTransactionRepository(readContext);

        var found = await repository.GetByImportKeyAsync(
            accountId, ImportKeyFactory.FromReceipt(accountId, DateTimeOffset.UtcNow, -1m, "USD", "Nobody"), CancellationToken.None);

        found.Should().BeNull();
    }

    [Fact]
    public async Task The_lookup_is_scoped_to_the_account()
    {
        using var vault = new TempVault();
        await MigrateAsync(vault);

        var accountId = await SeedAccountAsync(vault, "Current");
        var otherAccountId = await SeedAccountAsync(vault, "Savings");
        var date = new DateTimeOffset(2026, 3, 5, 0, 0, 0, TimeSpan.Zero);
        var key = ImportKeyFactory.FromReceipt(accountId, date, -24.50m, "USD", "ACME");

        await SeedTransactionAsync(vault, accountId, date, "ACME", -24.50m, key);

        await using var readContext = new PineDbContext(vault.DatabasePath, vault.Key);
        var repository = new SqliteTransactionRepository(readContext);

        var found = await repository.GetByImportKeyAsync(otherAccountId, key, CancellationToken.None);

        found.Should().BeNull("the same key on a different account is not the same purchase");
    }

    private static async Task MigrateAsync(TempVault vault)
    {
        await using var context = new PineDbContext(vault.DatabasePath, vault.Key);
        await context.Database.MigrateAsync();
    }

    private static async Task<Guid> SeedAccountAsync(TempVault vault, string name)
    {
        await using var context = new PineDbContext(vault.DatabasePath, vault.Key);
        var account = Account.Create(name, AccountType.Bank, Dollars(0m), TimeProvider.System);
        context.Accounts.Add(account);
        await context.SaveChangesAsync(CancellationToken.None);
        return account.Id;
    }

    private static async Task<Guid> SeedTransactionAsync(
        TempVault vault, Guid accountId, DateTimeOffset date, string payee, decimal amount, string importKey)
    {
        await using var context = new PineDbContext(vault.DatabasePath, vault.Key);
        var repository = new SqliteTransactionRepository(context);
        await using var unitOfWork = new EfCoreUnitOfWork(context);

        var transaction = Transaction.Create(
            accountId, date, payee, Dollars(amount), TransactionType.Expense, TimeProvider.System);
        transaction.AddSplit(Dollars(amount), "Groceries");
        transaction.AssignImportKey(importKey);
        transaction.EnsureValid();

        await unitOfWork.BeginTransactionAsync();
        await repository.SaveAsync(transaction);
        await unitOfWork.SaveChangesAsync();
        await unitOfWork.CommitAsync();

        return transaction.Id;
    }
}
