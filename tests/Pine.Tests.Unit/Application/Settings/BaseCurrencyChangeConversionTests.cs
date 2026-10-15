using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using Pine.Application.Budgets;
using Pine.Application.ExchangeRates.Commands;
using Pine.Application.Settings.Commands;
using Pine.Core.Accounts;
using Pine.Core.Budgets;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Configuration;
using Pine.Core.ExchangeRates;
using Pine.Core.Settings;
using Xunit;

namespace Pine.Tests.Unit.Application.Settings;

/// <summary>
/// Changing the base currency converts the budgets once at the entered rate in force, or is refused
/// (owner decision, 2026-10-14).
/// </summary>
public class BaseCurrencyChangeConversionTests
{
    private static readonly CurrencyCode Usd = new("USD");
    private static readonly CurrencyCode Eur = new("EUR");
    private static readonly CurrencyCode Jpy = new("JPY");
    private static readonly DateTimeOffset Today = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Today;
    }

    private sealed class Budgets : IBudgetRepository
    {
        public List<Budget> All { get; } = [];
        public List<Guid> Saved { get; } = [];
        public Task<Budget?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(All.FirstOrDefault(b => b.Id == id));
        public Task<IEnumerable<Budget>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<Budget>>(All.ToList());
        public Task SaveAsync(Budget budget, CancellationToken cancellationToken = default) { Saved.Add(budget.Id); return Task.CompletedTask; }
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class Rates : IExchangeRateRepository
    {
        public List<ExchangeRate> All { get; } = [];
        public Task<IReadOnlyList<ExchangeRate>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ExchangeRate>>(All.ToList());
        public Task<ExchangeRate?> GetAsync(CurrencyCode from, CurrencyCode target, DateTime effectiveDateUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult(All.FirstOrDefault(r => r.FromCurrency == from && r.ToCurrency == target && r.EffectiveDateUtc == ExchangeRate.DayOf(effectiveDateUtc)));
        public Task<ExchangeRate?> GetInForceAsync(CurrencyCode from, CurrencyCode target, DateTime asOfUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult(All.Where(r => r.FromCurrency == from && r.ToCurrency == target && r.EffectiveDateUtc <= asOfUtc).OrderByDescending(r => r.EffectiveDateUtc).FirstOrDefault());
        public Task SaveAsync(ExchangeRate rate, CancellationToken cancellationToken = default) { if (!All.Contains(rate)) All.Add(rate); return Task.CompletedTask; }
    }

    private readonly Budgets _budgets = new();
    private readonly Rates _rates = new();
    private readonly ISettingRepository _settings = Substitute.For<ISettingRepository>();
    private readonly IAccountRepository _accounts = Substitute.For<IAccountRepository>();

    private SaveSettingCommandValidator Validator() => new(_budgets, _rates, _accounts, new FixedTime());

    private SaveSettingCommandHandler Handler() => new(_settings, _budgets, _rates, _accounts, new FixedTime());

    private void Budget(string category, decimal amount, CurrencyCode currency) =>
        _budgets.All.Add(Pine.Core.Budgets.Budget.Create(category, new Money(amount, currency), BudgetPeriod.Monthly));

    private void Rate(CurrencyCode from, CurrencyCode to, decimal rate, int day) =>
        _rates.All.Add(ExchangeRate.Create(from, to, rate, new DateTime(2026, 10, day, 0, 0, 0, DateTimeKind.Utc)));

    private static SaveSettingCommand ChangeTo(string code) => new(BaseCurrency.SettingKey, code);

    [Fact]
    public async Task NoRateForThePair_RefusesTheChange_NamingThePairToEnter()
    {
        Budget("Food", 100m, Usd);

        var errors = await Validator().ValidateAsync(ChangeTo("EUR"));

        var error = errors.Should().ContainSingle().Subject;
        error.UserFacingMessage.Key.Should().Be("Validation.BaseCurrency.RateMissing");
        error.UserFacingMessage.Arguments.Should().Equal("USD", "EUR");
    }

    [Fact]
    public async Task NoRateForThePair_TheHandlerAlsoRefuses_AndTouchesNothing()
    {
        Budget("Food", 100m, Usd);

        var act = () => Handler().Handle(ChangeTo("EUR"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _budgets.All.Single().Limit.Should().Be(new Money(100m, Usd));
        _budgets.Saved.Should().BeEmpty();
        await _settings.DidNotReceive().SaveAsync(Arg.Any<Setting>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheInverseRate_IsNotUsed()
    {
        Budget("Food", 100m, Usd);
        Rate(Eur, Usd, 1.087m, 1);

        (await Validator().ValidateAsync(ChangeTo("EUR"))).Should().ContainSingle()
            .Which.UserFacingMessage.Key.Should().Be("Validation.BaseCurrency.RateMissing");
    }

    [Fact]
    public async Task ARateNotYetInForce_IsNotUsed()
    {
        Budget("Food", 100m, Usd);
        Rate(Usd, Eur, 0.92m, 20); // 20 October, after "today" (14 October)

        (await Validator().ValidateAsync(ChangeTo("EUR"))).Should().ContainSingle();
    }

    [Fact]
    public async Task TheLatestRateInForce_Converts_RoundedAndRecorded_AndTheSettingIsStored()
    {
        Budget("Food", 100.10m, Usd);
        Budget("Rent", 800m, Usd);
        Rate(Usd, Eur, 0.50m, 1);
        Rate(Usd, Eur, 0.9125m, 10);   // the one in force
        Rate(Usd, Eur, 5m, 20);        // future

        (await Validator().ValidateAsync(ChangeTo("eur"))).Should().BeEmpty();
        await Handler().Handle(ChangeTo("eur"), CancellationToken.None);

        var food = _budgets.All.Single(b => b.Category == "Food");
        food.Limit.Should().Be(new Money(91.34m, Eur), "100.10 x 0.9125 = 91.34125 -> 91.34");
        food.PreviousLimitAmount.Should().Be(100.10m);
        food.PreviousLimitCurrency.Should().Be("USD");
        food.LimitConversionRate.Should().Be(0.9125m);
        food.LimitConvertedAtUtc.Should().Be(Today.UtcDateTime);
        _budgets.All.Single(b => b.Category == "Rent").Limit.Should().Be(new Money(730m, Eur));
        _budgets.Saved.Should().HaveCount(2);
        await _settings.Received(1).SaveAsync(Arg.Is<Setting>(s => s.Key == BaseCurrency.SettingKey && s.Value == "EUR"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ABudgetAlreadyInTheTargetCurrency_NeedsNoRate_AndIsNotTouched()
    {
        Budget("Food", 100m, Eur);

        (await Validator().ValidateAsync(ChangeTo("EUR"))).Should().BeEmpty();
        await Handler().Handle(ChangeTo("EUR"), CancellationToken.None);

        _budgets.Saved.Should().BeEmpty();
        _budgets.All.Single().PreviousLimitCurrency.Should().BeNull();
    }

    [Fact]
    public async Task OneMissingRate_RefusesTheWholeChange_EvenWhenAnotherBudgetCouldConvert()
    {
        Budget("Food", 100m, Usd);
        Budget("Trips", 5000m, Jpy);
        Rate(Usd, Eur, 0.9m, 1);

        var errors = await Validator().ValidateAsync(ChangeTo("EUR"));

        var error = errors.Should().ContainSingle().Subject;
        error.UserFacingMessage.Arguments.Should().Equal("JPY", "EUR");

        var act = () => Handler().Handle(ChangeTo("EUR"), CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();
        _budgets.Saved.Should().BeEmpty("a partial conversion would leave the vault in two currencies");
    }

    [Fact]
    public async Task ABudgetThatWouldBecomeZero_RefusesTheChange()
    {
        Budget("Tiny", 1m, Usd);
        Rate(Usd, Jpy, 0.3m, 1);

        var errors = await Validator().ValidateAsync(ChangeTo("JPY"));

        errors.Should().ContainSingle().Which.UserFacingMessage.Key.Should().Be("Validation.BaseCurrency.BudgetWouldBeZero");
    }

    [Fact]
    public async Task ClearingTheSetting_ConvertsToTheOldestAccountsCurrency_OrRefuses()
    {
        _accounts.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new[]
        {
            Account.Create("Euro account", AccountType.Bank, new Money(1m, Eur), TimeProvider.System),
        });
        Budget("Food", 100m, Usd);

        (await Validator().ValidateAsync(ChangeTo(string.Empty))).Should().ContainSingle()
            .Which.UserFacingMessage.Arguments.Should().Equal("USD", "EUR");
    }

    [Fact]
    public async Task OtherSettings_AreNeverAConversion()
    {
        Budget("Food", 100m, Usd);

        (await Validator().ValidateAsync(new SaveSettingCommand("Appearance.Theme", "1"))).Should().BeEmpty();
        await Handler().Handle(new SaveSettingCommand("Appearance.Theme", "1"), CancellationToken.None);

        _budgets.Saved.Should().BeEmpty();
    }

    [Fact]
    public async Task TheRateCommand_IsManualOnly_AndReplacesTheSamePairAndDay()
    {
        var handler = new SetExchangeRateCommandHandler(_rates);

        var first = await handler.Handle(new SetExchangeRateCommand("usd", "eur", 0.9m, Today.UtcDateTime), CancellationToken.None);
        var second = await handler.Handle(new SetExchangeRateCommand("USD", "EUR", 0.95m, Today.UtcDateTime.AddHours(5)), CancellationToken.None);

        second.Should().Be(first);
        var rate = _rates.All.Should().ContainSingle().Subject;
        rate.Rate.Should().Be(0.95m);
        rate.Source.Should().Be("manual");
    }

    [Theory]
    [InlineData("ZZZ", "EUR", "1", "Validation.CurrencyCodeInvalid")]
    [InlineData("USD", "usd", "1", "Validation.ExchangeRate.SameCurrency")]
    [InlineData("USD", "EUR", "0", "Validation.ExchangeRate.NotPositive")]
    [InlineData("USD", "EUR", "-1", "Validation.ExchangeRate.NotPositive")]
    [InlineData("USD", "EUR", "0.123456789", "Validation.ExchangeRate.TooPrecise")]
    [InlineData("USD", "EUR", "2000000000", "Validation.ExchangeRate.TooLarge")]
    public async Task TheRateCommandValidator_RefusesWhatIsNotARate(string from, string to, string rate, string key)
    {
        var errors = await new SetExchangeRateCommandValidator().ValidateAsync(new SetExchangeRateCommand(
            from, to, decimal.Parse(rate, System.Globalization.CultureInfo.InvariantCulture), Today.UtcDateTime));

        errors.Should().ContainSingle().Which.UserFacingMessage.Key.Should().Be(key);
    }

    [Fact]
    public async Task TheRateCommandValidator_AcceptsAGoodRate() =>
        (await new SetExchangeRateCommandValidator().ValidateAsync(new SetExchangeRateCommand("USD", "EUR", 0.92m, Today.UtcDateTime))).Should().BeEmpty();
}
