using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Pine.Application.Accounts.Commands;
using Pine.Core.Accounts;
using Xunit;

namespace Pine.Tests.Unit.Application.Accounts;

/// <summary>A bad currency is refused with a localisable key before the domain throws (TD-78).</summary>
public class CreateAccountCommandValidatorTests
{
    private static async Task<string[]> KeysFor(string currency) =>
        (await new CreateAccountCommandValidator().ValidateAsync(
            new CreateAccountCommand("Cash", currency, AccountType.Cash, 0m)))
        .Select(e => e.UserFacingMessage.Key).ToArray();

    [Theory]
    [InlineData("USD")]
    [InlineData(" eur ")]
    [InlineData("JPY")]
    public async Task ARealCurrency_IsAccepted(string currency) =>
        (await KeysFor(currency)).Should().BeEmpty();

    [Theory]
    [InlineData("ZZZ")]
    [InlineData("US")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("12$")]
    public async Task ANonCurrency_IsRefused_ByKey(string? currency) =>
        (await KeysFor(currency!)).Should().Equal("Validation.CurrencyCodeInvalid");
}
