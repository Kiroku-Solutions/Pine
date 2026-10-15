using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using Pine.Application.Budgets;
using Pine.Application.Budgets.Commands;
using Pine.Core.Accounts;
using Pine.Core.Budgets;
using Pine.Core.Configuration;
using Pine.Core.Settings;
using Pine.Core.Common.ValueObjects;
using Xunit;

namespace Pine.Tests.Unit.Application.Budgets;

/// <summary>
/// What a new budget is refused for, and in which words (TD-152).
/// </summary>
/// <remarks>
/// Until the budgets page could create a budget, nothing sent <see cref="CreateBudgetCommand"/>, so it
/// had no validator: a blank category or an out-of-scale limit reached <c>Budget.Create</c> and
/// <c>Money</c>, whose English exceptions name constructor parameters. Each refusal below is asserted by
/// its KEY, because the key is what the screen words in the user's language.
/// </remarks>
public class CreateBudgetCommandValidatorTests
{
    private static readonly DateTimeOffset May1 = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset June1 = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly IBudgetRepository _budgets = Substitute.For<IBudgetRepository>();
    private readonly ISettingRepository _settings = Substitute.For<ISettingRepository>();
    private readonly IAccountRepository _accounts = Substitute.For<IAccountRepository>();

    private CreateBudgetCommandValidator Validator() => new(_budgets, _settings, _accounts);

    /// <summary>The vault's base currency, set explicitly (TD-63).</summary>
    private void BaseCurrencyIs(string code) =>
        _settings.GetAsync(BaseCurrency.SettingKey, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Setting?>(Setting.Create(BaseCurrency.SettingKey, code)));

