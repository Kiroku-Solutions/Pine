using System;
using Pine.Core.Common;
using Pine.Core.Common.ValueObjects;

namespace Pine.Core.Budgets;

public class Budget
{
    public Guid Id { get; private set; }

    /// <summary>The category this budget is measured against: the label itself (TD-132).</summary>
    /// <remarks>
    /// <para>
    /// THIS WAS A <c>Guid CategoryId</c>, A FOREIGN KEY INTO A TABLE NOTHING IN PRODUCTION HAD WRITTEN
    /// TO SINCE <c>67af1f3</c> (before it, one demo row from the Diagnostics "Test Features" button). TD-132
    /// asked for the decision and post-audit plan item 2.3 took it: the `Category` aggregate is
    /// gone and the label is the model. `10-Domain-Model` §7 had always said so — `Category` is
    /// listed there as a VALUE OBJECT, "a hierarchical label", and §6 lists no `Category` aggregate
    /// at all — so the class that existed in code was the thing that disagreed with the canonical
    /// document, not this.
    /// </para>
    /// <para>
    /// WHAT IT BUYS, CONCRETELY: a budget can now be compared with the ledger. Every other place a
    /// category appears — <c>TransactionSplit.Category</c>, <c>Rule.Category</c>,
    /// <c>Transaction.Category</c>, the categoriser's training set and its predictions — is already
    /// a label, so this was the one endpoint speaking a different language, and
    /// <c>GetBudgetProgressQuery</c> had to report "spend unknown" for every budget a user could
    /// create because the id resolved to nothing.
    /// </para>
    /// <para>
    /// It is compared TRIMMED AND ORDINALLY, case intact, the same rule
    /// <c>CategorisationTrainingData</c> and <c>RuleEvaluator</c> use. Folding case would merge
    /// categories a user chose to spell apart.
    /// </para>
    /// </remarks>
    public string Category { get; private set; }
    public Money Limit { get; private set; }
    public BudgetPeriod Period { get; private set; }
    
    // For Custom periods, StartDate and EndDate define the exact bounds.
    // For Monthly, Weekly, etc., they can be null and the bounds are dynamically calculated based on the current date,
    // OR we can explicitly store them for a specific historical budget period.
    public DateTimeOffset? StartDate { get; private set; }
    public DateTimeOffset? EndDate { get; private set; }

    /// <summary>
    /// <see cref="StartDate"/> as a UTC instant, kept so the store can order and compare budget
    /// periods (TD-96).
    /// </summary>
    /// <remarks>
    /// Same reasoning as <c>Transaction.DateUtc</c>: SQLite stores a <see cref="DateTimeOffset"/> as
    /// text with the offset appended, so comparing it compares local wall clocks, not instants.
    /// <see cref="StartDate"/> is kept as entered; this is derived in the only place it is assigned,
    /// so the two cannot drift apart.
    /// </remarks>
    public DateTime? StartDateUtc { get; private set; }

    /// <summary><see cref="EndDate"/> as a UTC instant. See <see cref="StartDateUtc"/>.</summary>
    public DateTime? EndDateUtc { get; private set; }

    /// <summary>The limit as it stood before the last base-currency conversion; null for a budget never converted.</summary>
    public decimal? PreviousLimitAmount { get; private set; }

    /// <summary>The currency <see cref="PreviousLimitAmount"/> was in.</summary>
    public string? PreviousLimitCurrency { get; private set; }

    /// <summary>The rate the last conversion applied (units of the new currency per unit of the old).</summary>
    public decimal? LimitConversionRate { get; private set; }

    /// <summary>When the last conversion happened, UTC.</summary>
    public DateTime? LimitConvertedAtUtc { get; private set; }

    private Budget() { Limit = null!; Category = null!; } // EF Core

    private Budget(Guid id, string category, Money limit, BudgetPeriod period, DateTimeOffset? startDate, DateTimeOffset? endDate)
    {
        Id = id;

        // A budget against no category measures nothing: there is no set of splits it could be
        // compared with, so the row would render as a limit with a permanently unknown spend. That
        // was the old CategoryId's failure mode and it is refused here rather than reproduced.
        if (string.IsNullOrWhiteSpace(category))
        {
            throw new ArgumentException("A budget must name the category it is measured against.", nameof(category));
        }

        // Stored trimmed, because every comparison against it trims. Storing the untrimmed form and
        // trimming at each use is the arrangement that eventually has one caller who forgets.
        Category = category.Trim();
        Limit = limit ?? throw new ArgumentNullException(nameof(limit));
        Period = period;
        
        if (period == BudgetPeriod.Custom && (!startDate.HasValue || !endDate.HasValue))
            throw new ArgumentException("Custom budgets must have a StartDate and EndDate.");

        if (startDate.HasValue && endDate.HasValue && startDate.Value >= endDate.Value)
            throw new ArgumentException("StartDate must be before EndDate.");

        StartDate = startDate;
        EndDate = endDate;
        StartDateUtc = startDate?.UtcDateTime;
        EndDateUtc = endDate?.UtcDateTime;
    }

