using System;
using System.Threading.Tasks;
using FluentAssertions;
using Pine.Application.Accounts.Commands;
using Pine.Application.Common;
using Pine.Application.Transactions.Commands;
using Xunit;

namespace Pine.Tests.Integration.Atomicity;

/// <summary>
/// TD-70 against a real encrypted database: the opening balance and the recomputed balance commit
/// together, and a refused command never opens a transaction.
/// </summary>
public class SetOpeningBalanceAtomicityTests
{
    private static readonly DateTimeOffset Date = new(2026, 3, 4, 0, 0, 0, TimeSpan.Zero);

    private static async Task<(AtomicityHost Host, Guid Account)> LegacyAccountAsync()
    {
        var host = await AtomicityHost.CreateAsync();
        var account = await host.SeedAccountAsync("Legacy", 1000m);
        await host.Mediator.Send(new AddTransactionCommand(account, Date, "Grocer", -75.50m, "USD", "shop", "Groceries"));
        // Reproduce a row written before opening balances were tracked.
        host.ExecuteSqlRaw(@"UPDATE ""Accounts"" SET ""OpeningBalance_Amount"" = NULL;");
        return (host, account);
    }

    [Fact]
    public async Task SetOpeningBalance_RecordsTheFigureAndRecomputesTheBalance_InOneTransaction()
    {
        var (host, account) = await LegacyAccountAsync();
        await using var _ = host;
        host.ReadAccountAmountsRaw(account).OpeningBalanceAmount.Should().BeNull();
        host.UnitOfWorkLog.Clear();

        var result = await host.Mediator.Send(new SetOpeningBalanceCommand(account, 1000m));

        result.NewBalance.Should().Be(924.50m);
        result.Drift.Should().Be(0m);
        result.TransactionsCounted.Should().Be(1);
        host.ReadAccountAmountsRaw(account).OpeningBalanceAmount.Should().NotBeNull();
        (await host.ReadBalanceAsync(account)).Should().Be(924.50m);
        host.UnitOfWorkLog.Calls.Should().Equal(
            UnitOfWorkCall.Begin, UnitOfWorkCall.SaveChanges, UnitOfWorkCall.Commit);
    }

    [Fact]
    public async Task SetOpeningBalance_WithADifferentFigure_CorrectsTheStoredBalanceAndReportsTheDrift()
    {
        var (host, account) = await LegacyAccountAsync();
        await using var _ = host;

        var result = await host.Mediator.Send(new SetOpeningBalanceCommand(account, 1200m));

        result.PreviousBalance.Should().Be(924.50m);
        result.NewBalance.Should().Be(1124.50m);
        result.Drift.Should().Be(200m);
        (await host.ReadBalanceAsync(account)).Should().Be(1124.50m);
    }

    [Fact]
    public async Task SetOpeningBalance_OnAnAccountThatAlreadyHasOne_IsRefusedBeforeATransactionIsOpened()
    {
        await using var host = await AtomicityHost.CreateAsync();
        var account = await host.SeedAccountAsync("Current", 1000m);
        host.UnitOfWorkLog.Clear();

        var act = async () => await host.Mediator.Send(new SetOpeningBalanceCommand(account, 5m));

        await act.Should().ThrowAsync<RequestValidationException>();
        (await host.ReadBalanceAsync(account)).Should().Be(1000m);
        host.UnitOfWorkLog.Calls.Should().BeEmpty();
    }
}