    public CreateBudgetCommandValidatorTests()
    {
        _budgets.GetAllAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IEnumerable<Budget>>(Array.Empty<Budget>()));
        _settings.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<Setting?>(null));
        _accounts.GetAllAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<Account>>(Array.Empty<Account>()));
        BaseCurrencyIs("USD");
    }

    private async Task<IReadOnlyList<string>> KeysFor(CreateBudgetCommand command) =>
        (await Validator().ValidateAsync(command))
            .Select(error => error.UserFacingMessage.Key)
            .ToList();

    [Fact]
    public async Task AUsableBudget_IsAccepted()
    {
        (await KeysFor(new CreateBudgetCommand("Groceries", 500m, BudgetPeriod.Monthly))).Should().BeEmpty();
        (await KeysFor(new CreateBudgetCommand("Holiday", 1500m, BudgetPeriod.Custom, May1, June1))).Should().BeEmpty();
    }

    /// <summary>TD-63: with neither a setting nor an account the vault has no currency to measure a budget in.</summary>
    [Fact]
    public async Task AVaultWithNoBaseCurrency_RefusesABudget_ByKey()
    {
        _settings.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<Setting?>(null));

        (await KeysFor(new CreateBudgetCommand("Groceries", 500m, BudgetPeriod.Monthly)))
            .Should().Equal("Validation.Budget.NoBaseCurrency");
    }

    /// <summary>TD-63: with no setting, the oldest account's currency is the base currency.</summary>
    [Fact]
    public async Task WithNoSetting_TheOldestAccountsCurrency_IsTheBaseCurrency()
    {
        _settings.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<Setting?>(null));
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(May1);
        var older = Account.Create("Old", AccountType.Bank, new Pine.Core.Common.ValueObjects.Money(0m, new Pine.Core.Common.ValueObjects.CurrencyCode("JPY")), clock);
        clock.Advance(TimeSpan.FromDays(1));
        var newer = Account.Create("New", AccountType.Bank, new Pine.Core.Common.ValueObjects.Money(0m, new Pine.Core.Common.ValueObjects.CurrencyCode("USD")), clock);
        _accounts.GetAllAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<Account>>(new[] { newer, older }));

        // 1500.5 is too precise for JPY (the oldest account's currency), fine for USD.
        (await KeysFor(new CreateBudgetCommand("Groceries", 1500.5m, BudgetPeriod.Monthly)))
            .Should().Equal("Validation.Budget.LimitTooPrecise");
    }

    [Theory]
    [InlineData("", 500, "USD", "Validation.Budget.CategoryRequired")]
    [InlineData("   ", 500, "USD", "Validation.Budget.CategoryRequired")]
    [InlineData("Groceries", 0, "USD", "Validation.Budget.LimitNotPositive")]
    [InlineData("Groceries", -1, "USD", "Validation.Budget.LimitNotPositive")]
    [InlineData("Groceries", 10.001, "USD", "Validation.Budget.LimitTooPrecise")]
    [InlineData("Groceries", 1500.5, "JPY", "Validation.Budget.LimitTooPrecise")]
    public async Task AnUnmeasurableBudget_IsRefused_ByKey(string category, decimal limit, string baseCurrency, string expectedKey)
    {
        BaseCurrencyIs(baseCurrency);
        (await KeysFor(new CreateBudgetCommand(category, limit, BudgetPeriod.Monthly))).Should().Equal(expectedKey);
    }

    [Fact]
    public async Task ALimitAtTheCurrencysOwnScale_IsNotTooPrecise()
    {
        BaseCurrencyIs("KWD");
        (await KeysFor(new CreateBudgetCommand("Groceries", 12.345m, BudgetPeriod.Monthly))).Should().BeEmpty(
            "KWD has three decimal places");
        BaseCurrencyIs("JPY");
        (await KeysFor(new CreateBudgetCommand("Groceries", 1500m, BudgetPeriod.Monthly))).Should().BeEmpty();
    }

    [Fact]
    public async Task ACustomBudget_NeedsBothDates_InOrder()
    {
        (await KeysFor(new CreateBudgetCommand("Holiday", 100m, BudgetPeriod.Custom, May1, null)))
            .Should().Equal("Validation.Budget.CustomNeedsDates");
        (await KeysFor(new CreateBudgetCommand("Holiday", 100m, BudgetPeriod.Custom, June1, May1)))
            .Should().Equal("Validation.Budget.DatesOutOfOrder");
        (await KeysFor(new CreateBudgetCommand("Holiday", 100m, BudgetPeriod.Custom, May1, May1)))
            .Should().ContainSingle("the window is half-open, so equal dates hold no instant")
            .Which.Should().Be("Validation.Budget.DatesOutOfOrder");
    }

    /// <summary>
    /// The aggregate accepts a dateless bi-weekly budget; the page would then say "not known" for ever.
    /// </summary>
    [Fact]
    public async Task ADatelessBiWeeklyBudget_IsRefused_ButOneWithDatesIsNot()
    {
        (await KeysFor(new CreateBudgetCommand("Groceries", 100m, BudgetPeriod.BiWeekly)))
            .Should().Equal("Validation.Budget.BiWeeklyNeedsDates");
        (await KeysFor(new CreateBudgetCommand("Groceries", 100m, BudgetPeriod.BiWeekly, May1, May1.AddDays(14))))
            .Should().BeEmpty();
    }

    [Fact]
    public async Task EveryProblem_IsReportedAtOnce()
    {
        _settings.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<Setting?>(null));
        (await KeysFor(new CreateBudgetCommand(" ", 0m, BudgetPeriod.Custom)))
            .Should().BeEquivalentTo("Validation.Budget.CategoryRequired", "Validation.Budget.LimitNotPositive",
                "Validation.Budget.NoBaseCurrency", "Validation.Budget.CustomNeedsDates");
    }

    [Fact]
    public async Task ADuplicate_IsRefused_ComparingTheCategoryTrimmedAndCaseIntact()
    {
        var existing = Budget.Create("Groceries", new Money(500m, new CurrencyCode("USD")), BudgetPeriod.Monthly);
        _budgets.GetAllAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IEnumerable<Budget>>(new[] { existing }));

        var errors = await Validator()
            .ValidateAsync(new CreateBudgetCommand("  Groceries ", 900m, BudgetPeriod.Monthly));

        errors.Should().ContainSingle().Which.UserFacingMessage.Key.Should().Be("Validation.Budget.AlreadyExists");
        errors[0].UserFacingMessage.Arguments.Should().Equal("Groceries", "USD");

        (await KeysFor(new CreateBudgetCommand("groceries", 900m, BudgetPeriod.Monthly))).Should().BeEmpty(
            "a user who spells two categories apart has two categories");
        (await KeysFor(new CreateBudgetCommand("Groceries", 900m, BudgetPeriod.Weekly))).Should().BeEmpty();

        // A legacy budget in another currency (made before TD-63) is kept, and is not this budget's duplicate.
        BaseCurrencyIs("EUR");
        (await KeysFor(new CreateBudgetCommand("Groceries", 900m, BudgetPeriod.Monthly))).Should().BeEmpty();
    }

    [Fact]
    public async Task ACustomDuplicate_IsOnlyADuplicateOverTheSameDates()
    {
        var existing = Budget.Create("Holiday", new Money(500m, new CurrencyCode("USD")), BudgetPeriod.Custom, May1, June1);
        _budgets.GetAllAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IEnumerable<Budget>>(new[] { existing }));

        (await KeysFor(new CreateBudgetCommand("Holiday", 1m, BudgetPeriod.Custom, May1, June1)))
            .Should().Equal("Validation.Budget.AlreadyExists");
        (await KeysFor(new CreateBudgetCommand("Holiday", 1m, BudgetPeriod.Custom, June1, June1.AddDays(30))))
            .Should().BeEmpty();
    }

    /// <summary>
    /// Half a window is refused for a calendar period, which would silently ignore it (review finding m7).
    /// </summary>
    [Fact]
    public async Task OneDateAlone_IsRefused_ForACalendarPeriod()
    {
        (await KeysFor(new CreateBudgetCommand("Groceries", 100m, BudgetPeriod.Monthly, May1, null)))
            .Should().Equal("Validation.Budget.DatesIncomplete");
        (await KeysFor(new CreateBudgetCommand("Groceries", 100m, BudgetPeriod.Weekly, null, June1)))
            .Should().Equal("Validation.Budget.DatesIncomplete");
        (await KeysFor(new CreateBudgetCommand("Groceries", 100m, BudgetPeriod.Monthly, May1, June1)))
            .Should().BeEmpty("both dates make an explicit window, which ResolveWindow honours");
    }
}
