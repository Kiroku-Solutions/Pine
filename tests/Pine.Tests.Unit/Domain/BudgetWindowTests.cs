using System;
using FluentAssertions;
using Pine.Core.Budgets;
using Pine.Core.Common.ValueObjects;
using Xunit;

namespace Pine.Tests.Unit.Domain;

/// <summary>
/// Which transactions a budget is measured over (TD-99).
/// </summary>
/// <remarks>
/// The budgets screen showed a spend of zero for every budget, and the first thing a real figure
/// needs is a window. That question belongs to the budget — it is the only place that holds a
/// period AND a pair of optional dates — so it is answered by <see cref="Budget.ResolveWindow"/>
/// and pinned here rather than inside the query that consumes it.
/// </remarks>
public class BudgetWindowTests
{
    private static readonly CurrencyCode Eur = new("EUR");

    private static Budget BudgetOf(BudgetPeriod period, DateTimeOffset? start = null, DateTimeOffset? end = null) =>
        Budget.Create("Groceries", new Money(100m, Eur), period, start, end);

    private static readonly DateTimeOffset Wednesday20May2026 = new(2026, 5, 20, 15, 30, 0, TimeSpan.Zero);

    [Fact]
    public void AMonthlyBudgetIsMeasuredOverTheCalendarMonth()
    {
        var window = BudgetOf(BudgetPeriod.Monthly).ResolveWindow(Wednesday20May2026, TimeZoneInfo.Utc);

        window.Should().NotBeNull();
        window!.Value.Start.Should().Be(new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero));
        window.Value.End.Should().Be(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void AYearlyBudgetIsMeasuredOverTheCalendarYear()
    {
        var window = BudgetOf(BudgetPeriod.Yearly).ResolveWindow(Wednesday20May2026, TimeZoneInfo.Utc);

        window!.Value.Start.Should().Be(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        window.Value.End.Should().Be(new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void AWeeklyBudgetStartsOnTheMondayOfTheCurrentWeek()
    {
        var window = BudgetOf(BudgetPeriod.Weekly).ResolveWindow(Wednesday20May2026, TimeZoneInfo.Utc);

        window!.Value.Start.Should().Be(new DateTimeOffset(2026, 5, 18, 0, 0, 0, TimeSpan.Zero));
        window.Value.End.Should().Be(new DateTimeOffset(2026, 5, 25, 0, 0, 0, TimeSpan.Zero));
    }

    /// <summary>
    /// Sunday is the END of the week here, not the start of the next one. The rotation that makes
    /// that true is the easiest line in the file to get wrong, and getting it wrong moves a whole
    /// day of spending into the wrong budget while both totals still look plausible.
    /// </summary>
    [Fact]
    public void AWeeklyBudgetTreatsSundayAsTheLastDayOfTheWeek()
    {
        var sunday = new DateTimeOffset(2026, 5, 24, 23, 0, 0, TimeSpan.Zero);

        var window = BudgetOf(BudgetPeriod.Weekly).ResolveWindow(sunday, TimeZoneInfo.Utc);

        window!.Value.Start.Should().Be(new DateTimeOffset(2026, 5, 18, 0, 0, 0, TimeSpan.Zero));
    }

    /// <summary>
    /// A fortnight has no calendar anchor and this aggregate stores no origin, so the honest answer
    /// is that the window is unknown.
    /// </summary>
    [Fact]
    public void ABiWeeklyBudgetWithNoStoredDatesHasNoWindow()
    {
        BudgetOf(BudgetPeriod.BiWeekly).ResolveWindow(Wednesday20May2026, TimeZoneInfo.Utc).Should().BeNull(
            "any anchor chosen here would be invented by the code and then shown to the user as "
            + "their budget");
    }

    [Fact]
    public void StoredDatesWinOverTheCalendarPeriod()
    {
        var start = new DateTimeOffset(2026, 2, 10, 0, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 2, 24, 0, 0, 0, TimeSpan.Zero);

        var window = BudgetOf(BudgetPeriod.BiWeekly, start, end).ResolveWindow(Wednesday20May2026, TimeZoneInfo.Utc);

        window!.Value.Start.Should().Be(start);
        window.Value.End.Should().Be(end);
    }

    /// <summary>
    /// Half a window is not a window: the constructor permits a start with no end for every period
    /// but <c>Custom</c>, and guessing the missing end is the class of invention TD-99 removes.
    /// </summary>
    [Fact]
    public void AStartWithNoEndFallsBackToTheCalendarPeriod()
    {
        var window = BudgetOf(BudgetPeriod.Monthly, new DateTimeOffset(2026, 2, 10, 0, 0, 0, TimeSpan.Zero))
            .ResolveWindow(Wednesday20May2026, TimeZoneInfo.Utc);

        window!.Value.Start.Should().Be(new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero));
    }

    /// <summary>
    /// The month is the user's, not UTC's (TD-149). The case the independent review of plan item 3.0
    /// reproduced: 22:00 on 30 September in Ecuador is already October in UTC.
    /// </summary>
    [Fact]
    public void AMonthlyBudget_IsTheUsersCalendarMonth_NotUtcs()
    {
        var ecuador = TimeZoneInfo.CreateCustomTimeZone("Test/Ecuador", TimeSpan.FromHours(-5), "Ecuador", "Ecuador");
        var eveningOf30September = new DateTimeOffset(2026, 9, 30, 22, 0, 0, TimeSpan.FromHours(-5));

        var window = BudgetOf(BudgetPeriod.Monthly).ResolveWindow(eveningOf30September, ecuador)!.Value;

        window.Start.Should().Be(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.FromHours(-5)));
        window.End.Should().Be(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.FromHours(-5)));
        eveningOf30September.Should().BeOnOrAfter(window.Start).And.BeBefore(window.End,
            "that evening is still September where the user lives");
    }

    /// <summary>
    /// Each edge at the offset in force at that edge: March in Madrid starts at +01:00 and ends at +02:00.
    /// </summary>
    [Fact]
    public void AWindowAcrossADaylightSavingChange_UsesEachEdgesOwnOffset()
    {
        var madrid = TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid");
        var midMarch = new DateTimeOffset(2026, 3, 15, 12, 0, 0, TimeSpan.FromHours(1));

        var window = BudgetOf(BudgetPeriod.Monthly).ResolveWindow(midMarch, madrid)!.Value;

        window.Start.Should().Be(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.FromHours(1)));
        window.Start.Offset.Should().Be(TimeSpan.FromHours(1));
        window.End.Should().Be(new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.FromHours(2)));
        window.End.Offset.Should().Be(TimeSpan.FromHours(2));
    }

    /// <summary>A week and a year are the user's too, not only a month.</summary>
    [Fact]
    public void AWeekAndAYear_AreLocalToo()
    {
        var tokyo = TimeZoneInfo.CreateCustomTimeZone("Test/Tokyo", TimeSpan.FromHours(9), "Tokyo", "Tokyo");
        // 23:30 UTC on Thursday 31 December 2026 is 08:30 on Friday 1 January 2027 in Tokyo.
        var newYearsMorningInTokyo = new DateTimeOffset(2026, 12, 31, 23, 30, 0, TimeSpan.Zero);

        BudgetOf(BudgetPeriod.Yearly).ResolveWindow(newYearsMorningInTokyo, tokyo)!.Value.Start
            .Should().Be(new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.FromHours(9)));
        BudgetOf(BudgetPeriod.Weekly).ResolveWindow(newYearsMorningInTokyo, tokyo)!.Value.Start
            .Should().Be(new DateTimeOffset(2026, 12, 28, 0, 0, 0, TimeSpan.FromHours(9)), "Friday 1 January 2027's ISO week starts on Monday 28 December");
    }
}
