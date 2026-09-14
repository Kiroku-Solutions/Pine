using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using Pine.Application.Budgets.Commands;
using Pine.Core.Budgets;
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

    public CreateBudgetCommandValidatorTests()
    {
        _budgets.GetAllAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IEnumerable<Budget>>(Array.Empty<Budget>()));
    }

    private async Task<IReadOnlyList<string>> KeysFor(CreateBudgetCommand command) =>
        (await new CreateBudgetCommandValidator(_budgets).ValidateAsync(command))
            .Select(error => error.UserFacingMessage.Key)
            .ToList();

    [Fact]
    public async Task AUsableBudget_IsAccepted()
    {
        (await KeysFor(new CreateBudgetCommand("Groceries", 500m, "USD", BudgetPeriod.Monthly))).Should().BeEmpty();
        (await KeysFor(new CreateBudgetCommand("Holiday", 1500m, "EUR", BudgetPeriod.Custom, May1, June1))).Should().BeEmpty();
    }

    [Theory]
    [InlineData("", 500, "USD", "Validation.Budget.CategoryRequired")]
    [InlineData("   ", 500, "USD", "Validation.Budget.CategoryRequired")]
    [InlineData("Groceries", 0, "USD", "Validation.Budget.LimitNotPositive")]
    [InlineData("Groceries", -1, "USD", "Validation.Budget.LimitNotPositive")]
    [InlineData("Groceries", 500, "ZZZ", "Validation.CurrencyCodeInvalid")]
    [InlineData("Groceries", 10.001, "USD", "Validation.Budget.LimitTooPrecise")]
    [InlineData("Groceries", 1500.5, "JPY", "Validation.Budget.LimitTooPrecise")]
    public async Task AnUnmeasurableBudget_IsRefused_ByKey(string category, decimal limit, string currency, string expectedKey)
    {
        (await KeysFor(new CreateBudgetCommand(category, limit, currency, BudgetPeriod.Monthly))).Should().Equal(expectedKey);
    }

    [Fact]
    public async Task ALimitAtTheCurrencysOwnScale_IsNotTooPrecise()
    {
        (await KeysFor(new CreateBudgetCommand("Groceries", 12.345m, "KWD", BudgetPeriod.Monthly))).Should().BeEmpty(
            "KWD has three decimal places");
        (await KeysFor(new CreateBudgetCommand("Groceries", 1500m, "JPY", BudgetPeriod.Monthly))).Should().BeEmpty();
    }

    [Fact]
    public async Task ACustomBudget_NeedsBothDates_InOrder()
    {
        (await KeysFor(new CreateBudgetCommand("Holiday", 100m, "USD", BudgetPeriod.Custom, May1, null)))
            .Should().Equal("Validation.Budget.CustomNeedsDates");
        (await KeysFor(new CreateBudgetCommand("Holiday", 100m, "USD", BudgetPeriod.Custom, June1, May1)))
            .Should().Equal("Validation.Budget.DatesOutOfOrder");
        (await KeysFor(new CreateBudgetCommand("Holiday", 100m, "USD", BudgetPeriod.Custom, May1, May1)))
            .Should().ContainSingle("the window is half-open, so equal dates hold no instant")
            .Which.Should().Be("Validation.Budget.DatesOutOfOrder");
    }

    /// <summary>
    /// The aggregate accepts a dateless bi-weekly budget; the page would then say "not known" for ever.
    /// </summary>
    [Fact]
    public async Task ADatelessBiWeeklyBudget_IsRefused_ButOneWithDatesIsNot()
    {
        (await KeysFor(new CreateBudgetCommand("Groceries", 100m, "USD", BudgetPeriod.BiWeekly)))
            .Should().Equal("Validation.Budget.BiWeeklyNeedsDates");
        (await KeysFor(new CreateBudgetCommand("Groceries", 100m, "USD", BudgetPeriod.BiWeekly, May1, May1.AddDays(14))))
            .Should().BeEmpty();
    }

    [Fact]
    public async Task EveryProblem_IsReportedAtOnce()
    {
        (await KeysFor(new CreateBudgetCommand(" ", 0m, "ZZZ", BudgetPeriod.Custom)))
            .Should().BeEquivalentTo("Validation.Budget.CategoryRequired", "Validation.Budget.LimitNotPositive",
                "Validation.CurrencyCodeInvalid", "Validation.Budget.CustomNeedsDates");
    }

    [Fact]
    public async Task ADuplicate_IsRefused_ComparingTheCategoryTrimmedAndCaseIntact()
    {
        var existing = Budget.Create("Groceries", new Money(500m, new CurrencyCode("USD")), BudgetPeriod.Monthly);
        _budgets.GetAllAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IEnumerable<Budget>>(new[] { existing }));

        var errors = await new CreateBudgetCommandValidator(_budgets)
            .ValidateAsync(new CreateBudgetCommand("  Groceries ", 900m, "usd", BudgetPeriod.Monthly));

        errors.Should().ContainSingle().Which.UserFacingMessage.Key.Should().Be("Validation.Budget.AlreadyExists");
        errors[0].UserFacingMessage.Arguments.Should().Equal("Groceries", "USD");

        (await KeysFor(new CreateBudgetCommand("groceries", 900m, "USD", BudgetPeriod.Monthly))).Should().BeEmpty(
            "a user who spells two categories apart has two categories");
        (await KeysFor(new CreateBudgetCommand("Groceries", 900m, "EUR", BudgetPeriod.Monthly))).Should().BeEmpty();
        (await KeysFor(new CreateBudgetCommand("Groceries", 900m, "USD", BudgetPeriod.Weekly))).Should().BeEmpty();
    }

    [Fact]
    public async Task ACustomDuplicate_IsOnlyADuplicateOverTheSameDates()
    {
        var existing = Budget.Create("Holiday", new Money(500m, new CurrencyCode("USD")), BudgetPeriod.Custom, May1, June1);
        _budgets.GetAllAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IEnumerable<Budget>>(new[] { existing }));

        (await KeysFor(new CreateBudgetCommand("Holiday", 1m, "USD", BudgetPeriod.Custom, May1, June1)))
            .Should().Equal("Validation.Budget.AlreadyExists");
        (await KeysFor(new CreateBudgetCommand("Holiday", 1m, "USD", BudgetPeriod.Custom, June1, June1.AddDays(30))))
            .Should().BeEmpty();
    }
}
