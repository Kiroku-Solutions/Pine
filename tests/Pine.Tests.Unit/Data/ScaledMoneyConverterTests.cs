using System;
using FluentAssertions;
using Pine.Core.Common.ValueObjects;
using Pine.Infrastructure.Data.Converters;
using Xunit;

namespace Pine.Tests.Unit.Data;

/// <summary>
/// TD-31: the converter that stores a monetary <c>decimal</c> as an INTEGER count of
/// ten-thousandths.
/// </summary>
/// <remarks>
/// The integration suite proves the migration converts an existing vault exactly. This file pins the
/// two properties that make the format safe in the first place: the round trip is lossless for every
/// scale ISO 4217 defines, and a value the format cannot represent is refused rather than quietly
/// rounded into one it can.
/// </remarks>
public class ScaledMoneyConverterTests
{
    /// <summary>
    /// One amount per ISO scale, at the limit of what that scale can express. If the storage scale
    /// were ever reduced below four, the <c>CLF</c> row is the one that would go red first.
    /// </summary>
    public static TheoryData<decimal, long> EveryIsoScale() => new()
    {
        { 1500m, 15_000_000L },       // JPY, 0 minor units
        { 12.34m, 123_400L },         // USD, 2 minor units
        { 12.345m, 123_450L },        // KWD, 3 minor units
        { 12.3456m, 123_456L },       // CLF, 4 minor units — the ISO maximum
    };

    [Theory]
    [MemberData(nameof(EveryIsoScale))]
    public void ToStorage_ScalesByTenThousand_ForEveryScaleIso4217Defines(decimal amount, long expected)
    {
        ScaledMoneyConverter.ToStorage(amount).Should().Be(expected);
    }

    [Theory]
    [MemberData(nameof(EveryIsoScale))]
    public void FromStorage_ReturnsTheOriginalAmount(decimal amount, long stored)
    {
        ScaledMoneyConverter.FromStorage(stored).Should().Be(amount);
    }

    /// <summary>
    /// The sign must not travel with the fraction. A conversion that splits the number into its two
    /// halves and forgets to re-apply the sign gets positive amounts right and negative ones wrong,
    /// which in a ledger is every expense.
    /// </summary>
    [Theory]
    [InlineData("-12.34", -123_400L)]
    [InlineData("-0.0001", -1L)]
    [InlineData("-1500", -15_000_000L)]
    public void ToStorage_CarriesTheSign(string amount, long expected)
    {
        var value = decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture);

        ScaledMoneyConverter.ToStorage(value).Should().Be(expected);
        ScaledMoneyConverter.FromStorage(expected).Should().Be(value);
    }

    [Fact]
    public void Zero_RoundTrips_AsZero()
    {
        ScaledMoneyConverter.ToStorage(0m).Should().Be(0L);
        ScaledMoneyConverter.FromStorage(0L).Should().Be(0m);
    }

    /// <summary>
    /// Trailing zeros are a property of the CLR value, not of the amount. <c>12.3400</c> and
    /// <c>12.34</c> are the same quantity of money and must reach the same integer.
    /// </summary>
    [Fact]
    public void AnAmountPaddedWithTrailingZeros_ReachesTheSameInteger()
    {
        ScaledMoneyConverter.ToStorage(12.3400m).Should().Be(ScaledMoneyConverter.ToStorage(12.34m));
    }

    /// <summary>
    /// The refusal that keeps the store honest. <c>Money</c>'s constructor already rounds to the
    /// currency's scale, so an amount arriving here with a fifth decimal means that normalisation was
    /// bypassed — and silently rounding it would erase the only evidence that something is writing
    /// to the vault without going through the domain.
    /// </summary>
    [Fact]
    public void ToStorage_RefusesAnAmountBeyondFourDecimals_RatherThanRoundingIt()
    {
        var writing = () => ScaledMoneyConverter.ToStorage(10.00005m);

        writing.Should().Throw<InvalidOperationException>()
            .WithMessage("*more than 4 decimal places*");
    }

    /// <summary>
    /// The refusal above must not fire on anything the domain can legitimately produce, or it becomes
    /// a crash on save. Every <c>Money</c> is normalised to its currency's scale, and the largest of
    /// those is four.
    /// </summary>
    [Theory]
    [InlineData("JPY")]
    [InlineData("USD")]
    [InlineData("KWD")]
    [InlineData("CLF")]
    public void ToStorage_AcceptsEveryAmountMoneyCanConstruct(string code)
    {
        var currency = new CurrencyCode(code);
        var money = Money.FromRounded(1234.56789m, currency);

        var writing = () => ScaledMoneyConverter.ToStorage(money.Amount);

        writing.Should().NotThrow();
        ScaledMoneyConverter.FromStorage(ScaledMoneyConverter.ToStorage(money.Amount))
            .Should().Be(money.Amount);
    }

    /// <summary>
    /// An amount too large for a 64-bit count of ten-thousandths overflows loudly. Unchecked, the
    /// cast would wrap and write a number of the opposite sign into the ledger.
    /// </summary>
    [Fact]
    public void ToStorage_OverflowsLoudly_RatherThanWrapping()
    {
        var writing = () => ScaledMoneyConverter.ToStorage(1_000_000_000_000_000m);

        writing.Should().Throw<OverflowException>();
    }
}
