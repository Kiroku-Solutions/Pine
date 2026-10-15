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

    /// <summary>The vault's base currency as the page reads it (TD-63); none means no account and no setting.</summary>
    private void WithBaseCurrency(string? code) =>
        _mediator.Send(Arg.Any<GetBaseCurrencyQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>(code));

    private BudgetsViewModel Page() => new(_mediator, _localizer);

    [Fact]
    public async Task OpeningTheForm_ShowsTheVaultsBaseCurrency_AndNoFortnight()
    {
        WithBaseCurrency("EUR");
        using var page = Page();

        await page.BeginCreateCommand.ExecuteAsync(null);

        page.IsCreating.Should().BeTrue();
        page.BaseCurrency.Should().Be("EUR");
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
        WithBaseCurrency(null);
        using var page = Page();

        await page.BeginCreateCommand.ExecuteAsync(null);

        page.IsCreating.Should().BeFalse();
        page.CreateMessage.Should().Be(_localizer["Budgets.Create.NoAccounts"]);
    }

    [Fact]
    public async Task Saving_SendsWhatTheFormHolds_ClosesTheForm_AndReloads()
    {
        WithBaseCurrency("USD");
        using var page = Page();
        await page.BeginCreateCommand.ExecuteAsync(null);

        page.NewCategory = "Groceries";
        page.NewLimit = 400m;
        page.NewPeriod = page.PeriodOptions.Single(o => o.Period == BudgetPeriod.Weekly);

        await page.SaveBudgetCommand.ExecuteAsync(null);

        _sent.Should().ContainSingle().Which.Should().Be(
            new CreateBudgetCommand("Groceries", 400m, BudgetPeriod.Weekly, null, null));
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
        WithBaseCurrency("USD");
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
        WithBaseCurrency("USD");
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
        WithBaseCurrency("USD");
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
        WithBaseCurrency("USD");
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
        WithBaseCurrency("USD");
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

    // ------------------------------------------------------------------ independent review of item 3.0

    /// <summary>
    /// A budget that was saved is never reported as not saved (review finding M1).
    /// </summary>
    /// <remarks>
    /// The reload after a create sat inside the create's try, so a failing reload read "Nothing was saved"
    /// about a saved budget, emptied the list, and a second attempt was refused as a duplicate.
    /// </remarks>
    [Fact]
    public async Task ASavedBudgetWhoseReloadFails_IsReportedAsALoadFailure_NotAsUnsaved()
    {
        WithBaseCurrency("USD");
        using var page = Page();
        await page.BeginCreateCommand.ExecuteAsync(null);
        page.NewCategory = "Groceries";
        page.NewLimit = 10m;
        _mediator.Send(Arg.Any<GetBudgetProgressQuery>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("the vault locked between the write and the read"));

        await page.SaveBudgetCommand.ExecuteAsync(null);

        _sent.Should().ContainSingle();
        page.CreateMessage.Should().BeEmpty("the budget WAS created");
        page.ErrorMessage.Should().Be(_localizer["Budgets.Failure.Load"]);
        page.IsCreating.Should().BeFalse();
        page.IsEmpty.Should().BeFalse("a failed load is not an empty list");
    }

    /// <summary>The custom dates are local midnights, not UTC ones (review finding m1).</summary>
    [Fact]
    public async Task ACustomBudgetsDates_AreLocalMidnight_AtTheOffsetInForceOnThatDay()
    {
        WithBaseCurrency("USD");
        using var page = Page();
        await page.BeginCreateCommand.ExecuteAsync(null);
        page.NewCategory = "Holiday";
        page.NewLimit = 100m;
        page.NewPeriod = page.PeriodOptions.Single(o => o.Period == BudgetPeriod.Custom);
        page.NewStartDate = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
        page.NewLastDate = new DateTimeOffset(2026, 7, 31, 12, 0, 0, TimeSpan.Zero);

        await page.SaveBudgetCommand.ExecuteAsync(null);

        var sent = _sent.Should().ContainSingle().Subject;
        sent.StartDate!.Value.Offset.Should().Be(TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 3, 1)));
        sent.EndDate!.Value.Offset.Should().Be(TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 8, 1)));
    }

    /// <summary>
    /// After a load that found nothing, the page is told it is empty (review finding m2).
    /// </summary>
    /// <remarks>
    /// On the real page the view binds before the load finishes, so it only learns the page is empty from a
    /// notification raised when loading ends. The last notification must therefore read true.
    /// </remarks>
    [Fact]
    public async Task WhenALoadFindsNothing_TheLastEmptyNotification_SaysEmpty()
    {
        using var page = Page();
        var observed = new List<bool>();
        page.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BudgetsViewModel.IsEmpty))
            {
                observed.Add(page.IsEmpty);
            }
        };

        await page.InitializeAsync(CancellationToken.None);

        observed.Should().NotBeEmpty().And.EndWith(true);
    }

    /// <summary>An emptied limit box sends no stale number (review finding m4).</summary>
    [Fact]
    public async Task AnEmptyLimit_IsSentAsZero_SoTheValidatorRefusesIt()
    {
        WithBaseCurrency("USD");
        using var page = Page();
        await page.BeginCreateCommand.ExecuteAsync(null);
        page.NewCategory = "Groceries";
        page.NewLimit = 12.5m;
        page.NewLimit = null;

        await page.SaveBudgetCommand.ExecuteAsync(null);

        _sent.Should().ContainSingle().Which.Amount.Should().Be(0m);
    }

    /// <summary>A language change re-words the rows and the picker, keeping the choice (review finding m6).</summary>
    [Fact]
    public async Task ALanguageChange_ReWordsRowsAndPeriods_AndKeepsTheChosenPeriod()
    {
        WithBaseCurrency("USD");
        _mediator.Send(Arg.Any<GetBudgetProgressQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<BudgetProgress>>(new[]
            {
                new BudgetProgress(Guid.NewGuid(), "Groceries", new Money(10m, new CurrencyCode("USD")), null, null, null, 0, BudgetPeriod.Weekly),
            }));
        using var page = Page();
        await page.InitializeAsync(CancellationToken.None);
        await page.BeginCreateCommand.ExecuteAsync(null);
        page.NewPeriod = page.PeriodOptions.Single(o => o.Period == BudgetPeriod.Yearly);

        _localizer.SetLanguage("es");

        page.Budgets.Should().ContainSingle().Which.PeriodText.Should().Be("Semanal");
        page.Budgets[0].SpentAmountText.Should().Be("Desconocido");
        page.PeriodOptions.Select(o => o.Label).Should().Equal("Mensual", "Semanal", "Anual", "Fechas personalizadas");
        page.NewPeriod!.Period.Should().Be(BudgetPeriod.Yearly);
    }

    /// <summary>A disposed page stops listening to the process-lifetime localizer.</summary>
    [Fact]
    public void ADisposedPage_StopsListeningToTheLanguage()
    {
        var page = Page();
        page.Dispose();
        var notified = false;
        page.PropertyChanged += (_, _) => notified = true;

        _localizer.SetLanguage("es");

        notified.Should().BeFalse();
    }

    /// <summary>The "no budgets yet" sentence does not sit under an open form (review finding m9).</summary>
    [Fact]
    public async Task TheEmptySentence_IsHidden_WhileTheFormIsOpen()
    {
        WithBaseCurrency("USD");
        using var page = Page();
        await page.InitializeAsync(CancellationToken.None);
        page.IsEmpty.Should().BeTrue();

        await page.BeginCreateCommand.ExecuteAsync(null);

        page.IsEmpty.Should().BeFalse();
    }
}
