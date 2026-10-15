using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using Pine.Application.Transactions.Commands;
using Pine.Core.Accounts;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Transactions;
using Xunit;

namespace Pine.Tests.Unit.Domain;

/// <summary>TD-95: the scale policy for a line item's quantity, and where it is enforced.</summary>
public class LineQuantityTests
{
    private static readonly CurrencyCode Usd = new("USD");
    private static readonly DateTimeOffset Date = new(2026, 3, 5, 0, 0, 0, TimeSpan.Zero);

    private static decimal D(string text) => decimal.Parse(text, CultureInfo.InvariantCulture);

    [Theory]
    [InlineData("1")]
    [InlineData("1.545")]
    [InlineData("12.301")]
    [InlineData("0.0001")]
    [InlineData("123456789012.3456")]
    public void AValidQuantity_IsAccepted(string text)
    {
        LineQuantity.IsValid(D(text)).Should().BeTrue();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("-0.0001")]
    [InlineData("1.00001")]
    [InlineData("12.30101")]
    public void ZeroNegativeAndOverPreciseQuantitiesAreRejected(string text)
    {
        var quantity = D(text);
        LineQuantity.IsValid(quantity).Should().BeFalse();
        var act = () => LineQuantity.EnsureValid(quantity);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TrailingZerosBeyondTheScaleAreNotExcess()
    {
        LineQuantity.HasExcessScale(1.5000000m).Should().BeFalse("the value is exactly 1.5; only a significant fifth digit is excess");
        LineQuantity.HasExcessScale(1.50001m).Should().BeTrue();
    }

    [Theory]
    [InlineData("0.33335", "0.3334")]
    [InlineData("0.33334", "0.3333")]
    [InlineData("0.33325", "0.3332")]
    [InlineData("-0.33325", "-0.3332")]
    [InlineData("0.00005", "0")]
    [InlineData("0.00015", "0.0002")]
    [InlineData("-0.33335", "-0.3334")]
    [InlineData("1.545", "1.545")]
    [InlineData("0.00004", "0")]
    public void ARecognisedQuantityIsRoundedHalfToEven(string input, string expected)
    {
        LineQuantity.RoundRecognised(D(input)).Should().Be(D(expected));
    }

    private static Transaction NewTransaction() =>
        Transaction.Create(Guid.NewGuid(), Date, "Fuel", new Money(-10m, Usd), TransactionType.Expense, TimeProvider.System);

    [Fact]
    public void AddLineItem_RefusesAnInvalidQuantity()
    {
        var tx = NewTransaction();
        var zero = () => tx.AddLineItem("Gas", 0m, new Money(-10m, Usd), new Money(-10m, Usd));
        var excess = () => tx.AddLineItem("Gas", 1.00001m, new Money(-10m, Usd), new Money(-10m, Usd));
        zero.Should().Throw<ArgumentException>();
        excess.Should().Throw<ArgumentException>();
        tx.LineItems.Should().BeEmpty();
    }

    [Fact]
    public void ReplaceLineItems_WithAnInvalidQuantity_LeavesTheAggregateUnmutated()
    {
        var tx = NewTransaction();
        tx.AddLineItem("Gas", 12.301m, new Money(-10m, Usd), new Money(-10m, Usd));
        var originalId = tx.LineItems.First().Id;

        var act = () => tx.ReplaceLineItems(new[]
        {
            new LineItemDefinition("Gas", 2m, new Money(-5m, Usd), new Money(-5m, Usd), LineItemKind.Product, originalId),
            new LineItemDefinition("Bad", -1m, new Money(-5m, Usd), new Money(-5m, Usd), LineItemKind.Product),
        });

        act.Should().Throw<ArgumentException>();
        tx.LineItems.Should().ContainSingle().Which.Quantity.Should().Be(12.301m, "the first definition must not have been applied");
    }

    private static (AddTransactionCommandValidator Validator, Account Account) ValidatorFor()
    {
        var account = Account.Create("Main", AccountType.Bank, new Money(100m, Usd), TimeProvider.System);
        var accounts = Substitute.For<IAccountRepository>();
        accounts.GetByIdAsync(account.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Account?>(account));
        return (new AddTransactionCommandValidator(accounts), account);
    }

    private static AddTransactionCommand WithQuantities(Guid accountId, params decimal[] quantities)
    {
        var items = new AddTransactionLineItemCommand[quantities.Length];
        for (var i = 0; i < quantities.Length; i++)
        {
            items[i] = new AddTransactionLineItemCommand("Item", quantities[i], -1m, -1m);
        }

        return new AddTransactionCommand(accountId, Date, "Shop", -quantities.Length, "USD", "", "Food", items);
    }

    [Fact]
    public async Task Validator_AcceptsFourDecimalQuantities()
    {
        var (validator, account) = ValidatorFor();
        (await validator.ValidateAsync(WithQuantities(account.Id, 1.545m, 12.301m))).Should().BeEmpty();
    }

    [Fact]
    public async Task Validator_RejectsAFifthDecimal_WithAMessage_AndDoesNotRound()
    {
        var (validator, account) = ValidatorFor();
        var errors = await validator.ValidateAsync(WithQuantities(account.Id, 1m, 1.23456m));

        var error = errors.Should().ContainSingle().Subject;
        error.UserFacingMessage.Key.Should().Be("Validation.LineItem.QuantityScale");
        error.UserFacingMessage.Arguments.Should().Equal(2, 4);
        error.PropertyName.Should().Contain("[1]");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public async Task Validator_RejectsAZeroOrNegativeQuantity(int quantity)
    {
        var (validator, account) = ValidatorFor();
        var errors = await validator.ValidateAsync(WithQuantities(account.Id, quantity));

        errors.Should().ContainSingle().Which.UserFacingMessage.Key.Should().Be("Validation.LineItem.QuantityNotPositive");
    }
}
