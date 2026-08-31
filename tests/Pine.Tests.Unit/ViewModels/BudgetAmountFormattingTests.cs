using System;
using System.Globalization;
using FluentAssertions;
using Pine.Ui.Avalonia.ViewModels;
using Xunit;

namespace Pine.Tests.Unit.ViewModels;

/// <summary>
/// TD-75 on the budgets screen: the amounts are written at their currency's scale rather than at a
/// hardcoded two decimals.
/// </summary>
/// <remarks>
/// The budgets row carries a bare <c>decimal</c> and a currency code rather than a <c>Money</c>, so
/// it cannot bind through the shared converter the widgets use — it rejoins the pair itself. These
/// tests exist because that is a second implementation of the same rule, and two implementations
/// drift unless something holds them together.
/// </remarks>
public class BudgetAmountFormattingTests
{
    [Fact]
    public void AnAmount_IsWrittenAtItsCurrencysScale()
    {
        using var _ = new InvariantCulture();
        var row = new BudgetProgressViewModel { LimitAmount = 1500m, CurrencyCode = "JPY" };

        row.LimitAmountText.Should().Be("1,500", "yen has no minor unit");
    }

    [Fact]
    public void AThreeMinorUnitCurrency_KeepsItsThirdDecimal()
    {
        using var _ = new InvariantCulture();
        var row = new BudgetProgressViewModel { LimitAmount = 12.345m, CurrencyCode = "KWD" };

        row.LimitAmountText.Should().Be("12.345", "a hardcoded N2 dropped the third digit");
    }

    [Fact]
    public void ATwoMinorUnitCurrency_IsUnchangedFromBefore()
    {
        using var _ = new InvariantCulture();
        var row = new BudgetProgressViewModel { SpentAmount = 1234.5m, CurrencyCode = "USD" };

        row.SpentAmountText.Should().Be("1,234.50");
    }

    /// <summary>
    /// A budget can be created against a currency that is not in the ISO table (TD-63 permits it, and
    /// TD-78 records that gap). Rendering must not take the page down over one.
    /// </summary>
    [Fact]
    public void AnUnknownCurrency_StillRenders_RatherThanThrowing()
    {
        using var _ = new InvariantCulture();
        var row = new BudgetProgressViewModel { LimitAmount = 100m, CurrencyCode = "ZZZ" };

        row.Invoking(r => r.LimitAmountText).Should().NotThrow();
        row.LimitAmountText.Should().Be("100.00", "an unknown code falls back to the default scale");
    }

    [Fact]
    public void AnAbsentCurrency_StillRenders()
    {
        using var _ = new InvariantCulture();
        var row = new BudgetProgressViewModel { LimitAmount = 100m, CurrencyCode = string.Empty };

        row.Invoking(r => r.LimitAmountText).Should().NotThrow();
    }

    /// <summary>
    /// The rendered text has to move when its inputs do, or the row shows a stale figure.
    /// </summary>
    [Fact]
    public void ChangingTheAmount_RaisesAChangeForTheRenderedText()
    {
        var row = new BudgetProgressViewModel { CurrencyCode = "USD" };
        var raised = new System.Collections.Generic.List<string?>();
        row.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        row.SpentAmount = 42m;

        raised.Should().Contain(nameof(BudgetProgressViewModel.SpentAmountText));
    }

    private sealed class InvariantCulture : IDisposable
    {
        private readonly CultureInfo _previous = CultureInfo.CurrentCulture;

        public InvariantCulture() => CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        public void Dispose() => CultureInfo.CurrentCulture = _previous;
    }
}
