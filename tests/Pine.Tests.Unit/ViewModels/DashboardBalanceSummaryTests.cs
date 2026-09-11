using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MediatR;
using NSubstitute;
using Pine.Application.Accounts.Queries;
using Pine.Core.Accounts;
using Pine.Core.Common.ValueObjects;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.ViewModels.Widgets;
using Xunit;

namespace Pine.Tests.Unit.ViewModels;

/// <summary>
/// The dashboard's total balance figure, one per currency.
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
/// MOVED TO THE TILE IN POST-AUDIT PLAN ITEM 1.3. These tests pinned <c>DashboardViewModel.TotalBalance</c>,
/// a correct and well-tested figure that no view ever bound -- a number the suite proved and the user
/// never saw. It is shown by <see cref="TotalBalanceWidgetViewModel"/> now, and the tests follow it.
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

        await viewModel.InitializeAsync(CancellationToken.None);

        Rendered(viewModel).Should().Equal("1,234.56 USD");
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

        await viewModel.InitializeAsync(CancellationToken.None);

        Rendered(viewModel).Should().Equal("900.00 EUR", "1,000.00 USD");

        // The old behaviour, stated so the test cannot quietly start passing for the wrong reason.
        Rendered(viewModel).Should().NotContain(figure => figure.Contains("1900", StringComparison.Ordinal),
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

        await viewModel.InitializeAsync(CancellationToken.None);

        Rendered(viewModel).Should().Equal("1,500 JPY", "10.00 USD");
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

        await viewModel.InitializeAsync(CancellationToken.None);

        Rendered(viewModel).Single().Should().NotContain("€");
        Rendered(viewModel).Single().Should().EndWith("USD");

        // The separators still follow the user's culture, which is the half that should be local.
        Rendered(viewModel).Should().Equal("1.234,56 USD");
    }

    /// <summary>
    /// An empty vault has no total, and says so, rather than a zero in a currency nobody chose.
    /// </summary>
    /// <remarks>
    /// CHANGED DELIBERATELY IN ITEM 1.3. This test used to assert "0.00 USD" for a vault with no
    /// accounts -- US dollars invented for a user who may never hold one. The tile shows its empty
    /// state instead, and only once a read has succeeded.
    /// </remarks>
    [Fact]
    public async Task AnEmptyVault_ShowsTheEmptyState_NotAZeroInAnInventedCurrency()
    {
        using var _ = new InvariantCulture();
        var viewModel = BuildViewModel();

        viewModel.IsEmpty.Should().BeFalse("before the read lands, nothing is known -- not even emptiness");

        await viewModel.InitializeAsync(CancellationToken.None);

        viewModel.IsEmpty.Should().BeTrue();
        viewModel.Totals.Should().BeEmpty();
    }

    private static Account Account(string name, decimal balance, CurrencyCode currency) =>
        Pine.Core.Accounts.Account.Create(name, AccountType.Bank, new Money(balance, currency), TimeProvider.System);

    /// <summary>
    /// What the tile's view shows: one line per total, each written by <see cref="Money.ToString"/>
    /// through <c>Text="{Binding}"</c>.
    /// </summary>
    /// <remarks>
    /// Asserted on the collection the view binds, not on a joined string. The first retargeting of these
    /// tests pinned a <c>Summary</c> property that no view bound -- the very "tested and unseen" shape
    /// this item removed, reproduced in its own tests, as the independent review pointed out.
    /// </remarks>
    private static string[] Rendered(TotalBalanceWidgetViewModel viewModel) =>
        viewModel.Totals.Select(total => total.ToString()).ToArray();

    private static TotalBalanceWidgetViewModel BuildViewModel(params Account[] accounts)
    {
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<GetAccountsQuery>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<Account>)accounts);

        return new TotalBalanceWidgetViewModel(mediator, new Localizer());
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
