using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Pine.Application.Planning;
using Pine.Application.Transactions.Commands;
using Pine.Core.Accounts;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Planning;
using Pine.Core.Transactions;
using Pine.Infrastructure.Data;
using Xunit;

namespace Pine.Tests.Integration.Data;

public sealed class AccountHierarchyMigrationTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"pine-hierarchy-{Guid.NewGuid():N}.db");
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
        }
        catch (IOException)
        {
            // Best-effort temp file cleanup on Windows SQLite pool release.
        }
    }

    [Fact]
    public async Task MigrateAndPersist_ParentSubAccountAndFilteredTransactionsQuery_WorkEndToEnd()
    {
        var usd = new CurrencyCode("USD");
        var eur = new CurrencyCode("EUR");

        await using (var context = new PineDbContext(_dbPath, _key))
        {
            await context.Database.MigrateAsync();

            var parent = Account.Create("Cuenta Principal", AccountType.Bank, new Money(1800m, usd), TimeProvider.System);
            var subMed = Account.Create("Medicina", AccountType.Bank, new Money(0m, usd), TimeProvider.System, parent.Id, 250m);
            var foreign = Account.Create("CDT Exterior", AccountType.Investment, new Money(5000m, eur), TimeProvider.System);

            context.Accounts.AddRange(parent, subMed, foreign);

            var tx1 = Transaction.Create(subMed.Id, DateTimeOffset.UtcNow, "Farmacia Fybeca", new Money(-45m, usd), TransactionType.Expense, TimeProvider.System, "Vitaminas");
            tx1.AddSplit(new Money(-45m, usd), "Salud", string.Empty);

            var tx2 = Transaction.Create(parent.Id, DateTimeOffset.UtcNow, "Salario Mensual", new Money(1800m, usd), TransactionType.Income, TimeProvider.System, string.Empty);
            tx2.AddSplit(new Money(1800m, usd), "Ingreso", string.Empty);

            var tx3 = Transaction.Create(foreign.Id, DateTimeOffset.UtcNow, "Intereses CDT", new Money(120m, eur), TransactionType.Income, TimeProvider.System, string.Empty);
            tx3.AddSplit(new Money(120m, eur), "Inversiones", string.Empty);

            context.Transactions.AddRange(tx1, tx2, tx3);
            await context.SaveChangesAsync();
        }

        await using (var verify = new PineDbContext(_dbPath, _key))
        {
            var accounts = await verify.Accounts.AsNoTracking().ToListAsync();
            var parent = accounts.Single(a => a.Name == "Cuenta Principal");
            var subMed = accounts.Single(a => a.Name == "Medicina");

            parent.ParentAccountId.Should().BeNull();
            parent.IsSubAccount.Should().BeFalse();
            subMed.ParentAccountId.Should().Be(parent.Id);
            subMed.IsSubAccount.Should().BeTrue();
            subMed.MonthlyAllocation.Should().Be(250m);

            var txRepo = new SqliteTransactionRepository(verify);

            // Filter by parent + sub-account tree
            var principalTree = await txRepo.GetPagedAsync(1, 20, includeVoided: false, new[] { parent.Id, subMed.Id }, null, null);
            principalTree.TotalCount.Should().Be(2);
            principalTree.Items.Select(t => t.Payee).Should().Contain("Farmacia Fybeca").And.Contain("Salario Mensual");

            // Filter by type Expense + search by category "Salud"
            var healthExpenses = await txRepo.GetPagedAsync(1, 20, includeVoided: false, null, TransactionType.Expense, "Salud");
            healthExpenses.TotalCount.Should().Be(1);
            healthExpenses.Items[0].Payee.Should().Be("Farmacia Fybeca");
        }
    }

    [Fact]
    public async Task EditScannedReceiptWithLineItems_AndLinkedGoalContributionDeficit_PersistCleanly()
    {
        var usd = new CurrencyCode("USD");
        Guid accountId;
        Guid receiptTxId;
        Guid goalId;

        await using (var setup = new PineDbContext(_dbPath, _key))
        {
            await setup.Database.MigrateAsync();

            var account = Account.Create("Banco Pichincha", AccountType.Bank, new Money(100m, usd), TimeProvider.System);
            accountId = account.Id;
            setup.Accounts.Add(account);

            var receipt = Transaction.Create(account.Id, DateTimeOffset.UtcNow.AddDays(-2), "East Repair Inc", new Money(-154.06m, usd), TransactionType.Expense, TimeProvider.System, "");
            receipt.AddSplit(new Money(-154.06m, usd), "Medicina", "");
            receipt.AddLineItem("Front Brake", 1m, new Money(-100m, usd), new Money(-100m, usd), LineItemKind.Product);
            receipt.AddLineItem("New Blades", 2m, new Money(-15m, usd), new Money(-30m, usd), LineItemKind.Product);
            receipt.AddLineItem("Labor", 3m, new Money(-5m, usd), new Money(-15m, usd), LineItemKind.Product);
            receipt.AddLineItem("Sales Tax", 1m, new Money(-9.06m, usd), new Money(-9.06m, usd), LineItemKind.Tax);
            receiptTxId = receipt.Id;
            account.Withdraw(new Money(154.06m, usd), TimeProvider.System);
            setup.Transactions.Add(receipt);

            var goal = SavingsGoal.Create("Fondo Emergencia", new Money(500m, usd), new Money(40m, usd), linkedAccountId: account.Id, timeProvider: TimeProvider.System);
            goalId = goal.Id;
            setup.SavingsGoals.Add(goal);

            await setup.SaveChangesAsync();
        }

        // 1. Edit both Date and Amount on the scanned receipt (previously threw LineItemSumFailure!)
        await using (var editCtx = new PineDbContext(_dbPath, _key))
        {
            var txRepo = new SqliteTransactionRepository(editCtx);
            var acctRepo = new SqliteAccountRepository(editCtx);
            var handler = new UpdateTransactionCommandHandler(txRepo, acctRepo, TimeProvider.System);

            var existing = await txRepo.GetByIdAsync(receiptTxId);
            var splitId = existing!.Splits.Single().Id;
            var newDate = DateTimeOffset.UtcNow.AddDays(-5);

            await handler.Handle(
                new UpdateTransactionCommand(
                    receiptTxId,
                    newDate,
                    "East Repair Inc",
                    -120.00m,
                    "USD",
                    "Updated receipt",
                    [new UpdateTransactionSplitCommand(splitId, -120.00m, "Medicina", "Updated receipt")]),
                default);

            await editCtx.SaveChangesAsync();
        }

        // 2. Contribute $40 to the linked savings goal -> withdraws from Banco Pichincha (driving real deficit)
        await using (var contribCtx = new PineDbContext(_dbPath, _key))
        {
            var goalsRepo = new SqliteSavingsGoalRepository(contribCtx);
            var acctRepo = new SqliteAccountRepository(contribCtx);
            var txRepo = new SqliteTransactionRepository(contribCtx);
            var handler = new ContributeSavingsGoalCommandHandler(goalsRepo, acctRepo, txRepo, TimeProvider.System);

            await handler.Handle(new ContributeSavingsGoalCommand(goalId, 40m), default);
            await contribCtx.SaveChangesAsync();
        }

        await using (var verify = new PineDbContext(_dbPath, _key))
        {
            var updatedTx = await verify.Transactions.Include(t => t.Splits).SingleAsync(t => t.Id == receiptTxId);
            updatedTx.TotalAmount.Amount.Should().Be(-120.00m);
            updatedTx.LineItems.Sum(li => li.TotalPrice.Amount).Should().Be(-120.00m);

            var acct = await verify.Accounts.SingleAsync(a => a.Id == accountId);
            // 100 opening - 120 edited receipt - 40 savings contribution = -60.00 deficit
            acct.Balance.Amount.Should().Be(-60.00m);

            var goal = await verify.SavingsGoals.SingleAsync(g => g.Id == goalId);
            goal.CurrentAmount.Amount.Should().Be(40m);

            var allTxs = await verify.Transactions.AsNoTracking().Where(t => t.AccountId == accountId).ToListAsync();
            allTxs.Should().HaveCount(2);
            allTxs.Should().Contain(t => t.Payee == "Fondo Emergencia" && t.TotalAmount.Amount == -40m);
        }
    }
}
