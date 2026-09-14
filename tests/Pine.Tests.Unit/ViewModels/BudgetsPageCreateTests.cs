using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MediatR;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Pine.Application.Accounts.Queries;
using Pine.Application.Budgets.Commands;
using Pine.Application.Budgets.Queries;
using Pine.Application.Common;
using Pine.Core.Accounts;
using Pine.Core.Budgets;
using Pine.Core.Common.ValueObjects;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.ViewModels;
using Xunit;

namespace Pine.Tests.Unit.ViewModels;

/// <summary>
/// The budgets page can create a budget (TD-152, post-audit plan item 3.0).
/// </summary>
/// <remarks>
/// <para>
/// WHAT WAS WRONG. The page measured spend against budgets and nothing in the product could create one:
/// <c>CreateBudgetCommand</c> had no sender, which <c>EveryRequestHasASenderTests</c> found in plan item
/// 2.5. On every real vault the page was an empty list with no sentence explaining it.
/// </para>
/// <para>
/// These are view-model tests. That the command really writes a budget the progress query then
/// measures is <c>CreateBudgetEndToEndTests</c>' job, against a real vault.
/// </para>
/// </remarks>
public class BudgetsPageCreateTests
{
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly Localizer _localizer = new(CultureInfo.GetCultureInfo("en-US"));
    private readonly List<CreateBudgetCommand> _sent = new();

