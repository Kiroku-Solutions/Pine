using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Transactions;

namespace Pine.Application.Transactions.Queries;

/// <summary>What came in and what went out this month, in one currency.</summary>
/// <param name="Currency">The currency every figure on this row is in.</param>
/// <param name="Income">Everything received, as a positive quantity.</param>
/// <param name="Spending">Everything spent, as the positive quantity a user reads.</param>
public sealed record CurrencyCashFlow(CurrencyCode Currency, Money Income, Money Spending)
{
    /// <summary>Income less spending. Negative when the month spent more than it earned.</summary>
    public Money Net => Income - Spending;
}

/// <summary>The current month's cash flow, one row per currency that had any.</summary>
/// <param name="PeriodStart">Inclusive start of the month measured: the user's local midnight on the 1st.</param>
/// <param name="PeriodEnd">Exclusive end of the month measured: the local midnight that begins the next.</param>
/// <param name="ByCurrency">One row per currency with activity, ordered by currency code.</param>
public sealed record MonthlyCashFlow(
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    IReadOnlyList<CurrencyCashFlow> ByCurrency)
{
    /// <summary>Whether anything at all was earned or spent in the month.</summary>
    public bool HasActivity => ByCurrency.Count > 0;
}

/// <summary>This month's income and spending, measured from the ledger.</summary>
public sealed record GetMonthlyCashFlowQuery() : IRequest<MonthlyCashFlow>;

/// <summary>
/// Measures the current month's income and spending (post-audit plan item 1.3).
/// </summary>
/// <remarks>
/// <para>
/// WHAT THIS REPLACED WAS THREE TILES THAT SAID NOTHING. The dashboard showed Income, Expenses and
/// Savings tiles that rendered "No data yet" permanently while the ledger they would have described
/// sat one query away. The rule the audit made the house rule after TD-99 is that a screen shows only
/// what it can measure, so they are one tile now, and this is what it measures.
/// </para>
/// <para>
/// THE SAME RULES AS THE BUDGETS SCREEN, EXCEPT THE ONE IT GETS WRONG. Voided transactions do not
/// count, for the same reason they do not count towards a balance. Transfers are money moving between
/// the user's own accounts and are neither earned nor spent. The month is the user's LOCAL calendar
/// month (see <see cref="CurrentLocalMonth"/>) -- which is where this parts company with
/// <c>Budget.ResolveWindow</c>, whose UTC month is a defect of its own. And amounts in different currencies are never added
/// -- Pine has no exchange-rate source, so each currency gets its own row, the same refusal the
/// dashboard's balance summary makes.
/// </para>
/// <para>
/// SIGNS COME FROM THE AGGREGATE, NOT FROM HERE. <c>Transaction</c> refuses an expense above zero and
/// an income below zero, so an expense's amount is negated into spending and an income's is taken as
/// it stands. A refund recorded as income against the month does NOT reduce spending: telling a refund
/// from a salary is something nothing in the model can do, and the budgets screen makes the same call.
/// </para>
/// <para>
/// THE LEDGER IS FILTERED IN MEMORY, for the reason <c>GetBudgetProgressQueryHandler</c> gives: no
/// repository call takes a date window, and pushing one into SQL would compare <c>DateUtc</c> against
/// text bounds in SQLite, which is the class of defect TD-68 closed.
/// </para>
/// </remarks>
public sealed class GetMonthlyCashFlowQueryHandler : IRequestHandler<GetMonthlyCashFlowQuery, MonthlyCashFlow>
{
    private readonly ITransactionRepository _transactions;
    private readonly TimeProvider _timeProvider;

    public GetMonthlyCashFlowQueryHandler(ITransactionRepository transactions, TimeProvider timeProvider)
    {
        _transactions = transactions;
        _timeProvider = timeProvider;
    }

    public async Task<MonthlyCashFlow> Handle(GetMonthlyCashFlowQuery request, CancellationToken cancellationToken)
    {
        var (start, end) = CurrentLocalMonth(_timeProvider);

        var inMonth = (await _transactions.GetAllAsync(cancellationToken).ConfigureAwait(false))
            .Where(transaction => !transaction.IsVoided)
            .Where(transaction => transaction.Type is TransactionType.Income or TransactionType.Expense)
            .Where(transaction => transaction.DateUtc >= start.UtcDateTime && transaction.DateUtc < end.UtcDateTime);

        var rows = inMonth
            .GroupBy(transaction => transaction.TotalAmount.Currency)
            .Select(group =>
            {
                var income = Money.Zero(group.Key);
                var spending = Money.Zero(group.Key);

                foreach (var transaction in group)
                {
                    if (transaction.Type == TransactionType.Income)
                    {
                        income += transaction.TotalAmount;
                    }
                    else
                    {
                        spending += transaction.TotalAmount.Negate();
                    }
                }

                return new CurrencyCashFlow(group.Key, income, spending);
            })
            .OrderBy(row => row.Currency.Value, StringComparer.Ordinal)
            .ToList();

        return new MonthlyCashFlow(start, end, rows);
    }

    /// <summary>
    /// The calendar month the user is in, as two UTC instants: the local midnight that begins it and
    /// the local midnight that begins the next.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE USER'S MONTH, NOT UTC'S -- AND THE FIRST VERSION OF THIS HANDLER GOT IT WRONG. It used the
    /// UTC calendar month, to match <c>Budget.ResolveWindow</c>. An independent review showed what that
    /// means on a real clock: at 21:00 on 30 September in Ecuador (UTC-5) it is already October in
    /// UTC, so a tile titled "This month" showed October -- the month's salary gone, the evening's
    /// dinner counted as next month's. Matching the budgets screen is worth nothing when both are
    /// wrong in the same direction; that half is recorded as its own debt row rather than widened into
    /// this one.
    /// </para>
    /// <para>
    /// Each edge takes the offset in force AT THAT EDGE, not the offset now, because a month that
    /// crosses a daylight-saving change has a first midnight and a last midnight an hour apart in UTC.
    /// A date-only entry lands as LOCAL midnight on every path into the ledger -- checked, not assumed:
    /// <c>CsvImporter</c> and <c>ExcelImporter</c> parse with no offset in the text, which .NET reads as
    /// the local zone's; <c>ReceiptDateParser</c> uses <c>AssumeLocal</c>; manual entry starts from
    /// <c>DateTimeOffset.Now</c>. Such a date therefore falls in the month whose name it carries, in
    /// every time zone. The UTC window put 1 October in Brisbane (UTC+10) into September.
    /// </para>
    /// </remarks>
    /// <param name="timeProvider">The clock, and through it the user's time zone.</param>
    /// <returns>The half-open window [start, end).</returns>
    internal static (DateTimeOffset Start, DateTimeOffset End) CurrentLocalMonth(TimeProvider timeProvider)
    {
        var zone = timeProvider.LocalTimeZone;
        var local = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), zone);

        static DateTimeOffset LocalMidnight(TimeZoneInfo zone, DateTime date) =>
            new(date, zone.GetUtcOffset(date));

        var firstOfMonth = new DateTime(local.Year, local.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);

        return (LocalMidnight(zone, firstOfMonth), LocalMidnight(zone, firstOfMonth.AddMonths(1)));
    }
}
