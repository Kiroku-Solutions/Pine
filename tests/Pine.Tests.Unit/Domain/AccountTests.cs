using FluentAssertions;
using Pine.Core.Accounts;
using Pine.Core.Common.ValueObjects;
using System;
using System.Linq;
using Xunit;

namespace Pine.Tests.Unit.Domain;

public class AccountTests
{
    private readonly TimeProvider _timeProvider = TimeProvider.System;

    [Fact]
    public void Create_ShouldReturnAccount_WhenValidArgumentsAreProvided()
    {
        // Arrange
        var initialBalance = new Money(100m, new CurrencyCode("USD"));
        var name = "My Bank Account";

        // Act
        var account = Account.Create(name, AccountType.Bank, initialBalance, _timeProvider);

        // Assert
        account.Should().NotBeNull();
        account.Name.Should().Be(name);
        account.Balance.Should().BeEquivalentTo(initialBalance);
        account.DomainEvents.Should().HaveCount(1);
    }

    [Fact]
    public void Create_ShouldThrowException_WhenNameIsEmpty()
    {
        // Arrange
        var initialBalance = new Money(0, new CurrencyCode("USD"));

        // Act
        Action act = () => Account.Create("", AccountType.Bank, initialBalance, _timeProvider);

        // Assert
        act.Should().Throw<ArgumentException>().WithMessage("*name*");
    }

    [Fact]
    public void Deposit_ShouldIncreaseBalance_WhenAmountIsPositive()
    {
        // Arrange
        var account = Account.Create("Wallet", AccountType.Cash, new Money(50m, new CurrencyCode("USD")), _timeProvider);
        
        // Act
        account.Deposit(new Money(10m, new CurrencyCode("USD")), _timeProvider);

        // Assert
        account.Balance.Amount.Should().Be(60m);
    }

    [Fact]
    public void Deposit_ShouldThrowException_WhenAmountIsNegativeOrZero()
    {
        // Arrange
        var account = Account.Create("Wallet", AccountType.Cash, new Money(50m, new CurrencyCode("USD")), _timeProvider);

        // Act
        Action act = () => account.Deposit(new Money(0m, new CurrencyCode("USD")), _timeProvider);

        // Assert
        act.Should().Throw<ArgumentException>().WithMessage("*positive*");
    }

    [Fact]
    public void Withdraw_ShouldDecreaseBalance_WhenAmountIsPositive()
    {
        // Arrange
        var account = Account.Create("Wallet", AccountType.Cash, new Money(50m, new CurrencyCode("USD")), _timeProvider);

        // Act
        account.Withdraw(new Money(20m, new CurrencyCode("USD")), _timeProvider);

        // Assert
        account.Balance.Amount.Should().Be(30m);
    }
}