    public BudgetsPageCreateTests()
    {
        _mediator.Send(Arg.Any<GetBudgetProgressQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<BudgetProgress>>(Array.Empty<BudgetProgress>()));
        _mediator.Send(Arg.Do<CreateBudgetCommand>(_sent.Add), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Guid.NewGuid()));
    }

    private void WithAccounts(params string[] currencies) =>
        _mediator.Send(Arg.Any<GetAccountsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Account>>(currencies
                .Select((code, i) => Account.Create($"Account {i}", AccountType.Bank, new Money(0m, new CurrencyCode(code)), TimeProvider.System))
                .ToList()));

    private BudgetsViewModel Page() => new(_mediator, _localizer);

    [Fact]
    public async Task OpeningTheForm_OffersTheCurrenciesOfTheUsersAccounts_Once_AndNoFortnight()
    {
        WithAccounts("USD", "EUR", "USD");
        using var page = Page();

        await page.BeginCreateCommand.ExecuteAsync(null);

        page.IsCreating.Should().BeTrue();
        page.CurrencyOptions.Should().Equal("EUR", "USD");
        page.NewCurrency.Should().Be("EUR");
        page.PeriodOptions.Select(o => o.Period).Should().Equal(
            BudgetPeriod.Monthly, BudgetPeriod.Weekly, BudgetPeriod.Yearly, BudgetPeriod.Custom);
        page.PeriodOptions.Select(o => o.Label).Should().Equal("Monthly", "Weekly", "Yearly", "Custom dates");
        page.NewPeriod!.Period.Should().Be(BudgetPeriod.Monthly);
        page.NeedsDates.Should().BeFalse();
    }

    /// <summary>With no account there is no currency a budget could be measured in, so the form does not open.</summary>
    [Fact]
    public async Task WithNoAccounts_TheFormDoesNotOpen_AndSaysWhy()
    {
        WithAccounts();
        using var page = Page();

        await page.BeginCreateCommand.ExecuteAsync(null);

        page.IsCreating.Should().BeFalse();
        page.CreateMessage.Should().Be(_localizer["Budgets.Create.NoAccounts"]);
    }

    [Fact]
    public async Task Saving_SendsWhatTheFormHolds_ClosesTheForm_AndReloads()
    {
        WithAccounts("USD");
        using var page = Page();
        await page.BeginCreateCommand.ExecuteAsync(null);

        page.NewCategory = "Groceries";
        page.NewLimit = 400m;
        page.NewPeriod = page.PeriodOptions.Single(o => o.Period == BudgetPeriod.Weekly);

        await page.SaveBudgetCommand.ExecuteAsync(null);

        _sent.Should().ContainSingle().Which.Should().Be(
            new CreateBudgetCommand("Groceries", 400m, "USD", BudgetPeriod.Weekly, null, null));
        page.IsCreating.Should().BeFalse();
        page.CreateMessage.Should().BeEmpty();
        await _mediator.Received(1).Send(Arg.Any<GetBudgetProgressQuery>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The day the user picks as the last is inside the budget; the model's window is half-open.
    /// </summary>
    [Fact]
    public async Task ACustomBudget_SendsTheDayAfterTheLastDayPicked_AsItsEnd()
    {
        WithAccounts("USD");
        using var page = Page();
        await page.BeginCreateCommand.ExecuteAsync(null);

        page.NewCategory = "Holiday";
        page.NewLimit = 1500m;
        page.NewPeriod = page.PeriodOptions.Single(o => o.Period == BudgetPeriod.Custom);
        page.NeedsDates.Should().BeTrue();
        page.NewStartDate = new DateTimeOffset(2026, 7, 1, 15, 30, 0, TimeSpan.FromHours(-5));
        page.NewLastDate = new DateTimeOffset(2026, 7, 31, 9, 0, 0, TimeSpan.FromHours(-5));

        await page.SaveBudgetCommand.ExecuteAsync(null);

        var sent = _sent.Should().ContainSingle().Subject;
        sent.StartDate!.Value.DateTime.Should().Be(new DateTime(2026, 7, 1), "the start is the beginning of the day picked");
        sent.EndDate!.Value.DateTime.Should().Be(new DateTime(2026, 8, 1), "31 July is included, so the window ends at the start of 1 August");
    }

    /// <summary>A period other than custom sends no dates, even if the pickers were filled before switching.</summary>
    [Fact]
    public async Task SwitchingAwayFromCustom_SendsNoDates()
    {
        WithAccounts("USD");
        using var page = Page();
        await page.BeginCreateCommand.ExecuteAsync(null);

        page.NewCategory = "Groceries";
        page.NewLimit = 10m;
        page.NewPeriod = page.PeriodOptions.Single(o => o.Period == BudgetPeriod.Custom);
        page.NewStartDate = DateTimeOffset.Now;
        page.NewLastDate = DateTimeOffset.Now.AddDays(3);
        page.NewPeriod = page.PeriodOptions.Single(o => o.Period == BudgetPeriod.Monthly);

        await page.SaveBudgetCommand.ExecuteAsync(null);

        _sent.Should().ContainSingle().Which.StartDate.Should().BeNull();
        _sent[0].EndDate.Should().BeNull();
    }

    /// <summary>A refusal is shown in the screen's language, and the form stays open with what was typed.</summary>
    [Fact]
    public async Task ARefusal_IsShown_InTheScreensLanguage_AndTheFormStaysOpen()
    {
        WithAccounts("USD");
        _mediator.Send(Arg.Any<CreateBudgetCommand>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestValidationException(new[]
            {
                new ValidationError("Amount", "limit not positive", ValidationText.BudgetLimitNotPositive()),
            }));
        using var page = Page();
        await page.BeginCreateCommand.ExecuteAsync(null);
        page.NewCategory = "Groceries";

        await page.SaveBudgetCommand.ExecuteAsync(null);

        page.IsCreating.Should().BeTrue();
        page.NewCategory.Should().Be("Groceries");
        page.CreateMessage.Should().Be("The limit must be greater than zero.");

        _localizer.SetLanguage("es");
        page.CreateMessage.Should().Be("El límite debe ser mayor que cero.");
    }

    [Fact]
    public async Task AFailureThatIsNotARefusal_SaysNothingWasSaved()
    {
        WithAccounts("USD");
        _mediator.Send(Arg.Any<CreateBudgetCommand>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("disk full at C:\\Users\\someone"));
        using var page = Page();
        await page.BeginCreateCommand.ExecuteAsync(null);

        await page.SaveBudgetCommand.ExecuteAsync(null);

        page.CreateMessage.Should().Be(_localizer["Budgets.Create.Failure"]).And.NotContain("disk");
        page.IsSaving.Should().BeFalse();
    }

    [Fact]
    public async Task Cancelling_ClosesTheForm_AndSendsNothing()
    {
        WithAccounts("USD");
        using var page = Page();
        await page.BeginCreateCommand.ExecuteAsync(null);

        page.CancelCreateCommand.Execute(null);

        page.IsCreating.Should().BeFalse();
        _sent.Should().BeEmpty();
    }

    /// <summary>An empty page says it is empty, and stops saying so when a budget appears or a load fails.</summary>
    [Fact]
    public async Task AnEmptyPage_SaysSo_OnlyWhenItReallyIsEmpty()
    {
        using var page = Page();
        await page.InitializeAsync(CancellationToken.None);

        page.IsEmpty.Should().BeTrue();

        page.Budgets.Add(BudgetProgressViewModel.From(
            new BudgetProgress(Guid.NewGuid(), "Groceries", new Money(10m, new CurrencyCode("USD")), null, null, null, 0, BudgetPeriod.Monthly),
            _localizer));
        page.IsEmpty.Should().BeFalse();

        page.Budgets.Clear();
        page.ErrorMessage = "could not load";
        page.IsEmpty.Should().BeFalse("a failed load is not an empty list");
    }

    [Theory]
    [InlineData(BudgetPeriod.Weekly, "Weekly")]
    [InlineData(BudgetPeriod.BiWeekly, "Every two weeks")]
    [InlineData(BudgetPeriod.Monthly, "Monthly")]
    [InlineData(BudgetPeriod.Yearly, "Yearly")]
    [InlineData(BudgetPeriod.Custom, "Custom dates")]
    public void EveryRow_NamesItsPeriod(BudgetPeriod period, string expected)
    {
        var row = BudgetProgressViewModel.From(
            new BudgetProgress(Guid.NewGuid(), "Groceries", new Money(10m, new CurrencyCode("USD")), null, null, null, 0, period),
            _localizer);

        row.PeriodText.Should().Be(expected);
    }

    [Fact]
    public void EveryPeriod_HasAName()
    {
        foreach (var period in Enum.GetValues<BudgetPeriod>())
        {
            _localizer[BudgetProgressViewModel.PeriodKey(period)].Should().NotStartWith("!").And.NotStartWith("Budgets.");
        }
    }
}