    public static Budget Create(string category, Money limit, BudgetPeriod period, DateTimeOffset? startDate = null, DateTimeOffset? endDate = null)
    {
        return new Budget(Guid.NewGuid(), category, limit, period, startDate, endDate);
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
    /// The window is the user's CALENDAR period, in <paramref name="zone"/>, and is half-open, matching the ledger's own
    /// ordering: <c>Transaction.DateUtc</c> is the instant the ledger sorts on, so a transaction at
    /// midnight on the first of the month belongs to that month and one at midnight on the first of
    /// the next belongs to the next.
    /// </para>
    /// </remarks>
    /// <para>
    /// IT WAS UTC, AND THAT WAS TD-149. Every path into the ledger stores a date-only entry as LOCAL
    /// midnight, so a UTC month disagreed with the transactions near its edges: for an Ecuadorian user a
    /// monthly budget measured October from 19:00 on 30 September. That was unreachable while no screen
    /// could create a budget, and became the default the moment one could (post-audit plan item 3.0,
    /// found by its independent review). Each edge is now local midnight at the offset in force AT THAT
    /// EDGE — a month that crosses a daylight-saving change starts and ends at different offsets — the
    /// rule <c>GetMonthlyCashFlowQueryHandler.CurrentLocalMonth</c> already applied to the dashboard.
    /// </para>
    /// </remarks>
    /// <param name="asOf">The instant "the current period" is relative to.</param>
    /// <param name="zone">The user's time zone, which decides where a day, a week and a month begin.</param>
    /// <returns>The window, or <see langword="null"/> when this budget has no determinable one.</returns>
    public (DateTimeOffset Start, DateTimeOffset End)? ResolveWindow(DateTimeOffset asOf, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);

        if (StartDate.HasValue && EndDate.HasValue)
        {
            return (StartDate.Value, EndDate.Value);
        }

        var local = TimeZoneInfo.ConvertTime(asOf, zone).DateTime;

        DateTimeOffset Midnight(DateTime date)
        {
            var day = new DateTime(date.Year, date.Month, date.Day, 0, 0, 0, DateTimeKind.Unspecified);
            return new DateTimeOffset(day, zone.GetUtcOffset(day));
        }

        switch (Period)
        {
            case BudgetPeriod.Monthly:
            {
                var first = new DateTime(local.Year, local.Month, 1);
                return (Midnight(first), Midnight(first.AddMonths(1)));
            }

            case BudgetPeriod.Yearly:
            {
                var first = new DateTime(local.Year, 1, 1);
                return (Midnight(first), Midnight(first.AddYears(1)));
            }

            case BudgetPeriod.Weekly:
            {
                // ISO weeks start on Monday; DayOfWeek starts on Sunday, hence the rotation.
                var sinceMonday = ((int)local.DayOfWeek + 6) % 7;
                var monday = local.Date.AddDays(-sinceMonday);
                return (Midnight(monday), Midnight(monday.AddDays(7)));
            }

            default:
                return null;
        }
    }

    /// <summary>
    /// Converts the limit, once, into <paramref name="target"/> at <paramref name="rate"/>, keeping what it was.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A BASE-CURRENCY CHANGE IS NOT A NEW BUDGET. The user's intent ("keep the same budget, now measured in the new
    /// currency") is one conversion at the rate in force, rounded half-to-even to the target's scale
    /// (<see cref="Money.FromRounded"/>). The previous amount, currency, the rate used and the time are written
    /// on the budget itself so the change is traceable; a later conversion overwrites them with its own.
    /// </para>
    /// <para>
    /// REFUSES RATHER THAN DEGRADES: a non-positive rate, the same currency (nothing to convert), and a result that
    /// rounds to zero (a budget every purchase exceeds) all throw; the caller decides what to tell the user first.
    /// </para>
    /// </remarks>
    /// <param name="target">The currency the limit becomes.</param>
    /// <param name="rate">Units of <paramref name="target"/> per unit of the current currency.</param>
    /// <param name="convertedAtUtc">When the conversion is done.</param>
    /// <exception cref="InvalidOperationException">The budget is already in <paramref name="target"/>, or the result is not positive.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="rate"/> is not positive.</exception>
    public void ConvertTo(CurrencyCode target, decimal rate, DateTime convertedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (rate <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(rate), rate, "A conversion rate must be positive.");
        }

        if (Limit.Currency == target)
        {
            throw new InvalidOperationException("The budget is already in " + target.Value + ".");
        }

        var converted = Money.FromRounded(Limit.Amount * rate, target);
        if (converted.Amount <= 0m)
        {
            throw new InvalidOperationException("Converting the limit would leave a budget of zero.");
        }

        PreviousLimitAmount = Limit.Amount;
        PreviousLimitCurrency = Limit.Currency.Value;
        LimitConversionRate = rate;
        LimitConvertedAtUtc = DateTime.SpecifyKind(convertedAtUtc, DateTimeKind.Utc);
        Limit = converted;
    }

    public void UpdateLimit(Money newLimit)
    {
        Limit = newLimit ?? throw new ArgumentNullException(nameof(newLimit));
    }
}
