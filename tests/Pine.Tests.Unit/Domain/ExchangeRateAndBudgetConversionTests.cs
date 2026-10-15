using System;
using FluentAssertions;
using Pine.Core.Budgets;
using Pine.Core.Common.ValueObjects;
using Pine.Core.ExchangeRates;
using Xunit;

namespace Pine.Tests.Unit.Domain;

/// <summary>The exchange-rate scale policy and the one-time budget conversion (owner decisions, 2026-10-14).</summary>
public class ExchangeRateAndBudgetConversionTests
{
    private static readonly CurrencyCode Usd = new("USD");
    private static readonly CurrencyCode Eur = new("EUR");
    private static readonly CurrencyCode Jpy = new("JPY");
    private static readonly DateTime Now = new(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);

    private static Budget BudgetOf(decimal amount, CurrencyCode currency) =>
        Budget.Create("Groceries", new Money(amount, currency), BudgetPeriod.Monthly);

    [Theory]
    [InlineData("0", ExchangeRateProblem.NotPositive)]
    [InlineData("-0.5", ExchangeRateProblem.NotPositive)]
    [InlineData("0.123456789", ExchangeRateProblem.TooPrecise)]
    [InlineData("1000000000.01", ExchangeRateProblem.TooLarge)]
    [InlineData("0.00000001", ExchangeRateProblem.None)]
    [InlineData("1000000000", ExchangeRateProblem.None)]
    [InlineData("0.92", ExchangeRateProblem.None)]
    public void TheScalePolicy_AcceptsOrNamesWhatIsWrong(string rate, ExchangeRateProblem expected) =>
        ExchangeRate.Check(decimal.Parse(rate, System.Globalization.CultureInfo.InvariantCulture)).Should().Be(expected);

    [Fact]
    public void ARate_IsBetweenTwoDifferentCurrencies_AndIsManual()
    {
        var act = () => ExchangeRate.Create(Usd, Usd, 1m, Now);
        act.Should().Throw<ArgumentException>();

        var rate = ExchangeRate.Create(Usd, Eur, 0.92m, new DateTime(2026, 10, 14, 17, 30, 0, DateTimeKind.Utc));
        rate.Source.Should().Be("manual");
        rate.EffectiveDateUtc.Should().Be(new DateTime(2026, 10, 14, 0, 0, 0, DateTimeKind.Utc), "only the UTC day counts");
    }

    [Fact]
    public void ConvertTo_ConvertsAtTheRate_AndKeepsWhatItWas()
    {
        var budget = BudgetOf(100.00m, Usd);

        budget.ConvertTo(Eur, 0.92m, Now);

        budget.Limit.Should().Be(new Money(92.00m, Eur));
        budget.PreviousLimitAmount.Should().Be(100.00m);
        budget.PreviousLimitCurrency.Should().Be("USD");
        budget.LimitConversionRate.Should().Be(0.92m);
        budget.LimitConvertedAtUtc.Should().Be(Now);
    }

    [Theory]
    [InlineData("0.05", "0.5", "0.02")] // 0.025 -> half to even
    [InlineData("0.15", "0.5", "0.08")] // 0.075 -> half to even
    [InlineData("10.00", "0.3333", "3.33")]
    [InlineData("10.00", "0.6667", "6.67")]
    public void ConvertTo_RoundsHalfToEven_ToTheTargetsScale(string amount, string rate, string expected)
    {
        var budget = BudgetOf(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), Usd);

        budget.ConvertTo(Eur, decimal.Parse(rate, System.Globalization.CultureInfo.InvariantCulture), Now);

        budget.Limit.Amount.Should().Be(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ConvertTo_ARoundedZeroScaleTarget_UsesTheTargetsScale()
    {
        var budget = BudgetOf(5m, Usd);

        budget.ConvertTo(Jpy, 0.5m, Now); // 2.5 JPY -> 2 (half to even), JPY has no minor unit

        budget.Limit.Should().Be(new Money(2m, Jpy));
    }

    [Fact]
    public void ConvertTo_RefusesAResultOfZero_AndLeavesTheBudgetAlone()
    {
        var budget = BudgetOf(1m, Usd);

        var act = () => budget.ConvertTo(Jpy, 0.3m, Now); // 0.3 JPY -> 0

        act.Should().Throw<InvalidOperationException>();
        budget.Limit.Should().Be(new Money(1m, Usd));
        budget.PreviousLimitCurrency.Should().BeNull();
    }

    [Fact]
    public void ConvertTo_RefusesTheSameCurrency_AndANonPositiveRate()
    {
        var budget = BudgetOf(10m, Usd);

        ((Action)(() => budget.ConvertTo(Usd, 1m, Now))).Should().Throw<InvalidOperationException>();
        ((Action)(() => budget.ConvertTo(Eur, 0m, Now))).Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => budget.ConvertTo(Eur, -1m, Now))).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void TheNaturalKey_IsThePairAndTheUtcDay()
    {
        var rate = ExchangeRate.Create(Usd, Eur, 0.92m, new DateTime(2026, 10, 14, 23, 59, 0, DateTimeKind.Utc));

        Pine.Core.Sync.NaturalKeys.For(rate).Should().Be("exchangerate|USD|EUR|2026-10-14");
        Pine.Core.Sync.NaturalKeys.KindOf(typeof(ExchangeRate)).Should().Be(Pine.Core.Sync.NaturalKeyKind.Derived);
    }
}
