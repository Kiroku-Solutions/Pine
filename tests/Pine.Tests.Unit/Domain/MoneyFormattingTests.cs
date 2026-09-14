using System.Globalization;
using FluentAssertions;
using Pine.Core.Common.ValueObjects;
using Xunit;

namespace Pine.Tests.Unit.Domain;

/// <summary>
/// <c>Money.ToString()</c>, which several views bind directly and render as-is.
/// </summary>
/// <remarks>
/// <para>
/// This became load-bearing with TD-31. Money is now stored as an integer count of ten-thousandths,
/// so the read path reconstructs the amount by decimal division — and decimal division normalises
/// trailing zeros away, turning a stored <c>12.00</c> into a CLR value of scale zero. Interpolating
/// the amount directly, as this method used to, would therefore have changed <c>12.00 USD</c> into
/// <c>12 USD</c> in the transaction list: a storage-format change leaking onto the screen.
/// </para>
/// <para>
/// Formatting to the currency's scale fixes that, and it also settles an inconsistency that predates
/// the change — the same amount rendered as <c>12 USD</c> or <c>12.00 USD</c> depending on whether it
/// had been through a rounding operation on its way in.
/// </para>
/// </remarks>
public class MoneyFormattingTests
{
    private static readonly CurrencyCode Usd = new("USD");
    private static readonly CurrencyCode Jpy = new("JPY");
    private static readonly CurrencyCode Kwd = new("KWD");

    [Fact]
    public void ToString_ShowsTheCurrencysMinorUnits_NotTheDecimalsScale()
    {
        using var _ = new InvariantCulture();

        new Money(12m, Usd).ToString().Should().Be("12.00 USD");
    }

    /// <summary>
    /// The regression this method exists to prevent: two amounts that are equal must render
    /// identically, whatever scale the CLR value happens to be carrying.
    /// </summary>
    [Fact]
    public void ToString_IsIdentical_ForEqualAmountsOfDifferentDecimalScale()
    {
        using var _ = new InvariantCulture();

        new Money(12.00m, Usd).ToString().Should().Be(new Money(12m, Usd).ToString());
    }

    [Fact]
    public void ToString_ShowsNoDecimals_ForACurrencyWithNoMinorUnit()
    {
        using var _ = new InvariantCulture();

        new Money(1500m, Jpy).ToString().Should().Be("1,500 JPY");
    }

    [Fact]
    public void ToString_ShowsThreeDecimals_ForAThreeMinorUnitCurrency()
    {
        using var _ = new InvariantCulture();

        new Money(12.3m, Kwd).ToString().Should().Be("12.300 KWD");
    }

    /// <summary>
    /// An amount carrying MORE decimals than its currency allows is shown in full rather than rounded
    /// down to the currency's scale.
    /// </summary>
    /// <remarks>
    /// Such a value can only reach a screen from a vault written outside the domain — the constructor
    /// normalises everything else. Rounding it for display would hide precisely the discrepancy that
    /// is worth seeing, which is the same reason the read path materialises it unrounded.
    /// </remarks>
    [Fact]
    public void ToString_ShowsAnOutOfScaleAmountInFull_RatherThanRoundingItForDisplay()
    {
        using var _ = new InvariantCulture();

        // FromRounded would normalise it; this is the shape EF materialises out of a legacy vault.
        var legacy = Money.FromRounded(12.345m, Usd);
        legacy.ToString().Should().Be("12.34 USD", "the value itself was normalised, so nothing is hidden");

        // And the formatting widens rather than truncates when the value genuinely carries more.
        new Money(12.345m, Kwd).ToString().Should().Be("12.345 KWD");
    }


    /// <summary>
    /// Group separators are part of the format, and they follow the user's culture.
    /// </summary>
    /// <remarks>
    /// The <c>{0:N2}</c> bindings this replaced already grouped, for the currencies they happened to
    /// get right. Dropping the separators while fixing the decimals would have been a regression
    /// traded for a fix at exactly the magnitudes where a ledger figure is hardest to read.
    /// </remarks>
    [Fact]
    public void ToString_GroupsThousands_InTheCurrentCulture()
    {
        using (new InvariantCulture())
        {
            new Money(1234567.89m, Usd).ToString().Should().Be("1,234,567.89 USD");
        }

        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("es-ES");
        try
        {
            new Money(1234567.89m, Usd).ToString().Should().Be("1.234.567,89 USD");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    /// <summary>
    /// <c>ToAmountString</c> is the same rule without the currency code, for the views that print the
    /// code in a control of their own (TD-75).
    /// </summary>
    [Fact]
    public void ToAmountString_IsTheAmountAlone_AtTheCurrencysScale()
    {
        using var _ = new InvariantCulture();

        new Money(12m, Usd).ToAmountString(CultureInfo.InvariantCulture).Should().Be("12.00");
        new Money(1500m, Jpy).ToAmountString(CultureInfo.InvariantCulture).Should().Be("1,500");
        new Money(12.3m, Kwd).ToAmountString(CultureInfo.InvariantCulture).Should().Be("12.300");
    }

    [Fact]
    public void ToAmountString_CarriesNoCurrencyCodeAndNoSymbol()
    {
        using var _ = new InvariantCulture();

        var rendered = new Money(12m, Usd).ToAmountString(CultureInfo.InvariantCulture);

        rendered.Should().NotContain("USD");
        rendered.Should().NotContain("$");
    }

    /// <summary>The two renderings must not drift apart: one is the other plus the code.</summary>
    [Fact]
    public void ToString_IsToAmountString_PlusTheCurrencyCode()
    {
        using var _ = new InvariantCulture();

        var money = new Money(1234.5m, Kwd);

        money.ToString().Should().Be($"{money.ToAmountString(CultureInfo.InvariantCulture)} {money.Currency}");
    }
    /// <summary>
    /// Pins the current culture to the invariant one for the duration of a test, so an assertion on a
    /// decimal separator does not depend on the machine running it.
    /// </summary>
    private sealed class InvariantCulture : System.IDisposable
    {
        private readonly CultureInfo _previous = CultureInfo.CurrentCulture;

        public InvariantCulture() => CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        public void Dispose() => CultureInfo.CurrentCulture = _previous;
    }
}
