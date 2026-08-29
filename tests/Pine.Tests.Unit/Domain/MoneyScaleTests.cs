using System;
using FluentAssertions;
using Pine.Core.Common.ValueObjects;
using Xunit;

namespace Pine.Tests.Unit.Domain;

/// <summary>
/// TD-18, first half: <see cref="Money"/>'s scale policy — what it normalises, and what it refuses.
/// </summary>
/// <remarks>
/// <para>
/// THE INVARIANT BEING BOUGHT. Before this policy existed, "the sum of the splits equals the total"
/// (<c>Transaction.ValidateSplits</c>) was exact only because nothing in Pine ever divided. It was
/// an invariant over arbitrary-precision decimals, not over money. These tests pin the frontier that
/// makes it an invariant over money instead.
/// </para>
/// <para>
/// THE FRONTIER IS ASYMMETRIC ON PURPOSE, and the asymmetry is the design, not an oversight:
/// digits below a currency's minor unit are sub-unit noise and are rounded away; a fractional part
/// in a currency that HAS no minor unit is not noise, because there is no sub-unit for noise to
/// occupy — it is an amount the caller should never have constructed, and it throws.
/// </para>
/// </remarks>
public class MoneyScaleTests
{
    private static CurrencyCode Usd => new("USD");
    private static CurrencyCode Jpy => new("JPY");
    private static CurrencyCode Kwd => new("KWD");

    // ---------------------------------------------------------------- normalisation

