using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MediatR;
using NSubstitute;
using Pine.Application.Accounts.Queries;
using Pine.Application.Budgets.Queries;
using Pine.Core.Accounts;
using Pine.Core.Common.ValueObjects;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.ViewModels.Widgets;
using Xunit;

namespace Pine.Tests.Unit.ViewModels;

/// <summary>TD-63 follow-up: the total-balance tile splits out the base-currency total and never converts the rest.</summary>
public class DashboardBaseCurrencyTotalTests
{
    private static readonly CurrencyCode Usd = new("USD");
    private static readonly CurrencyCode Eur = new("EUR");

    private static async Task<TotalBalanceWidgetViewModel> Load(string? baseCurrency, params Account[] accounts)
    {
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<GetAccountsQuery>(), Arg.Any<CancellationToken>()).Returns((IReadOnlyList<Account>)accounts);
        mediator.Send(Arg.Any<GetBaseCurrencyQuery>(), Arg.Any<CancellationToken>()).Returns(baseCurrency);
        var tile = new TotalBalanceWidgetViewModel(mediator, new Localizer());
        await tile.InitializeAsync(CancellationToken.None);
        return tile;
    }

    private static Account Acct(decimal amount, CurrencyCode c) =>
        Account.Create("a", AccountType.Bank, new Money(amount, c), TimeProvider.System);

    [Fact]
    public async Task MixedCurrencies_ShowTheBaseTotalAlone_AndTheOthersApart()
    {
        var tile = await Load("EUR", Acct(1000m, Usd), Acct(900m, Eur), Acct(100m, Eur));

        tile.ShowsBaseSplit.Should().BeTrue();
        tile.ShowsFlatTotals.Should().BeFalse();
        tile.BaseCurrencyTotal.Should().Be(new Money(1000m, Eur), "only the EUR accounts; the 1000 USD is not added or converted");
        tile.OtherCurrencyTotals.Should().Equal(new Money(1000m, Usd));
    }

    [Fact]
    public async Task ASingleCurrencyVault_StaysAFlatList()
    {
        var tile = await Load("USD", Acct(5m, Usd), Acct(6m, Usd));

        tile.ShowsBaseSplit.Should().BeFalse();
        tile.ShowsFlatTotals.Should().BeTrue();
        tile.Totals.Should().Equal(new Money(11m, Usd));
    }

    [Fact]
    public async Task WhenNoAccountIsInTheBaseCurrency_EveryCurrencyIsItsOwnLine()
    {
        var tile = await Load("GBP", Acct(5m, Usd), Acct(6m, Eur));

        tile.ShowsBaseSplit.Should().BeFalse();
        tile.Totals.Should().HaveCount(2);
    }
}
