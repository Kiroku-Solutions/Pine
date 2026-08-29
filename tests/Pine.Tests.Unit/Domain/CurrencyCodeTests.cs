using System;
using System.Linq;
using FluentAssertions;
using Pine.Core.Common.ValueObjects;
using Xunit;

namespace Pine.Tests.Unit.Domain;

/// <summary>
/// TD-18: <see cref="CurrencyCode"/> validates ISO 4217 MEMBERSHIP, not merely the
/// <c>^[A-Z]{3}$</c> shape, and offers a non-throwing way to ask (TD-61).
/// </summary>
/// <remarks>
/// <para>
/// WHY MEMBERSHIP AND NOT SHAPE. The shape rule accepts 17,576 strings, of which about 180 are
/// currencies. Everything else is a typo that reaches the vault, gets stored, and is then
/// undeleteable: there is no correct value to migrate <c>"ZZZ"</c> to. §12.9's INCOMPLETE list
/// recorded exactly this — a vault could be seeded with <c>"ZZZ"</c> through
/// <c>CreateAccountCommand</c>, which has no validator because there is nothing to validate an
/// account's currency AGAINST. Membership is the only rule that does not need a second party.
/// </para>
/// <para>
/// WHY THE MINOR-UNIT COUNT IS NOT TAKEN FROM THE OPERATING SYSTEM.
/// <c>NumberFormatInfo.CurrencyDecimalDigits</c> is ICU data and differs across OS releases and
/// platforms, so two machines opening the same vault would round differently. The table is embedded.
/// </para>
/// </remarks>
public class CurrencyCodeTests
{
    [Theory]
    [InlineData("USD")]
    [InlineData("EUR")]
    [InlineData("JPY")]
    [InlineData("KWD")]
    public void CurrencyCode_Accepts_CodesThatAreInTheIsoTable(string code)
    {
        new CurrencyCode(code).Value.Should().Be(code);
    }

    /// <summary>
    /// The regression this ticket exists to prevent: a well-shaped non-currency.
    /// </summary>
    [Theory]
    [InlineData("ZZZ")]
    [InlineData("ABC")]
    [InlineData("QQQ")]
    [InlineData("BTC")] // A real asset, and not an ISO 4217 code. It was in the account-creation picker.
    public void CurrencyCode_Rejects_AWellShapedStringThatIsNotACurrency(string code)
    {
        Action act = () => _ = new CurrencyCode(code);

        act.Should().Throw<ArgumentException>()
           .WithMessage($"*'{code}' is not an ISO 4217 currency code*");
    }

