using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MediatR;
using NSubstitute;
using Pine.Application.Accounts.Queries;
using Pine.Core.Accounts;
using Pine.Core.Common.ValueObjects;
using Pine.Ui.Avalonia.ViewModels;
using Xunit;

namespace Pine.Tests.Unit.ViewModels;

/// <summary>
/// The dashboard's headline balance figure.
/// </summary>
/// <remarks>
/// <para>
/// WHAT THIS REPLACED WAS A FABRICATED NUMBER, which is why these tests exist rather than a single
/// happy-path one. The previous implementation added every account's <c>Balance.Amount</c> into one
/// bare <c>decimal</c> and labelled the total with whichever currency the LAST account in the loop
/// carried. A vault holding 1 000 USD and 900 EUR displayed "1900 EUR" — not a wrong total, but a
/// quantity of nothing. <c>Money.Add</c> forbids adding across currencies; the old code got past it
/// by never using <c>Money</c> at all.
/// </para>
/// <para>
/// It also formatted with <c>{0:C2}</c>, which emits the CURRENT MACHINE's currency symbol. Every
/// test here pins the culture, because the defect it guards against is precisely a figure that
/// changes meaning with the machine it is rendered on.
/// </para>
/// </remarks>
public class DashboardBalanceSummaryTests
{
    private static readonly CurrencyCode Usd = new("USD");
    private static readonly CurrencyCode Eur = new("EUR");
    private static readonly CurrencyCode Jpy = new("JPY");

    [Fact]
    public async Task ASingleCurrencyVault_ShowsOneTotal()
    {
        using var _ = new InvariantCulture();
        var viewModel = BuildViewModel(
            Account("Current", 1000m, Usd),
            Account("Savings", 234.56m, Usd));

        await viewModel.LoadDataCommand.ExecuteAsync(null);

        viewModel.TotalBalance.Should().Be("1,234.56 USD");
    }

    /// <summary>
    /// The regression that matters: two currencies must produce two figures, not one invented sum.
    /// </summary>
    [Fact]
    public async Task AMultiCurrencyVault_ShowsATotalPerCurrency_NotOneAddedTogether()
    {
        using var _ = new InvariantCulture();
        var viewModel = BuildViewModel(
            Account("Dollars", 1000m, Usd),
            Account("Euros", 900m, Eur));

        await viewModel.LoadDataCommand.ExecuteAsync(null);

        viewModel.TotalBalance.Should().Be("900.00 EUR  ·  1,000.00 USD");

        // The old behaviour, stated so the test cannot quietly start passing for the wrong reason.
        viewModel.TotalBalance.Should().NotContain("1900",
            "adding dollars to euros produces a figure that is not a quantity of anything");
    }

    /// <summary>
    /// Each currency is written at its own scale, so a yen total does not grow two decimals it has
    /// no way to spend.
    /// </summary>
    [Fact]
    public async Task EachCurrencysTotal_IsWrittenAtItsOwnScale()
    {
        using var _ = new InvariantCulture();
        var viewModel = BuildViewModel(
            Account("Yen", 1500m, Jpy),
            Account("Dollars", 10m, Usd));

        await viewModel.LoadDataCommand.ExecuteAsync(null);

        viewModel.TotalBalance.Should().Be("1,500 JPY  ·  10.00 USD");
    }

    /// <summary>
    /// No borrowed currency symbol. Under a culture whose currency is the euro, a dollar balance must
    /// not acquire a euro sign.
    /// </summary>
    [Fact]
    public async Task TheFigure_CarriesNoCurrencySymbolFromTheMachinesCulture()
    {
        using var _ = new SpanishCulture();
        var viewModel = BuildViewModel(Account("Dollars", 1234.56m, Usd));

        await viewModel.LoadDataCommand.ExecuteAsync(null);

        viewModel.TotalBalance.Should().NotContain("€");
        viewModel.TotalBalance.Should().EndWith("USD");

        // The separators still follow the user's culture, which is the half that should be local.
        viewModel.TotalBalance.Should().Be("1.234,56 USD");
    }

    [Fact]
    public async Task AnEmptyVault_ShowsZero_RatherThanAnEmptyString()
    {
        using var _ = new InvariantCulture();
        var viewModel = BuildViewModel();

        await viewModel.LoadDataCommand.ExecuteAsync(null);

        viewModel.TotalBalance.Should().Be("0.00 USD");
    }

    private static Account Account(string name, decimal balance, CurrencyCode currency) =>
        Pine.Core.Accounts.Account.Create(name, AccountType.Bank, new Money(balance, currency), TimeProvider.System);

    private static DashboardViewModel BuildViewModel(params Account[] accounts)
    {
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<GetAccountsQuery>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<Account>)accounts);

        return new DashboardViewModel(mediator);
    }

    private sealed class InvariantCulture : IDisposable
    {
        private readonly CultureInfo _previous = CultureInfo.CurrentCulture;

        public InvariantCulture() => CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        public void Dispose() => CultureInfo.CurrentCulture = _previous;
    }

    private sealed class SpanishCulture : IDisposable
    {
        private readonly CultureInfo _previous = CultureInfo.CurrentCulture;

        public SpanishCulture() => CultureInfo.CurrentCulture = new CultureInfo("es-ES");

        public void Dispose() => CultureInfo.CurrentCulture = _previous;
    }
}
