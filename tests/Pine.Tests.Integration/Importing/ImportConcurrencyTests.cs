using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Pine.Application.Transactions.Commands;
using Pine.Core.Accounts;
using Pine.Core.Common;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Importing;
using Pine.Core.Transactions;
using Pine.Infrastructure.Data;
using Pine.Tests.Integration.Atomicity;
using Pine.Tests.Integration.Data;
using Xunit;

namespace Pine.Tests.Integration.Importing;

/// <summary>
/// TD-65: the import de-duplication check-then-insert race, closed by a partial unique index on
/// <c>(AccountId, ImportKey)</c> for the file-import key schemes.
/// </summary>
public class ImportConcurrencyTests
{
    private const string Source = "race-statement";
    private static readonly DateTimeOffset Day = new(2026, 3, 4, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The deterministic form of the race: two units of work both probe the key while it is absent,
    /// the first commits, and the second's insert must be refused rather than written.
    /// </summary>
    [Fact]
    public async Task A_second_insert_of_a_key_that_passed_a_stale_check_is_refused_and_nothing_is_written()
    {
        await using var host = await AtomicityHost.CreateAsync();
        var account = await host.SeedAccountAsync("Everyday", 1000m);
        var key = ImportKeyFactory.Derive(Day, -4.50m, "USD", "Coffee", Source, 0);

        using var scopeA = host.Services.CreateScope();
        using var scopeB = host.Services.CreateScope();
        var repoA = scopeA.ServiceProvider.GetRequiredService<ITransactionRepository>();
        var repoB = scopeB.ServiceProvider.GetRequiredService<ITransactionRepository>();

        // Both probes run before either insert: this is exactly the window the handler leaves open.
        (await repoA.GetExistingImportKeysAsync(account, new[] { key })).Should().BeEmpty();
        (await repoB.GetExistingImportKeysAsync(account, new[] { key })).Should().BeEmpty();

        await Insert(scopeA, repoA, account, key);

        var act = () => Insert(scopeB, repoB, account, key);
        await act.Should().ThrowAsync<ConcurrencyConflictException>();

        host.ReadImportKeysRaw().Should().Equal(key);
        host.CountRowsRaw("Transactions").Should().Be(1);
    }

    /// <summary>A receipt key (s1:) is deliberately exempt: saving a second copy after the warning is allowed.</summary>
    [Fact]
    public async Task Receipt_keys_are_not_unique()
    {
        await using var host = await AtomicityHost.CreateAsync();
        var account = await host.SeedAccountAsync("Everyday", 1000m);
        var key = ImportKeyFactory.FromReceipt(account, Day, -4.50m, "USD", "Cafe");

        for (var i = 0; i < 2; i++)
        {
            using var scope = host.Services.CreateScope();
            await Insert(scope, scope.ServiceProvider.GetRequiredService<ITransactionRepository>(), account, key);
        }

        host.CountRowsRaw("Transactions").Should().Be(2);
    }

    /// <summary>
    /// Two real imports of one file in flight at once through the full pipeline. Whatever the
    /// interleaving (one wins and one is refused, or the store serialises them), the vault must end
    /// with each row exactly once, and a follow-up import must find nothing left to add.
    /// </summary>
    [Fact]
    public async Task Two_concurrent_imports_of_one_file_never_write_a_row_twice()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await using var host = await AtomicityHost.CreateAsync();
            var account = await host.SeedAccountAsync("Everyday", 1000m);
            var importer = new Statement(("Coffee", -4.50m), ("Coffee", -4.50m), ("Books", -12.00m));

            using var gate = new Barrier(2);
            var tasks = Enumerable.Range(0, 2).Select(_ => Task.Run(async () =>
            {
                gate.SignalAndWait();
                try
                {
                    using var file = new MemoryStream();
                    await host.Mediator.Send(new ImportTransactionsCommand(
                        account, file, importer, ImportProfile.Create(Source, TimeProvider.System)));
                    return (Exception?)null;
                }
                catch (Exception ex)
                {
                    return ex;
                }
            })).ToArray();
            var outcomes = await Task.WhenAll(tasks);

            host.CountRowsRaw("Transactions").Should().BeLessThanOrEqualTo(3);

            // Retry as a user would: whatever is missing is added, nothing is duplicated.
            using var retryFile = new MemoryStream();
            await host.Mediator.Send(new ImportTransactionsCommand(
                account, retryFile, importer, ImportProfile.Create(Source, TimeProvider.System)));

            host.CountRowsRaw("Transactions").Should().Be(3);
            host.ReadImportKeysRaw().Should().OnlyHaveUniqueItems();
            (await host.ReadBalanceAsync(account)).Should().Be(1000m - 4.50m - 4.50m - 12.00m);
            outcomes.Should().Contain(o => o == null, "at least one of the two imports must have committed");
        }
    }

    /// <summary>
    /// A vault that already holds duplicate keys (the race happened before this fix) must still
    /// migrate: later duplicates lose their key, no transaction is deleted, receipts are untouched.
    /// </summary>
    [Fact]
    public async Task The_migration_demotes_pre_existing_duplicate_keys_instead_of_failing()
    {
        using var vault = new TempVault();
        const string previous = "20261011190857_AddBudgetUtcDates";
        Guid accountId;
        var dup = ImportKeyFactory.Derive(Day, -4.50m, "USD", "Coffee", Source, 0);
        string receipt;

        await using (var context = new PineDbContext(vault.DatabasePath, vault.Key))
        {
            await context.GetService<IMigrator>().MigrateAsync(previous, CancellationToken.None);
            var acct = Account.Create("Main", AccountType.Bank, new Money(100m, new CurrencyCode("USD")), TimeProvider.System);
            // Raw: the EF model maps the TD-77 opening-balance currency column, which this older schema lacks.
            await context.Database.ExecuteSqlRawAsync(
                @"INSERT INTO ""Accounts"" (""Id"", ""Name"", ""Type"", ""CreatedAt"", ""UpdatedAt"", ""OpeningBalanceAmount"", ""Balance_Amount"", ""Balance_Currency"")
                  VALUES ({0}, 'Main', 'Bank', '2026-01-01 00:00:00+00:00', '2026-01-01 00:00:00+00:00', 1000000, 1000000, 'USD');",
                acct.Id.ToString().ToUpperInvariant());
            accountId = acct.Id;

            // The TD-62 flag column exists only from a later migration; added for these writes, dropped again after.
            await context.Database.ExecuteSqlRawAsync(@"ALTER TABLE ""Transactions"" ADD COLUMN ""IsZeroAmountImport"" INTEGER NOT NULL DEFAULT 0;");

            receipt = ImportKeyFactory.FromReceipt(accountId, Day, -9m, "USD", "Cafe");
            foreach (var k in new[] { dup, dup, receipt, receipt })
            {
                var t = Transaction.Create(accountId, Day, "Coffee", new Money(-4.50m, new CurrencyCode("USD")),
                    TransactionType.Expense, TimeProvider.System);
                t.AssignImportKey(k);
                context.Transactions.Add(t);
                await context.SaveChangesAsync(); // one at a time: insertion order == rowid order
            }
            await context.Database.ExecuteSqlRawAsync(@"ALTER TABLE ""Transactions"" DROP COLUMN ""IsZeroAmountImport"";");
        }

        await using (var context = new PineDbContext(vault.DatabasePath, vault.Key))
        {
            await new EfCoreVaultMigrator(context, new VaultBackupService(context), PineTestOptions.Security)
                .MigrateAsync(CancellationToken.None);
        }

        await using var check = new PineDbContext(vault.DatabasePath, vault.Key);
        var rows = await check.Transactions.AsNoTracking().ToListAsync();
        rows.Should().HaveCount(4, "no transaction may be lost");
        rows.Count(r => r.ImportKey == dup).Should().Be(1);
        rows.Count(r => r.ImportKey is null).Should().Be(1);
        rows.Count(r => r.ImportKey == receipt).Should().Be(2);
    }

    private static async Task Insert(IServiceScope scope, ITransactionRepository repository, Guid account, string key)
    {
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var tx = Transaction.Create(account, Day, "Coffee", new Money(-4.50m, new CurrencyCode("USD")),
            TransactionType.Expense, TimeProvider.System);
        tx.AssignImportKey(key);

        await uow.BeginTransactionAsync();
        try
        {
            await repository.SaveAsync(tx);
            await uow.SaveChangesAsync();
            await uow.CommitAsync();
        }
        catch
        {
            await uow.RollbackAsync();
            throw;
        }
    }

    private sealed class Statement : IImporter
    {
        private readonly (string Payee, decimal Amount)[] _rows;

        public Statement(params (string Payee, decimal Amount)[] rows) => _rows = rows;

        public Task<IReadOnlyList<Transaction>> ImportAsync(
            Account targetAccount, Stream fileStream, ImportProfile profile, CancellationToken cancellationToken = default)
        {
            var currency = targetAccount.Balance.Currency;
            var list = new List<Transaction>();
            for (var i = 0; i < _rows.Length; i++)
            {
                var t = Transaction.Create(targetAccount.Id, Day, _rows[i].Payee, new Money(_rows[i].Amount, currency),
                    TransactionType.Expense, TimeProvider.System);
                t.AssignImportKey(ImportKeyFactory.Derive(Day, _rows[i].Amount, currency.Value, _rows[i].Payee, Source, i));
                list.Add(t);
            }

            return Task.FromResult<IReadOnlyList<Transaction>>(list);
        }
    }
}