    [Theory]
    [InlineData("US")]
    [InlineData("USDD")]
    [InlineData("US1")]
    [InlineData("US$")]
    public void CurrencyCode_Rejects_AnythingThatIsNotThreeAsciiLetters(string code)
    {
        Action act = () => _ = new CurrencyCode(code);

        act.Should().Throw<ArgumentException>().WithMessage("*exactly 3 uppercase letters*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CurrencyCode_Rejects_TheEmptyCases(string? code)
    {
        Action act = () => _ = new CurrencyCode(code!);

        act.Should().Throw<ArgumentException>().WithMessage("*cannot be empty*");
    }

    /// <summary>
    /// Normalisation happens BEFORE validation, so <c>" usd "</c> is a legitimate way to say USD.
    /// §12.9 recorded this as previously unpinned; it stays pinned here.
    /// </summary>
    [Theory]
    [InlineData("usd")]
    [InlineData(" USD ")]
    [InlineData("  uSd\t")]
    public void CurrencyCode_NormalisesBeforeItValidates(string raw)
    {
        new CurrencyCode(raw).Value.Should().Be("USD");
    }

    [Fact]
    public void TryCreate_Succeeds_AndProducesTheSameValueAsTheConstructor()
    {
        CurrencyCode.TryCreate(" eur ", out var code).Should().BeTrue();
        code!.Value.Should().Be("EUR");
        code.Should().Be(new CurrencyCode("EUR"));
    }

    /// <summary>
    /// TD-61's actual closure condition: the non-throwing path must agree with the throwing one on
    /// EVERY input, including the ones that only membership rejects. If they disagreed, a validator
    /// would wave a value through into a handler that then threw — the exact leak validation exists
    /// to close.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("US")]
    [InlineData("USDD")]
    [InlineData("US1")]
    [InlineData("ZZZ")]
    [InlineData("BTC")]
    [InlineData("usd")]
    [InlineData(" JPY ")]
    [InlineData("KWD")]
    public void TryCreate_AgreesWithTheConstructor_OnEveryInput(string? raw)
    {
        var constructorSucceeded = true;
        CurrencyCode? fromConstructor = null;

        try
        {
            fromConstructor = new CurrencyCode(raw!);
        }
        catch (ArgumentException)
        {
            constructorSucceeded = false;
        }

        var trySucceeded = CurrencyCode.TryCreate(raw, out var fromTry);

        trySucceeded.Should().Be(constructorSucceeded, "TryCreate must be the constructor's rule, not a copy of it");

        if (constructorSucceeded)
        {
            fromTry.Should().Be(fromConstructor);
        }
        else
        {
            fromTry.Should().BeNull();
        }
    }

    /// <summary>
    /// The backward-compatibility seam. A vault written before membership validation existed may
    /// hold anything three letters long, and it must still open.
    /// </summary>
    [Fact]
    public void FromStoredValue_Accepts_ACodeTheConstructorWouldReject()
    {
        var legacy = CurrencyCode.FromStoredValue("ZZZ");

        legacy.Value.Should().Be("ZZZ");
        legacy.IsIsoMember.Should().BeFalse();
        legacy.MinorUnits.Should().Be(IsoCurrencies.DefaultMinorUnits,
            "an unknown code has to be given a scale, and two is the only guess that leaves an existing cents amount alone");
    }

    [Fact]
    public void FromStoredValue_StillRefuses_AnEmptyValue()
    {
        Action act = () => _ = CurrencyCode.FromStoredValue("  ");

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("USD", 2)]
    [InlineData("EUR", 2)]
    [InlineData("GBP", 2)]
    [InlineData("JPY", 0)]
    [InlineData("KRW", 0)]
    [InlineData("XOF", 0)]
    [InlineData("KWD", 3)]
    [InlineData("BHD", 3)]
    [InlineData("CLF", 4)]
    public void MinorUnits_MatchesTheStandard(string code, int expected)
    {
        new CurrencyCode(code).MinorUnits.Should().Be(expected);
    }

    [Fact]
    public void IsoCurrencies_TableIsPopulatedAndInternallyConsistent()
    {
        IsoCurrencies.Count.Should().BeGreaterThan(150, "the active ISO 4217 list is around 180 codes");

        IsoCurrencies.Codes.Should().OnlyContain(
            code => code.Length == 3 && code.All(WellShaped),
            "every member must itself satisfy the shape rule the constructor applies first");

        IsoCurrencies.Codes.Should().OnlyContain(
            code => IsoCurrencies.MinorUnitsOrNull(code) >= 0 && IsoCurrencies.MinorUnitsOrNull(code) <= 4,
            "Money only knows how to scale to four digits");

        // Every member must also be constructible, which is the rule this table backs.
        IsoCurrencies.Codes.Should().OnlyContain(code => IsConstructible(code));

        IsoCurrencies.IsMember("USD").Should().BeTrue();
        IsoCurrencies.IsMember("ZZZ").Should().BeFalse();
        IsoCurrencies.MinorUnitsOrNull("ZZZ").Should().BeNull();
    }

    private static bool WellShaped(char c) => c is >= 'A' and <= 'Z';

    private static bool IsConstructible(string code) => CurrencyCode.TryCreate(code, out _);
}
