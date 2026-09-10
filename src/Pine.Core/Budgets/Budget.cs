using System;
using Pine.Core.Common;
using Pine.Core.Common.ValueObjects;

namespace Pine.Core.Budgets;

public class Budget
{
    public Guid Id { get; private set; }
    public Guid CategoryId { get; private set; }
    public Money Limit { get; private set; }
    public BudgetPeriod Period { get; private set; }
    
    // For Custom periods, StartDate and EndDate define the exact bounds.
    // For Monthly, Weekly, etc., they can be null and the bounds are dynamically calculated based on the current date,
    // OR we can explicitly store them for a specific historical budget period.
    public DateTimeOffset? StartDate { get; private set; }
    public DateTimeOffset? EndDate { get; private set; }

    private Budget() { Limit = null!; } // EF Core

    private Budget(Guid id, Guid categoryId, Money limit, BudgetPeriod period, DateTimeOffset? startDate, DateTimeOffset? endDate)
    {
        Id = id;
        CategoryId = categoryId;
        Limit = limit ?? throw new ArgumentNullException(nameof(limit));
        Period = period;
        
        if (period == BudgetPeriod.Custom && (!startDate.HasValue || !endDate.HasValue))
            throw new ArgumentException("Custom budgets must have a StartDate and EndDate.");

        if (startDate.HasValue && endDate.HasValue && startDate.Value >= endDate.Value)
            throw new ArgumentException("StartDate must be before EndDate.");

        StartDate = startDate;
        EndDate = endDate;
    }

    public static Budget Create(Guid categoryId, Money limit, BudgetPeriod period, DateTimeOffset? startDate = null, DateTimeOffset? endDate = null)
    {
        return new Budget(Guid.NewGuid(), categoryId, limit, period, startDate, endDate);
    }

    /// <summary>
    /// The half-open window <c>[Start, End)</c> this budget is measured over as of
    /// <paramref name="asOf"/>, or <see langword="null"/> when it cannot be determined.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS EXISTS BECAUSE THE BUDGETS SCREEN WAS SHOWING A FABRICATED SPEND (TD-99). It rendered
    /// <c>SpentAmount = 0m</c> for every budget, so every bar read 0% and no budget was ever
    /// exceeded. Computing a real figure means first knowing WHICH transactions belong to the
    /// budget, and that question is a property of the budget, not of the screen or of the query
    /// that reads it — so it is answered here, once, where <see cref="Period"/>,
    /// <see cref="StartDate"/> and <see cref="EndDate"/> all live.
    /// </para>
    /// <para>
    /// AN EXPLICIT PAIR OF DATES WINS OVER THE PERIOD. If the budget stores both bounds it IS the
    /// window; the enum then only records what the user called it. A budget storing just one of the
    /// two — which the constructor permits for every period except <c>Custom</c> — is treated as
    /// having none, because half a window is not a window and guessing the missing end is exactly
    /// the kind of invention this row exists to remove.
    /// </para>
    /// <para>
    /// WHY <c>BiWeekly</c> RETURNS <see langword="null"/> AND THE OTHERS DO NOT. Weekly, monthly and
    /// yearly periods are anchored by the calendar itself: everyone agrees where this month starts.
    /// A fortnight has no such anchor — "this fortnight" is only meaningful relative to some agreed
    /// first fortnight, and this aggregate stores no such origin. Any choice made here (the ISO week
    /// number's parity, the year's first Monday) would be a number invented by the code and then
    /// shown to the user as if it were their budget. The honest answer is that the window is
    /// unknown, and a bi-weekly budget with no explicit dates therefore reports no spend at all
    /// rather than a plausible wrong one.
    /// </para>
    /// <para>
    /// The window is computed in UTC and is half-open, matching the ledger's own
    /// ordering: <c>Transaction.DateUtc</c> is the instant the ledger sorts on, so a transaction at
    /// midnight on the first of the month belongs to that month and one at midnight on the first of
    /// the next belongs to the next.
    /// </para>
    /// </remarks>
    /// <param name="asOf">The instant "the current period" is relative to.</param>
    /// <returns>The window, or <see langword="null"/> when this budget has no determinable one.</returns>
    public (DateTimeOffset Start, DateTimeOffset End)? ResolveWindow(DateTimeOffset asOf)
    {
        if (StartDate.HasValue && EndDate.HasValue)
        {
            return (StartDate.Value, EndDate.Value);
        }

        var now = asOf.ToUniversalTime();

        switch (Period)
        {
            case BudgetPeriod.Monthly:
            {
                var start = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
                return (start, start.AddMonths(1));
            }

            case BudgetPeriod.Yearly:
            {
                var start = new DateTimeOffset(now.Year, 1, 1, 0, 0, 0, TimeSpan.Zero);
                return (start, start.AddYears(1));
            }

            case BudgetPeriod.Weekly:
            {
                // ISO weeks start on Monday; DayOfWeek starts on Sunday, hence the rotation.
                var sinceMonday = ((int)now.DayOfWeek + 6) % 7;
                var midnight = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, TimeSpan.Zero);
                var start = midnight.AddDays(-sinceMonday);
                return (start, start.AddDays(7));
            }

            default:
                return null;
        }
    }

    public void UpdateLimit(Money newLimit)
    {
        Limit = newLimit ?? throw new ArgumentNullException(nameof(newLimit));
    }
}
