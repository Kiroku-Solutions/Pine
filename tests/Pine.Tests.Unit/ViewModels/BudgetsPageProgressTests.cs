using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MediatR;
using NSubstitute;
using Pine.Application.Budgets.Queries;
using Pine.Core.Common.ValueObjects;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.Services;
using Pine.Ui.Avalonia.ViewModels;
using Xunit;

namespace Pine.Tests.Unit.ViewModels;

/// <summary>
/// The budgets page renders measured figures, and says so when there are none (TD-99).
/// </summary>
/// <remarks>
/// <para>
/// The page used to build every row itself: <c>SpentAmount = 0m</c> with a comment promising a
/// calculation, and <c>CategoryName = "Category " + b.CategoryId.ToString()[..4]</c> — four hex
/// digits of a GUID where a category name goes. These tests pin the two halves of the fix: the row
/// now shows what the query measured, and an unmeasurable budget reads as unknown rather than as
/// nothing spent.
/// </para>
/// </remarks>
public class BudgetsPageProgressTests
{
    private static readonly CurrencyCode Eur = new("EUR");

    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly Localizer _localizer = new();

    private BudgetsViewModel PageShowing(params BudgetProgress[] rows)
    {
        _mediator.Send(Arg.Any<GetBudgetProgressQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<BudgetProgress>>(rows));

        return new BudgetsViewModel(_mediator, _localizer);
    }

    private static BudgetProgress Measured(string category, decimal limit, decimal spent) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            category,
            "#00AA00",
            new Money(limit, Eur),
            new Money(spent, Eur),
            new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
            0);

    private static BudgetProgress Unmeasurable(decimal limit) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            CategoryName: null,
            CategoryColorHex: null,
            new Money(limit, Eur),
            Spent: null,
            PeriodStart: null,
            PeriodEnd: null,
            UncountedSplitCount: 0);

    [Fact]
    public async Task ARowShowsTheMeasuredSpendAndTheRealCategoryName()
    {
        var page = PageShowing(Measured("Groceries", limit: 500m, spent: 120.25m));

        await page.InitializeAsync(CancellationToken.None);

        var row = page.Budgets.Should().ContainSingle().Subject;
        row.CategoryName.Should().Be("Groceries", "the user's category, not four hex digits of its id");
        row.CategoryColorHex.Should().Be("#00AA00");
        row.SpentAmount.Should().Be(120.25m);
        row.LimitAmount.Should().Be(500m);
        row.CurrencyCode.Should().Be("EUR");
        row.IsSpendKnown.Should().BeTrue();
    }

    [Fact]
    public async Task TheProgressBarMovesWithTheMeasuredSpend()
    {
        var page = PageShowing(Measured("Groceries", limit: 200m, spent: 50m));

        await page.InitializeAsync(CancellationToken.None);

        page.Budgets[0].ProgressPercentage.Should().Be(25d,
            "every bar on this page used to read 0% because the spend was a literal zero");
    }

    [Fact]
    public async Task ARowOverItsLimitIsMarkedExceeded()
    {
        var page = PageShowing(Measured("Groceries", limit: 100m, spent: 140m));

        await page.InitializeAsync(CancellationToken.None);

        page.Budgets[0].IsExceeded.Should().BeTrue(
            "the over-budget warning in BudgetsView is bound to this, and with a fabricated zero "
            + "spend it could never be shown");
    }

    [Fact]
    public async Task ARowUnderItsLimitIsNotMarkedExceeded()
    {
        var page = PageShowing(Measured("Groceries", limit: 100m, spent: 99.99m));

        await page.InitializeAsync(CancellationToken.None);

        page.Budgets[0].IsExceeded.Should().BeFalse();
    }

    /// <summary>
    /// An unmeasurable budget must not render as "nothing spent".
    /// </summary>
    [Fact]
    public async Task AnUnknownSpendIsSaidToBeUnknownRatherThanShownAsZero()
    {
        var page = PageShowing(Unmeasurable(limit: 300m));

        await page.InitializeAsync(CancellationToken.None);

        var row = page.Budgets.Should().ContainSingle().Subject;
        row.IsSpendKnown.Should().BeFalse();
        row.SpentAmountText.Should().Be(_localizer["Budgets.Progress.SpendUnknown"]);
        row.SpentAmountText.Should().NotBe("0.00", "a zero here is a claim about the user's money");
        row.ProgressPercentage.Should().Be(0d);
        row.IsExceeded.Should().BeFalse("an unknown spend is not an exceeded one");
        row.CategoryName.Should().Be(_localizer["Budgets.Category.Unresolved"],
            "there is no name to show, and the old code invented one from the identifier");
    }

    [Fact]
    public async Task NoBudgetsProducesNoRows()
    {
        var page = PageShowing();

        await page.InitializeAsync(CancellationToken.None);

        page.Budgets.Should().BeEmpty();
        page.IsLoading.Should().BeFalse();
    }
}
