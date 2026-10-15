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
using Pine.Tests.Integration.Data;
using Xunit;

namespace Pine.Tests.Integration.Repositories;

/// <summary>
/// The store-side count and the paged read the categoriser trains from (TD-133), against a real vault.
/// </summary>
public class CategorisedTransactionPagingTests
{
    [Fact]
    public async Task CountAndPages_SeeOnlyCategorisedTransactions_AndPagesPartitionThemWithoutOverlap()
    {
        using var vault = new TempVault();
        await using var context = new PineDbContext(vault.DatabasePath, vault.Key);
        await context.Database.MigrateAsync(CancellationToken.None);

        var usd = new CurrencyCode("USD");
        var account = Account.Create("Main", AccountType.Bank, new Money(0m, usd), TimeProvider.System);
        context.Accounts.Add(account);

        var categorisedIds = new System.Collections.Generic.List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            var t = Transaction.Create(account.Id, DateTimeOffset.UtcNow.AddDays(-1), $"cat{i}", new Money(-1m, usd), TransactionType.Expense, TimeProvider.System);
            t.ReplaceSplits(new[] { new SplitDefinition(new Money(-1m, usd), "Groceries") });
            context.Transactions.Add(t);
            categorisedIds.Add(t.Id);
        }

        context.Transactions.Add(Transaction.Create(account.Id, DateTimeOffset.UtcNow.AddDays(-1), "none", new Money(-1m, usd), TransactionType.Expense, TimeProvider.System));
        var blank = Transaction.Create(account.Id, DateTimeOffset.UtcNow.AddDays(-1), "blank", new Money(-1m, usd), TransactionType.Expense, TimeProvider.System);
        blank.ReplaceSplits(new[] { new SplitDefinition(new Money(-1m, usd), "   ") });
        context.Transactions.Add(blank);
        await context.SaveChangesAsync(CancellationToken.None);

        var repository = new SqliteTransactionRepository(context);

        (await repository.CountPossiblyCategorisedAsync(CancellationToken.None)).Should().Be(5);

        var first = await repository.GetCategorisedPageAsync(0, 2, CancellationToken.None);
        var second = await repository.GetCategorisedPageAsync(2, 2, CancellationToken.None);
        var third = await repository.GetCategorisedPageAsync(4, 2, CancellationToken.None);
        var beyond = await repository.GetCategorisedPageAsync(6, 2, CancellationToken.None);

        first.Should().HaveCount(2);
        second.Should().HaveCount(2);
        third.Should().HaveCount(1);
        beyond.Should().BeEmpty();
        first.Concat(second).Concat(third).Select(t => t.Id).Should().BeEquivalentTo(categorisedIds);
        first.SelectMany(t => t.Splits).Should().OnlyContain(s => s.Category == "Groceries", "owned splits come back with their owner");
    }
}