    /// <summary>The example from the ticket, plus the half-to-even behaviour it depends on.</summary>
    [Theory]
    [InlineData("10.005", "10.00")]  // midpoint, down to the even cent
    [InlineData("10.015", "10.02")]  // midpoint, up to the even cent
    [InlineData("10.0049", "10.00")]
    [InlineData("10.006", "10.01")]
    [InlineData("0.004", "0.00")]
    [InlineData("-10.005", "-10.00")]
    [InlineData("-10.015", "-10.02")]
    public void Money_RoundsSubUnitNoise_HalfToEven(string raw, string expected)
    {
        var amount = decimal.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);
        var expectedAmount = decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture);

        new Money(amount, Usd).Amount.Should().Be(expectedAmount);
    }

    /// <summary>
    /// The discriminator against <see cref="MidpointRounding.AwayFromZero"/>. Away-from-zero would
    /// send both of these up, which is a bias that accumulates across a ledger in one direction.
    /// </summary>
    [Fact]
    public void Money_UsesBankersRounding_NotAwayFromZero()
    {
        new Money(2.345m, Usd).Amount.Should().Be(2.34m, "2.34 is even; away-from-zero would give 2.35");
        new Money(2.355m, Usd).Amount.Should().Be(2.36m, "2.36 is even; truncation would give 2.35");
    }

    [Fact]
    public void Money_RespectsAThreeDigitCurrency()
    {
        new Money(1.23456m, Kwd).Amount.Should().Be(1.235m);
        new Money(1.2345m, Kwd).Amount.Should().Be(1.234m, "half-to-even, and 4 is even");
    }

    /// <summary>
    /// Normalisation is idempotent: feeding an already-normalised amount back through the
    /// constructor changes nothing. Without this, arithmetic could drift a cent per operation.
    /// </summary>
    [Theory]
    [InlineData("10.005")]
    [InlineData("-0.004")]
    [InlineData("123456.789")]
    [InlineData("0.005")]
    [InlineData("-99999.995")]
    public void Money_Normalisation_IsIdempotent(string raw)
    {
        var amount = decimal.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);

        foreach (var currency in new[] { Usd, Kwd })
        {
            var once = new Money(amount, currency);
            var twice = new Money(once.Amount, currency);

            twice.Amount.Should().Be(once.Amount);
            twice.Should().Be(once);
        }
    }

    // ---------------------------------------------------------------- rejection

    /// <summary>
    /// The other side of the frontier. A fractional yen is not sub-unit noise; there is no yen
    /// sub-unit. Rounding it silently would hide whatever arithmetic produced the fraction.
    /// </summary>
    [Theory]
    [InlineData("10.5")]
    [InlineData("10.01")]
    [InlineData("-0.5")]
    [InlineData("0.4")]
    public void Money_Rejects_AFractionalAmountInAZeroMinorUnitCurrency(string raw)
    {
        var amount = decimal.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);

        Action act = () => _ = new Money(amount, Jpy);

        act.Should().Throw<ArgumentException>()
           .WithMessage("*JPY, which has no minor unit*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(-10)]
    [InlineData(1000000)]
    public void Money_Accepts_AWholeAmountInAZeroMinorUnitCurrency(decimal amount)
    {
        new Money(amount, Jpy).Amount.Should().Be(amount);
    }

    /// <summary>
    /// <c>10.0m</c> is a trailing zero, not a fraction. <see cref="decimal"/> keeps the scale in its
    /// representation, so an implementation that inspected the SCALE rather than the VALUE would
    /// wrongly reject this.
    /// </summary>
    [Fact]
    public void Money_Accepts_AWholeYenAmountWrittenWithTrailingZeroes()
    {
        Action act = () => _ = new Money(10.000m, Jpy);

        act.Should().NotThrow();
        new Money(10.000m, Jpy).Amount.Should().Be(10m);
    }

    [Fact]
    public void Money_Rejects_ANullCurrency()
    {
        Action act = () => _ = new Money(1m, null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // ---------------------------------------------------------------- explicit rounding

    /// <summary>
    /// The escape hatch a boundary needs. It never throws, which is precisely what distinguishes it
    /// from the constructor.
    /// </summary>
    [Fact]
    public void Round_IsTheExplicitWayToTurnAnArbitraryDecimalIntoAnExpressibleAmount()
    {
        Money.Round(10.005m, Usd).Should().Be(10.00m);
        Money.Round(10.5m, Jpy).Should().Be(10m, "half-to-even sends 10.5 to the even 10");
        Money.Round(11.5m, Jpy).Should().Be(12m);
        Money.Round(1.2345m, Kwd).Should().Be(1.234m);
    }

    [Fact]
    public void FromRounded_ConstructsWhereTheConstructorWouldHaveThrown()
    {
        Money.FromRounded(10.5m, Jpy).Amount.Should().Be(10m);
        Money.FromRounded(10.005m, Usd).Amount.Should().Be(10.00m);
    }

    // ---------------------------------------------------------------- derived API

    [Fact]
    public void Scale_ExposesTheCurrencysMinorUnits()
    {
        new Money(1m, Usd).Scale.Should().Be(2);
        new Money(1m, Jpy).Scale.Should().Be(0);
        new Money(1m, Kwd).Scale.Should().Be(3);
    }

    [Fact]
    public void Multiply_NormalisesItsResult_AndDoesNotThrowForAZeroMinorUnitCurrency()
    {
        new Money(10.00m, Usd).Multiply(0.075m).Amount.Should().Be(0.75m);
        new Money(3.33m, Usd).Multiply(3m).Amount.Should().Be(9.99m);

        // 101 JPY times a half is 50.5 — arithmetic this type performed, not an amount a caller
        // authored, so it rounds rather than throwing.
        new Money(101m, Jpy).Multiply(0.5m).Amount.Should().Be(50m);
    }

    [Fact]
    public void Multiply_HasAnOperatorForm()
    {
        (new Money(2.50m, Usd) * 4m).Should().Be(new Money(10.00m, Usd));
    }

    /// <summary>
    /// Four call sites were writing <c>new Money(-x.Amount, x.Currency)</c> by hand.
    /// </summary>
    [Fact]
    public void Negate_FlipsTheSignAndKeepsTheCurrency()
    {
        var owed = new Money(42.50m, Usd);

        owed.Negate().Should().Be(new Money(-42.50m, Usd));
        (-owed).Should().Be(owed.Negate());
        (-(-owed)).Should().Be(owed);
        (-Money.Zero(Usd)).Amount.Should().Be(0m);
    }

    [Fact]
    public void ToMinorUnits_ProjectsToWholeUnits()
    {
        new Money(10.00m, Usd).ToMinorUnits().Should().Be(1000);
        new Money(-0.01m, Usd).ToMinorUnits().Should().Be(-1);
        new Money(1234m, Jpy).ToMinorUnits().Should().Be(1234);
        new Money(1.234m, Kwd).ToMinorUnits().Should().Be(1234);
    }

    /// <summary>
    /// Arithmetic keeps amounts inside the scale, so a chain of operations cannot accumulate a
    /// sub-unit tail that later rounds all at once.
    /// </summary>
    [Fact]
    public void AddAndSubtract_StayNormalised()
    {
        var a = new Money(0.005m, Usd);   // -> 0.00
        var b = new Money(0.014m, Usd);   // -> 0.01

        (a + b).Amount.Should().Be(0.01m);
        (b - a).Amount.Should().Be(0.01m);
    }
}
