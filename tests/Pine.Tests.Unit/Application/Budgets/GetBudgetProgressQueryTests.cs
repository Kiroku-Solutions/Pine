using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Pine.Application.Budgets.Queries;
using Pine.Core.Budgets;
using Pine.Core.Categories;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Transactions;
using Xunit;

namespace Pine.Tests.Unit.Application.Budgets;

/// <summary>
/// The budgets screen's numbers, measured against the ledger (TD-99).
/// </summary>
/// <remarks>
/// <para>
/// WHAT THESE TESTS EXIST TO STOP. <c>BudgetsViewModel</c> set <c>SpentAmount = 0m</c> for every
/// budget, behind a comment promising it would one day be calculated. Nothing failed: the page
/// loaded, every bar rendered, every budget was comfortably under its limit and the over-budget
/// warning in the view was markup no user could reach. A screen that always says "you have spent
/// nothing" is not an empty state — it is a claim about the user's money that nothing checked.
/// </para>
/// <para>
/// Each test below therefore pins a figure that a zero would also have satisfied only by accident:
/// spend that is summed, spend that stops at the window's edge, spend that a void removes, and the
/// exceeded flag that a real number can finally raise.
/// </para>
/// </remarks>
public class GetBudgetProgressQueryTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly FakeTimeProvider Clock = new(Now);
    private static readonly CurrencyCode Eur = new("EUR");

    private readonly IBudgetRepository _budgets = Substitute.For<IBudgetRepository>();
    private readonly ICategoryRepository _categories = Substitute.For<ICategoryRepository>();
    private readonly ITransactionRepository _transactions = Substitute.For<ITransactionRepository>();

    private GetBudgetProgressQueryHandler Handler() =>
        new(_budgets, _categories, _transactions, Clock);

    private void GivenBudgets(params Budget[] budgets) =>
        _budgets.GetAllAsync(Arg.Any<CancellationToken>()).Returns(budgets.AsEnumerable());

    private void GivenCategories(params Category[] categories) =>
        _categories.GetAllAsync(Arg.Any<CancellationToken>()).Returns(categories.AsEnumerable());

    private void GivenLedger(params Transaction[] transactions) =>
        _transactions.GetAllAsync(Arg.Any<CancellationToken>()).Returns(transactions.AsEnumerable());

    /// <summary>
    /// An expense, whose single split carries <paramref name="category"/>.
    /// </summary>
    /// <remarks>
    /// It is built against a clock standing at its own date, not at <see cref="Now"/>, because
    /// <c>Transaction.Create</c> rejects a date more than a day ahead of the clock it is given — and
    /// one of these tests deliberately places a transaction in the period AFTER the one being
    /// measured. The clock the handler reads is still <see cref="Clock"/>.
    /// </remarks>
    private static Transaction Expense(decimal amount, string category, DateTimeOffset when, CurrencyCode? currency = null)
    {
        var money = new Money(-Math.Abs(amount), currency ?? Eur);
        var transaction = Transaction.Create(
            Guid.NewGuid(), when, "SHOP", money, TransactionType.Expense, new FakeTimeProvider(when));
        transaction.ReplaceSplits(new[] { new SplitDefinition(money, category) });
        return transaction;
    }

    private static Budget MonthlyBudgetOf(Guid categoryId, decimal limit) =>
        Budget.Create(categoryId, new Money(limit, Eur), BudgetPeriod.Monthly);

    private static Category CategoryNamed(string name) => Category.Create(name, "#123456");

    [Fact]
    public async Task SpendIsSummedFromTheTransactionsInThePeriod()
    {
        var category = CategoryNamed("Groceries");
        GivenBudgets(MonthlyBudgetOf(category.Id, 500m));
        GivenCategories(category);
        GivenLedger(
            Expense(40m, "Groceries", Now.AddDays(-1)),
            Expense(2.50m, "Groceries", Now.AddDays(-3)));

        var progress = await Handler().Handle(new GetBudgetProgressQuery(), CancellationToken.None);

        progress.Should().ContainSingle();
        progress[0].Spent!.Amount.Should().Be(42.50m,
            "the figure has to come from the ledger. A hardcoded 0 passed every check this screen had");
        progress[0].Spent!.Currency.Value.Should().Be("EUR");
        progress[0].IsSpendKnown.Should().BeTrue();
    }

    [Fact]
    public async Task ATransactionOutsideThePeriodIsNotCounted()
    {
        var category = CategoryNamed("Groceries");
        GivenBudgets(MonthlyBudgetOf(category.Id, 500m));
        GivenCategories(category);
        GivenLedger(
            Expense(40m, "Groceries", Now.AddDays(-1)),
            Expense(999m, "Groceries", new DateTimeOffset(2026, 4, 30, 23, 0, 0, TimeSpan.Zero)));

        var progress = await Handler().Handle(new GetBudgetProgressQuery(), CancellationToken.None);

        progress[0].Spent!.Amount.Should().Be(40m,
            "last month's spending is not this month's budget");
    }

    /// <summary>
    /// The window is half-open, so a transaction at the first instant of the next period belongs to
    /// the next period. Stated separately because an off-by-one here moves real money between two
    /// budgets and both totals still look plausible.
    /// </summary>
    [Fact]
    public async Task TheWindowIncludesItsFirstInstantAndExcludesItsLast()
    {
        var category = CategoryNamed("Groceries");
        GivenBudgets(MonthlyBudgetOf(category.Id, 500m));
        GivenCategories(category);
        GivenLedger(
            Expense(10m, "Groceries", new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero)),
            Expense(20m, "Groceries", new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero)));

        var progress = await Handler().Handle(new GetBudgetProgressQuery(), CancellationToken.None);

        progress[0].Spent!.Amount.Should().Be(10m);
    }

    [Fact]
    public async Task AVoidedTransactionIsNotCounted()
    {
        var category = CategoryNamed("Groceries");
        var voided = Expense(100m, "Groceries", Now.AddDays(-2));
        voided.Void("entered twice", Clock);

        GivenBudgets(MonthlyBudgetOf(category.Id, 500m));
        GivenCategories(category);
        GivenLedger(Expense(15m, "Groceries", Now.AddDays(-1)), voided);

        var progress = await Handler().Handle(new GetBudgetProgressQuery(), CancellationToken.None);

        progress[0].Spent!.Amount.Should().Be(15m,
            "a voided transaction no longer counts towards a balance, and it does not count towards a budget either");
    }

    [Fact]
    public async Task SpendingPastTheLimitMarksTheBudgetExceeded()
    {
        var category = CategoryNamed("Groceries");
        GivenBudgets(MonthlyBudgetOf(category.Id, 50m));
        GivenCategories(category);
        GivenLedger(Expense(60m, "Groceries", Now.AddDays(-1)));

        var progress = await Handler().Handle(new GetBudgetProgressQuery(), CancellationToken.None);

        progress[0].Spent!.Amount.Should().Be(60m);
        progress[0].IsExceeded.Should().BeTrue(
            "with a fabricated zero spend this flag could never be true, so the over-budget warning "
            + "in BudgetsView was unreachable markup");
    }

    [Fact]
    public async Task SpendingUnderTheLimitDoesNotMarkTheBudgetExceeded()
    {
        var category = CategoryNamed("Groceries");
        GivenBudgets(MonthlyBudgetOf(category.Id, 50m));
        GivenCategories(category);
        GivenLedger(Expense(49.99m, "Groceries", Now.AddDays(-1)));

        var progress = await Handler().Handle(new GetBudgetProgressQuery(), CancellationToken.None);

        progress[0].IsExceeded.Should().BeFalse();
    }

    [Fact]
    public async Task ADifferentCategorysSpendingIsNotCounted()
    {
        var category = CategoryNamed("Groceries");
        GivenBudgets(MonthlyBudgetOf(category.Id, 500m));
        GivenCategories(category, CategoryNamed("Transport"));
        GivenLedger(
            Expense(30m, "Groceries", Now.AddDays(-1)),
            Expense(70m, "Transport", Now.AddDays(-1)));

        var progress = await Handler().Handle(new GetBudgetProgressQuery(), CancellationToken.None);

        progress[0].Spent!.Amount.Should().Be(30m);
    }

    /// <summary>
    /// Income is excluded, and this is the choice with the most visible consequence — a refund does
    /// not reduce the spend shown. Pinned so it is a decision rather than an accident.
    /// </summary>
    [Fact]
    public async Task IncomeAgainstTheSameCategoryDoesNotReduceTheSpend()
    {
        var category = CategoryNamed("Groceries");
        var refund = Transaction.Create(
            Guid.NewGuid(), Now.AddDays(-1), "SHOP", new Money(25m, Eur), TransactionType.Income, Clock);
        refund.ReplaceSplits(new[] { new SplitDefinition(new Money(25m, Eur), "Groceries") });

        GivenBudgets(MonthlyBudgetOf(category.Id, 500m));
        GivenCategories(category);
        GivenLedger(Expense(40m, "Groceries", Now.AddDays(-1)), refund);

        var progress = await Handler().Handle(new GetBudgetProgressQuery(), CancellationToken.None);

        progress[0].Spent!.Amount.Should().Be(40m,
            "nothing in the model distinguishes a refund from a salary, and netting a salary off a "
            + "grocery budget would be a fabrication of a different kind");
    }

    /// <summary>
    /// The join is by NAME (TD-32/TD-132), trimmed and compared ordinally, exactly as
    /// <c>CategorisationTrainingData</c> compares it. Two rules for "the same category" would mean
    /// the categoriser and this screen grouping the same spending differently.
    /// </summary>
    [Fact]
    public async Task CategoryNamesAreMatchedTrimmedAndCaseSensitively()
    {
        var category = CategoryNamed("Groceries");
        GivenBudgets(MonthlyBudgetOf(category.Id, 500m));
        GivenCategories(category);
        GivenLedger(
            Expense(10m, "  Groceries  ", Now.AddDays(-1)),
            Expense(90m, "groceries", Now.AddDays(-1)));

        var progress = await Handler().Handle(new GetBudgetProgressQuery(), CancellationToken.None);

        progress[0].Spent!.Amount.Should().Be(10m,
            "surrounding space is not a category, but a different capitalisation may be one the user chose");
    }

    /// <summary>
    /// The row carries the real category, not four hex digits of its identifier.
    /// </summary>
    [Fact]
    public async Task TheCategorysRealNameAndColourAreReturned()
    {
        var category = Category.Create("Groceries", "#00AA00");
        GivenBudgets(MonthlyBudgetOf(category.Id, 500m));
        GivenCategories(category);
        GivenLedger();

        var progress = await Handler().Handle(new GetBudgetProgressQuery(), CancellationToken.None);

        progress[0].CategoryName.Should().Be("Groceries");
        progress[0].CategoryColorHex.Should().Be("#00AA00");
    }

    /// <summary>
    /// A budget pointing at a category that does not exist reports an UNKNOWN spend, not a zero.
    /// </summary>
    /// <remarks>
    /// This is the honest end of the join TD-132 leaves open: <c>Budget</c> holds a
    /// <c>CategoryId</c>, a split holds a name, and nothing in the shipping application writes a
    /// <c>Categories</c> row. Where there is no name to compare against there is no spend to
    /// report, and saying so is the difference between this fix and the defect it replaces.
    /// </remarks>
    [Fact]
    public async Task ABudgetWhoseCategoryDoesNotExistReportsAnUnknownSpendRatherThanZero()
    {
        GivenBudgets(MonthlyBudgetOf(Guid.NewGuid(), 500m));
        GivenCategories();
        GivenLedger(Expense(40m, "Groceries", Now.AddDays(-1)));

        var progress = await Handler().Handle(new GetBudgetProgressQuery(), CancellationToken.None);

        progress[0].Spent.Should().BeNull();
        progress[0].IsSpendKnown.Should().BeFalse();
        progress[0].CategoryName.Should().BeNull("there is no name to show, and inventing one is the bug");
        progress[0].IsExceeded.Should().BeFalse();
    }

    /// <summary>
    /// A fortnightly budget has no calendar anchor, so its window — and therefore its spend — is
    /// unknown until someone stores explicit dates for it.
    /// </summary>
    [Fact]
    public async Task ABiWeeklyBudgetWithNoStoredDatesReportsAnUnknownSpend()
    {
        var category = CategoryNamed("Groceries");
        _budgets.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new[]
        {
            Budget.Create(category.Id, new Money(100m, Eur), BudgetPeriod.BiWeekly),
        }.AsEnumerable());
        GivenCategories(category);
        GivenLedger(Expense(40m, "Groceries", Now.AddDays(-1)));

        var progress = await Handler().Handle(new GetBudgetProgressQuery(), CancellationToken.None);

        progress[0].Spent.Should().BeNull(
            "\"this fortnight\" means nothing without an origin, and picking one would put an "
            + "invented number on the screen");
        progress[0].PeriodStart.Should().BeNull();
    }

    /// <summary>
    /// A budget carrying explicit dates is measured over exactly those dates.
    /// </summary>
    [Fact]
    public async Task AnExplicitWindowIsUsedInsteadOfTheCalendarPeriod()
    {
        var category = CategoryNamed("Groceries");
        var start = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);
        _budgets.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new[]
        {
            Budget.Create(category.Id, new Money(500m, Eur), BudgetPeriod.Custom, start, end),
        }.AsEnumerable());
        GivenCategories(category);
        GivenLedger(
            Expense(11m, "Groceries", new DateTimeOffset(2026, 3, 15, 9, 0, 0, TimeSpan.Zero)),
            Expense(22m, "Groceries", Now.AddDays(-1)));

        var progress = await Handler().Handle(new GetBudgetProgressQuery(), CancellationToken.None);

        progress[0].Spent!.Amount.Should().Be(11m);
        progress[0].PeriodStart.Should().Be(start);
        progress[0].PeriodEnd.Should().Be(end);
    }

    /// <summary>
    /// A split in another currency cannot be counted against the limit, and is reported rather than
    /// silently dropped. Pine has no exchange rates; adding it would be the dashboard's old
    /// "1900 EUR from 1000 USD and 900 EUR" defect in a new place.
    /// </summary>
    [Fact]
    public async Task ASplitInAnotherCurrencyIsExcludedAndCounted()
    {
        var category = CategoryNamed("Groceries");
        GivenBudgets(MonthlyBudgetOf(category.Id, 500m));
        GivenCategories(category);
        GivenLedger(
            Expense(30m, "Groceries", Now.AddDays(-1)),
            Expense(70m, "Groceries", Now.AddDays(-1), new CurrencyCode("USD")));

        var progress = await Handler().Handle(new GetBudgetProgressQuery(), CancellationToken.None);

        progress[0].Spent!.Amount.Should().Be(30m);
        progress[0].Spent!.Currency.Value.Should().Be("EUR");
        progress[0].UncountedSplitCount.Should().Be(1,
            "an exclusion nobody can see is indistinguishable from an arithmetic error");
    }

    [Fact]
    public async Task ABudgetWithNoMatchingSpendingReportsZeroAndKnowsIt()
    {
        var category = CategoryNamed("Groceries");
        GivenBudgets(MonthlyBudgetOf(category.Id, 500m));
        GivenCategories(category);
        GivenLedger(Expense(70m, "Transport", Now.AddDays(-1)));

        var progress = await Handler().Handle(new GetBudgetProgressQuery(), CancellationToken.None);

        progress[0].Spent!.Amount.Should().Be(0m);
        progress[0].IsSpendKnown.Should().BeTrue(
            "a measured zero is a fact about the ledger; the defect was the UNMEASURED one");
    }

    [Fact]
    public async Task NoBudgetsMeansNoRowsAndNoLedgerRead()
    {
        GivenBudgets();

        var progress = await Handler().Handle(new GetBudgetProgressQuery(), CancellationToken.None);

        progress.Should().BeEmpty();
        await _transactions.DidNotReceive().GetAllAsync(Arg.Any<CancellationToken>());
    }
}
