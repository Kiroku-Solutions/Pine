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
    private readonly ITransactionRepository _transactions = Substitute.For<ITransactionRepository>();

    private GetBudgetProgressQueryHandler Handler() =>
        new(_budgets, _transactions, Clock);

    private void GivenBudgets(params Budget[] budgets) =>
        _budgets.GetAllAsync(Arg.Any<CancellationToken>()).Returns(budgets.AsEnumerable());

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

    private static Budget MonthlyBudgetOf(string category, decimal limit) =>
        Budget.Create(category, new Money(limit, Eur), BudgetPeriod.Monthly);

    [Fact]
    public async Task SpendIsSummedFromTheTransactionsInThePeriod()
    {
        GivenBudgets(MonthlyBudgetOf("Groceries", 500m));
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
        GivenBudgets(MonthlyBudgetOf("Groceries", 500m));
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
        GivenBudgets(MonthlyBudgetOf("Groceries", 500m));
        GivenLedger(
            Expense(10m, "Groceries", new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero)),
            Expense(20m, "Groceries", new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero)));

        var progress = await Handler().Handle(new GetBudgetProgressQuery(), CancellationToken.None);

        progress[0].Spent!.Amount.Should().Be(10m);
    }

    [Fact]
    public async Task AVoidedTransactionIsNotCounted()
    {
        var voided = Expense(100m, "Groceries", Now.AddDays(-2));
        voided.Void("entered twice", Clock);

        GivenBudgets(MonthlyBudgetOf("Groceries", 500m));
        GivenLedger(Expense(15m, "Groceries", Now.AddDays(-1)), voided);

        var progress = await Handler().Handle(new GetBudgetProgressQuery(), CancellationToken.None);

        progress[0].Spent!.Amount.Should().Be(15m,
            "a voided transaction no longer counts towards a balance, and it does not count towards a budget either");
    }

    [Fact]
    public async Task SpendingPastTheLimitMarksTheBudgetExceeded()
    {
        GivenBudgets(MonthlyBudgetOf("Groceries", 50m));
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
        GivenBudgets(MonthlyBudgetOf("Groceries", 50m));
        GivenLedger(Expense(49.99m, "Groceries", Now.AddDays(-1)));

        var progress = await Handler().Handle(new GetBudgetProgressQuery(), CancellationToken.None);

        progress[0].IsExceeded.Should().BeFalse();
    }

    [Fact]
    public async Task ADifferentCategorysSpendingIsNotCounted()
    {
        GivenBudgets(MonthlyBudgetOf("Groceries", 500m));
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
        var refund = Transaction.Create(
            Guid.NewGuid(), Now.AddDays(-1), "SHOP", new Money(25m, Eur), TransactionType.Income, Clock);
        refund.ReplaceSplits(new[] { new SplitDefinition(new Money(25m, Eur), "Groceries") });

        GivenBudgets(MonthlyBudgetOf("Groceries", 500m));
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
        GivenBudgets(MonthlyBudgetOf("Groceries", 500m));
        GivenLedger(
            Expense(10m, "  Groceries  ", Now.AddDays(-1)),
            Expense(90m, "groceries", Now.AddDays(-1)));

        var progress = await Handler().Handle(new GetBudgetProgressQuery(), CancellationToken.None);

        progress[0].Spent!.Amount.Should().Be(10m,
            "surrounding space is not a category, but a different capitalisation may be one the user chose");
    }

    /// <summary>
    /// The row carries the category the budget was created against.
    /// </summary>
    [Fact]
    public async Task TheCategoryOnTheRowIsTheOneTheBudgetWasCreatedAgainst()
    {
        GivenBudgets(MonthlyBudgetOf("Groceries", 500m));
        GivenLedger();

        var progress = await Handler().Handle(new GetBudgetProgressQuery(), CancellationToken.None);

        progress[0].Category.Should().Be("Groceries");
    }

    /// <summary>
    /// The spend a budget reports is measured, whoever created the budget (TD-132, plan item 2.3).
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS TEST REPLACES ITS OWN OPPOSITE. What stood here was
    /// <c>ABudgetWhoseCategoryDoesNotExistReportsAnUnknownSpendRatherThanZero</c>, and it was right
    /// about the smaller thing — an unknown spend must not be rendered as a zero — while pinning a
    /// state that was universal: <c>Budget</c> held a <c>CategoryId</c> into a table nothing ever
    /// wrote to, so EVERY budget a user could create reported "Unknown category" and no spend at
    /// all. The screen was honest and empty.
    /// </para>
    /// <para>
    /// With the label as the model there is no unresolvable category left to test. What is worth
    /// pinning instead is the consequence: a budget created the way a user would create one now
    /// measures the ledger. The unknown-spend path still exists and still matters — see
    /// <see cref="ABiWeeklyBudgetWithNoStoredDatesReportsAnUnknownSpend"/>, which is now the only
    /// way to reach it.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ABudgetCreatedTheWayAUserWouldCreateOneMeasuresTheLedger()
    {
        GivenBudgets(Budget.Create("Groceries", new Money(500m, Eur), BudgetPeriod.Monthly));
        GivenLedger(Expense(40m, "Groceries", Now.AddDays(-1)));

        var progress = await Handler().Handle(new GetBudgetProgressQuery(), CancellationToken.None);

        progress[0].IsSpendKnown.Should().BeTrue(
            "before TD-132 was decided this was false for every budget in existence");
        progress[0].Spent!.Amount.Should().Be(40m);
        progress[0].Category.Should().Be("Groceries");
    }

    /// <summary>
    /// A budget cannot be created without naming a category.
    /// </summary>
    /// <remarks>
    /// The old <c>CategoryId</c> had no such refusal: <c>Guid.Empty</c> was accepted and produced a
    /// budget that could never be measured. A budget that names nothing measures nothing, and it is
    /// cheaper to refuse it than to render it forever as a limit against an unknown.
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ABudgetMustNameACategory(string category)
    {
        var create = () => Budget.Create(category, new Money(500m, Eur), BudgetPeriod.Monthly);

        create.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// A budget read back from a vault with a blank label reports an unknown spend, not a spend
    /// against "no category".
    /// </summary>
    /// <remarks>
    /// EF materialises a budget without running its constructor, so the aggregate's refusal of a
    /// blank label does not protect a row read from disk. The independent review of plan item 2.3
    /// measured such a budget counting an uncategorised split as its spend. The row is built here
    /// the way EF builds it — by setting the property, not by calling <c>Create</c>.
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ABudgetWithABlankLabelReadFromAVaultReportsAnUnknownSpend(string label)
    {
        var budget = Budget.Create("placeholder", new Money(500m, Eur), BudgetPeriod.Monthly);
        typeof(Budget).GetProperty(nameof(Budget.Category))!.SetValue(budget, label);

        GivenBudgets(budget);
        GivenLedger(Expense(40m, label, Now.AddDays(-1)));

        var progress = await Handler().Handle(new GetBudgetProgressQuery(), CancellationToken.None);

        progress[0].IsSpendKnown.Should().BeFalse(
            "a blank label is no category, and counting the uncategorised splits against it would put "
            + "a confident wrong figure on the screen");
    }

    /// <summary>
    /// A label read back from a vault with surrounding whitespace is compared trimmed anyway.
    /// </summary>
    [Fact]
    public async Task ALabelReadFromAVaultWithWhitespaceIsStillMatched()
    {
        var budget = Budget.Create("placeholder", new Money(500m, Eur), BudgetPeriod.Monthly);
        typeof(Budget).GetProperty(nameof(Budget.Category))!.SetValue(budget, "  Groceries  ");

        GivenBudgets(budget);
        GivenLedger(Expense(40m, "Groceries", Now.AddDays(-1)));

        var progress = await Handler().Handle(new GetBudgetProgressQuery(), CancellationToken.None);

        progress[0].Spent!.Amount.Should().Be(40m,
            "the review measured this exact row reporting a known spend of 0");
    }

    /// <summary>
    /// The label is stored trimmed, so that every comparison against it agrees.
    /// </summary>
    [Fact]
    public void TheCategoryLabelIsStoredTrimmed()
    {
        Budget.Create("  Groceries  ", new Money(500m, Eur), BudgetPeriod.Monthly)
            .Category.Should().Be("Groceries");
    }

    /// <summary>
    /// A fortnightly budget has no calendar anchor, so its window — and therefore its spend — is
    /// unknown until someone stores explicit dates for it.
    /// </summary>
    [Fact]
    public async Task ABiWeeklyBudgetWithNoStoredDatesReportsAnUnknownSpend()
    {
        _budgets.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new[]
        {
            Budget.Create("Groceries", new Money(100m, Eur), BudgetPeriod.BiWeekly),
        }.AsEnumerable());
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
        var start = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);
        _budgets.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new[]
        {
            Budget.Create("Groceries", new Money(500m, Eur), BudgetPeriod.Custom, start, end),
        }.AsEnumerable());
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
        GivenBudgets(MonthlyBudgetOf("Groceries", 500m));
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
        GivenBudgets(MonthlyBudgetOf("Groceries", 500m));
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
