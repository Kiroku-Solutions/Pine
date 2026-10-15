using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Pine.Application.Common;
using Pine.Application.Transactions.Commands;
using Xunit;

namespace Pine.Tests.Integration.Atomicity;

/// <summary>
/// TD-69 against a real encrypted database: void then unvoid returns the balance to where it was,
/// both halves commit together, and a refused unvoid never opens a transaction.
/// </summary>
public class UnvoidTransactionAtomicityTests
{
    private const decimal OpeningBalance = 1000m;
    private const decimal Expense = -75.50m;
    private const decimal TransferAmount = 250m;
    private static readonly DateTimeOffset TransactionDate = new(2026, 3, 4, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task VoidThenUnvoid_RestoresTheRowAndTheBalance_Together()
    {
        await using var host = await AtomicityHost.CreateAsync();
        var account = await host.SeedAccountAsync("Current", OpeningBalance);
        var id = await host.Mediator.Send(new AddTransactionCommand(
            account, TransactionDate, "Grocer", Expense, "USD", "weekly shop", "Groceries"));

        await host.Mediator.Send(new VoidTransactionCommand(id, "mistake"));
        (await host.ReadBalanceAsync(account)).Should().Be(OpeningBalance);
        host.ReadVoidStateRaw(id).VoidedAt.Should().NotBeNull();

        host.UnitOfWorkLog.Clear();
        var result = await host.Mediator.Send(new UnvoidTransactionCommand(id));

        result.UnvoidedTransactionIds.Should().Equal(id);
        host.ReadVoidStateRaw(id).VoidedAt.Should().BeNull();
        host.ReadVoidStateRaw(id).VoidReason.Should().BeNull();
        (await host.ReadBalanceAsync(account)).Should().Be(OpeningBalance + Expense,
            "the expense counts again, so the balance is back to its pre-void figure");
        host.CountRowsRaw("Transactions").Should().Be(1);
        host.UnitOfWorkLog.Calls.Should().Equal(
            UnitOfWorkCall.Begin, UnitOfWorkCall.SaveChanges, UnitOfWorkCall.Commit);
    }

    [Fact]
    public async Task VoidThenUnvoid_OfATransfer_RestoresBothLegsAndBothBalances()
    {
        await using var host = await AtomicityHost.CreateAsync();
        var source = await host.SeedAccountAsync("Source", OpeningBalance);
        var destination = await host.SeedAccountAsync("Destination", 500m);

        await host.Mediator.Send(new TransferFundsCommand(
            source, destination, TransferAmount, "USD", TransactionDate, "t"));

        var outflowId = await host.QueryAsync(c => c.Transactions
            .AsNoTracking()
            .Where(t => t.AccountId == source)
            .Select(t => t.Id)
            .SingleAsync());

        await host.Mediator.Send(new VoidTransactionCommand(outflowId, "mistake"));
        (await host.ReadBalanceAsync(source)).Should().Be(OpeningBalance);
        (await host.ReadBalanceAsync(destination)).Should().Be(500m);

        var result = await host.Mediator.Send(new UnvoidTransactionCommand(outflowId));

        result.UnvoidedTransactionIds.Should().HaveCount(2);
        (await host.ReadBalanceAsync(source)).Should().Be(OpeningBalance - TransferAmount);
        (await host.ReadBalanceAsync(destination)).Should().Be(500m + TransferAmount);
    }

    [Fact]
    public async Task Unvoid_OfATransactionThatIsNotVoided_IsRefusedBeforeATransactionIsOpened()
    {
        await using var host = await AtomicityHost.CreateAsync();
        var account = await host.SeedAccountAsync("Current", OpeningBalance);
        var id = await host.Mediator.Send(new AddTransactionCommand(
            account, TransactionDate, "Grocer", Expense, "USD", "weekly shop", "Groceries"));

        host.UnitOfWorkLog.Clear();
        var act = async () => await host.Mediator.Send(new UnvoidTransactionCommand(id));

        var thrown = await act.Should().ThrowAsync<RequestValidationException>();
        thrown.Which.Message.Should().Contain("has not been voided");
        (await host.ReadBalanceAsync(account)).Should().Be(OpeningBalance + Expense,
            "a refused unvoid must not apply the amount a second time");
        host.UnitOfWorkLog.Calls.Should().BeEmpty();
    }
}
