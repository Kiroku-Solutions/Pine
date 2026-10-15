using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using Pine.Application.Budgets;
using Pine.Application.Settings.Commands;
using Pine.Core.Configuration;
using Pine.Core.Settings;
using Xunit;

namespace Pine.Tests.Unit.Application.Settings;

/// <summary>TD-63 follow-up: the vault base currency is a user-chosen setting and must be an ISO 4217 code.</summary>
public class BaseCurrencySettingTests
{
    private static Task<System.Collections.Generic.IReadOnlyList<Pine.Application.Common.ValidationError>> Validate(string key, string value) =>
        new SaveSettingCommandValidator().ValidateAsync(new SaveSettingCommand(key, value), CancellationToken.None);

    [Theory]
    [InlineData("EUR")]
    [InlineData("usd")]
    [InlineData(" jpy ")]
    [InlineData("")]
    public async Task AValidCodeOrAnEmptyClear_IsAccepted(string value) =>
        (await Validate(BaseCurrency.SettingKey, value)).Should().BeEmpty();

    [Theory]
    [InlineData("ZZZ")]
    [InlineData("US")]
    [InlineData("EURO")]
    [InlineData("12A")]
    [InlineData("  ")]
    public async Task ANonIsoValue_IsRefused_OnTheValueField(string value)
    {
        var errors = await Validate(BaseCurrency.SettingKey, value);

        errors.Should().ContainSingle().Which.PropertyName.Should().Be(nameof(SaveSettingCommand.Value));
    }

    [Fact]
    public async Task OtherKeys_AreNotValidatedAsCurrencies() =>
        (await Validate("Appearance.Theme", "not a currency")).Should().BeEmpty();

    [Fact]
    public async Task TheHandler_StoresTheCanonicalCode()
    {
        var repository = Substitute.For<ISettingRepository>();

        await new SaveSettingCommandHandler(repository, NSubstitute.Substitute.For<Pine.Core.Budgets.IBudgetRepository>(), NSubstitute.Substitute.For<Pine.Core.ExchangeRates.IExchangeRateRepository>(), NSubstitute.Substitute.For<Pine.Core.Accounts.IAccountRepository>(), System.TimeProvider.System).Handle(
            new SaveSettingCommand(BaseCurrency.SettingKey, " eur "), CancellationToken.None);

        await repository.Received(1).SaveAsync(
            Arg.Is<Setting>(s => s.Key == BaseCurrency.SettingKey && s.Value == "EUR"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnExplicitSetting_WinsOverTheOldestAccount()
    {
        var settings = Substitute.For<ISettingRepository>();
        settings.GetAsync(BaseCurrency.SettingKey, Arg.Any<CancellationToken>())
            .Returns(Setting.Create(BaseCurrency.SettingKey, "CHF"));
        var accounts = Substitute.For<Pine.Core.Accounts.IAccountRepository>();
        accounts.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new[]
        {
            Pine.Core.Accounts.Account.Create("A", Pine.Core.Accounts.AccountType.Bank,
                new Pine.Core.Common.ValueObjects.Money(1m, new Pine.Core.Common.ValueObjects.CurrencyCode("USD")), System.TimeProvider.System),
        });

        (await BaseCurrency.ResolveAsync(settings, accounts))!.Value.Should().Be("CHF");
    }
}
