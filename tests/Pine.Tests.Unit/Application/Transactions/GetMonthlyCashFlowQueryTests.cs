using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Pine.Application.Transactions.Queries;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Transactions;
using Xunit;

namespace Pine.Tests.Unit.Application.Transactions;

/// <summary>
/// The dashboard's "this month" figures, measured from the ledger (post-audit plan item 1.3).
/// </summary>
/// <remarks>
/// <para>
/// WHAT THESE REPLACE. The dashboard carried an Income tile, an Expenses tile and a Savings tile that
/// rendered "No data yet" permanently -- honest, but furniture. They are replaced by one tile fed by
/// this query, and every test here pins a figure a constant would only satisfy by accident: sums that
/// add, a window that stops at the month's edge, a void that removes, a transfer that is not
/// spending, and two currencies that stay two.
/// </para>
/// <para>
/// THE MONTH IS THE UTC CALENDAR MONTH, the same window a monthly budget measures
/// (<c>Budget.ResolveWindow</c>). Two figures on one dashboard that disagreed about where September
/// starts would be worse than either choice.
/// </para>
/// </remarks>
public class GetMonthlyCashFlowQueryTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly CurrencyCode Usd = new("USD");
    private static readonly CurrencyCode Eur = new("EUR");

    private readonly ITransactionRepository _transactions = Substitute.For<ITransactionRepository>();

    private GetMonthlyCashFlowQueryHandler Handler() => new(_transactions, new FakeTimeProvider(Now));

    private void GivenLedger(params Transaction[] transactions) =>
        _transactions.GetAllAsync(Arg.Any<CancellationToken>()).Returns(transactions.AsEnumerable());

    private static Transaction Of(TransactionType type, decimal amount, DateTimeOffset when, CurrencyCode? currency = null)
    {
        var signed = type == TransactionType.Expense ? -Math.Abs(amount) : Math.Abs(amount);
        return Transaction.Create(
            Guid.NewGuid(), when, "PAYEE", new Money(signed, currency ?? Usd), type, new FakeTimeProvider(when));
    }

    private Task<MonthlyCashFlow> RunAsync() => Handler().Handle(new GetMonthlyCashFlowQuery(), CancellationToken.None);

    [Fact]
    public async Task IncomeAndSpendingAreSummedAndNetted()
    {
        GivenLedger(
            Of(TransactionType.Income, 3000m, Now.AddDays(-5)),
            Of(TransactionType.Expense, 40m, Now.AddDays(-1)),
            Of(TransactionType.Expense, 2.50m, Now.AddDays(-2)));

        var flow = await RunAsync();

        var usd = flow.ByCurrency.Should().ContainSingle().Subject;
        usd.Income.Should().Be(new Money(3000m, Usd));
        usd.Spending.Should().Be(new Money(42.50m, Usd), "spending is the positive quantity a user reads");
        usd.Net.Should().Be(new Money(2957.50m, Usd));
    }

    [Fact]
    public async Task TheWindowIsTheUtcCalendarMonth_HalfOpen()
    {
        var firstInstant = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

        GivenLedger(
            Of(TransactionType.Expense, 1m, firstInstant),                 // in: first instant of September
            Of(TransactionType.Expense, 10m, firstInstant.AddTicks(-1)),   // out: last instant of August
            Of(TransactionType.Expense, 1000m, firstInstant.AddMonths(1)), // out: first instant of October
            Of(TransactionType.Expense, 100m, Now));                       // in

        var flow = await RunAsync();

        flow.PeriodStart.Should().Be(firstInstant);
        flow.PeriodEnd.Should().Be(firstInstant.AddMonths(1));
        flow.ByCurrency.Single().Spending.Should().Be(new Money(101m, Usd));
    }

    /// <summary>
    /// The reviewer's case: 21:00 on 30 September in Ecuador is already October in UTC.
    /// </summary>
    /// <remarks>
    /// WRITTEN BECAUSE THE FIRST VERSION MEASURED THE UTC MONTH and every other test here ran in UTC,
    /// so none could see it: the tile titled "This month" showed October -- no salary, one dinner.
    /// Replacing the local window with the UTC one fails this test; so does using the offset NOW for
    /// both edges instead of the offset at each edge, in the second case below.
    /// </remarks>
    [Fact]
    public async Task TheMonthIsTheUsersLocalMonth_NotUtcs_WestOfGreenwich()
    {
        var ecuador = TimeZoneInfo.CreateCustomTimeZone("UTC-5", TimeSpan.FromHours(-5), "UTC-5", "UTC-5");
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 30, 21, 0, 0, TimeSpan.FromHours(-5)));
        clock.SetLocalTimeZone(ecuador);

        GivenLedger(
            Of(TransactionType.Income, 3000m, new DateTimeOffset(2026, 9, 15, 0, 0, 0, TimeSpan.FromHours(-5))),
            Of(TransactionType.Expense, 80m, new DateTimeOffset(2026, 9, 30, 20, 0, 0, TimeSpan.FromHours(-5))),
            Of(TransactionType.Expense, 5m, new DateTimeOffset(2026, 8, 31, 23, 0, 0, TimeSpan.FromHours(-5))));

        var flow = await new GetMonthlyCashFlowQueryHandler(_transactions, clock)
            .Handle(new GetMonthlyCashFlowQuery(), CancellationToken.None);

        flow.PeriodStart.Should().Be(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.FromHours(-5)));
        var usd = flow.ByCurrency.Single();
        usd.Income.Should().Be(new Money(3000m, Usd), "it is still September where the user is");
        usd.Spending.Should().Be(new Money(80m, Usd), "the dinner was on the 30th, and 31 August was not");
    }

    /// <summary>East of Greenwich, and across a daylight-saving change, a local date stays in its month.</summary>
    [Fact]
    public async Task ALocalMidnightDateFallsInTheMonthItNames_EastOfGreenwich_AcrossDst()
    {
        // Sydney: UTC+10 in winter, UTC+11 from the first Sunday of October. The window for October
        // starts at +10 and ends at +11 -- one offset for both edges would be an hour wrong at one end.
        var sydney = TimeZoneInfo.FindSystemTimeZoneById(OperatingSystem.IsWindows() ? "AUS Eastern Standard Time" : "Australia/Sydney");
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.FromHours(11)));
        clock.SetLocalTimeZone(sydney);

        GivenLedger(
            Of(TransactionType.Expense, 1m, new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.FromHours(10))),   // in: 1 Oct
            Of(TransactionType.Expense, 10m, new DateTimeOffset(2026, 9, 30, 23, 0, 0, TimeSpan.FromHours(10))), // out: 30 Sep
            Of(TransactionType.Expense, 100m, new DateTimeOffset(2026, 10, 31, 23, 30, 0, TimeSpan.FromHours(11))), // in: 31 Oct
            Of(TransactionType.Expense, 1000m, new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.FromHours(11)))); // out: 1 Nov

        var flow = await new GetMonthlyCashFlowQueryHandler(_transactions, clock)
            .Handle(new GetMonthlyCashFlowQuery(), CancellationToken.None);

        flow.PeriodStart.Offset.Should().Be(TimeSpan.FromHours(10));
        flow.PeriodEnd.Offset.Should().Be(TimeSpan.FromHours(11));
        flow.ByCurrency.Single().Spending.Should().Be(new Money(101m, Usd));
    }

    [Fact]
    public async Task AVoidedTransactionDoesNotCount()
    {
        var voided = Of(TransactionType.Expense, 500m, Now.AddDays(-1));
        voided.Void("entered twice", new FakeTimeProvider(Now));

        GivenLedger(voided, Of(TransactionType.Expense, 5m, Now.AddDays(-1)));

        var flow = await RunAsync();

        flow.ByCurrency.Single().Spending.Should().Be(new Money(5m, Usd));
    }

    [Fact]
    public async Task ATransferIsNeitherIncomeNorSpending()
    {
        var legs = Transaction.CreateTransferPair(
            Guid.NewGuid(), Guid.NewGuid(), Now.AddDays(-1), "From", "To", new Money(250m, Usd), new FakeTimeProvider(Now));

        GivenLedger(legs.Source, legs.Destination, Of(TransactionType.Income, 10m, Now.AddDays(-1)));

        var flow = await RunAsync();

        var usd = flow.ByCurrency.Single();
        usd.Income.Should().Be(new Money(10m, Usd), "money moved between the user's own accounts was not earned");
        usd.Spending.Should().Be(Money.Zero(Usd), "nor was it spent");
    }

    /// <summary>Each transfer leg is excluded on its own, not merely cancelled by its twin.</summary>
    /// <remarks>
    /// WRITTEN BECAUSE A MUTATION SURVIVED THE TEST ABOVE. Letting transfers into the spending bucket
    /// left it green: the source leg adds 250 and the destination leg subtracts 250, so the pair nets to
    /// zero by arithmetic rather than by rule. A lone leg -- the destination voided, or outside the
    /// window -- is where "cancels out" and "is excluded" part company.
    /// </remarks>
    [Fact]
    public async Task ALoneTransferLeg_IsStillNotSpending()
    {
        var legs = Transaction.CreateTransferPair(
            Guid.NewGuid(), Guid.NewGuid(), Now.AddDays(-1), "From", "To", new Money(250m, Usd), new FakeTimeProvider(Now));

        GivenLedger(legs.Source, Of(TransactionType.Expense, 5m, Now.AddDays(-1)));

        var flow = await RunAsync();

        flow.ByCurrency.Single().Spending.Should().Be(new Money(5m, Usd));
    }

    [Fact]
    public async Task TwoCurrenciesStayTwoFigures_NeverOneSum()
    {
        GivenLedger(
            Of(TransactionType.Income, 1000m, Now.AddDays(-1), Usd),
            Of(TransactionType.Income, 900m, Now.AddDays(-1), Eur));

        var flow = await RunAsync();

        flow.ByCurrency.Select(c => c.Currency.Value).Should().Equal("EUR", "USD");
        flow.ByCurrency.Should().OnlyContain(c => c.Income.Amount == 1000m || c.Income.Amount == 900m,
            "Pine has no exchange rates, so 1 000 USD and 900 EUR are never added into a quantity of nothing");
    }

    [Fact]
    public async Task AMonthWithNoActivityHasNoRows_RatherThanAZeroInAnInventedCurrency()
    {
        GivenLedger(Of(TransactionType.Expense, 10m, Now.AddMonths(-2)));

        var flow = await RunAsync();

        flow.ByCurrency.Should().BeEmpty();
        flow.HasActivity.Should().BeFalse();
    }
}
