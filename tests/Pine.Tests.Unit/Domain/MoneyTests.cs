using FluentAssertions;
using Pine.Core.Common.ValueObjects;
using System;
using Xunit;

namespace Pine.Tests.Unit.Domain;

public class MoneyTests
{
    [Fact]
    public void Money_ShouldThrowException_WhenAddingDifferentCurrencies()
    {
        // Arrange
        var usd = new Money(100m, new CurrencyCode("USD"));
        var eur = new Money(100m, new CurrencyCode("EUR"));

        // Act
        Action act = () => { var result = usd + eur; };

        // Assert
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*currenc*");
    }

    [Fact]
    public void Money_ShouldAddCorrectly_WhenCurrenciesAreSame()
    {
        // Arrange
        var usd1 = new Money(100.50m, new CurrencyCode("USD"));
        var usd2 = new Money(50.25m, new CurrencyCode("USD"));

        // Act
        var result = usd1 + usd2;

        // Assert
        result.Amount.Should().Be(150.75m);
        result.Currency.Value.Should().Be("USD");
    }

    [Fact]
    public void Money_ShouldSubtractCorrectly_WhenCurrenciesAreSame()
    {
        // Arrange
        var usd1 = new Money(100m, new CurrencyCode("USD"));
        var usd2 = new Money(20m, new CurrencyCode("USD"));

        // Act
        var result = usd1 - usd2;

        // Assert
        result.Amount.Should().Be(80m);
    }
}
