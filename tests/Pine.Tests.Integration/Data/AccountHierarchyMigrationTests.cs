using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Pine.Core.Accounts;
using Pine.Core.Common.ValueObjects;
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
}
