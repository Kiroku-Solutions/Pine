using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Pine.Core.Accounts;
using Pine.Core.Budgets;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Transactions;

namespace Pine.Application.Budgets.Queries;

/// <summary>
/// One budget, with what has actually been spent against it.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Spent"/> IS NULLABLE AND THAT IS THE POINT (TD-99). The screen this feeds used to
/// display a literal <c>0</c> for every budget, which is a number that says "you have spent
/// nothing" — a claim, and a false one. There are budgets whose spend genuinely cannot be
/// determined (see <see cref="GetBudgetProgressQueryHandler"/>), and for those the honest value is
/// "not known", which a <see cref="decimal"/> cannot express and a <see langword="null"/> can. A
/// caller that renders <see langword="null"/> as zero has reintroduced the defect.
/// </para>
/// </remarks>
/// <param name="BudgetId">The budget this row describes.</param>
/// <param name="Category">
/// The category label the budget is measured against. The aggregate refuses to CREATE a budget
/// without one (TD-132, plan item 2.3), but a row read back from a vault bypasses that check, so this
/// can in principle be blank — in which case <see cref="Spent"/> is <see langword="null"/>. It used to be a <c>CategoryId</c> plus a
/// nullable name and a nullable swatch, because the id pointed at a table nothing wrote to — so
/// every budget a user could create rendered as "Unknown category".
/// </param>
/// <param name="Limit">The budget's limit, in the currency the budget was created in.</param>
/// <param name="Spent">
/// What has been spent inside the window, in the same currency as <paramref name="Limit"/>, or
/// <see langword="null"/> when it cannot be computed.
/// </param>
/// <param name="PeriodStart">Inclusive start of the window measured, or <see langword="null"/>.</param>
/// <param name="PeriodEnd">Exclusive end of the window measured, or <see langword="null"/>.</param>
/// <param name="Period">What the budget is called — weekly, monthly — shown beside it on the page (TD-152).</param>
/// <param name="UncountedSplitCount">
/// How many splits matched the category and the window but were denominated in a currency other
/// than the budget's, and therefore could not be added to <paramref name="Spent"/>.
/// </param>
public sealed record BudgetProgress(
    Guid BudgetId,
    string Category,
    Money Limit,
    Money? Spent,
    DateTimeOffset? PeriodStart,
    DateTimeOffset? PeriodEnd,
    int UncountedSplitCount,
    BudgetPeriod Period,
    Guid? AccountId = null,
    string? AccountName = null)
{
    /// <summary>Whether a real spend figure was computed for this budget.</summary>
    public bool IsSpendKnown => Spent is not null;

    /// <summary>Whether the known spend is over the limit. Never true while the spend is unknown.</summary>
    public bool IsExceeded => Spent is not null && Spent.Amount > Limit.Amount;

    /// <summary>Remaining budget allowance (negative when over budget), or null when spend is unknown.</summary>
    public Money? Remaining => Spent is null ? null : Limit - Spent;
}

/// <summary>Every budget, each with the spend measured against it (optionally filtered to specific accounts).</summary>
public sealed record GetBudgetProgressQuery(IReadOnlyCollection<Guid>? AccountIds = null) : IRequest<IReadOnlyList<BudgetProgress>>;

/// <summary>
/// Measures each budget's spend from the ledger.
/// </summary>
/// <remarks>
/// <para>
/// WHAT THIS REPLACED WAS NOT AN EMPTY STATE, IT WAS A FABRICATED ONE (TD-99).
/// <c>BudgetsViewModel</c> set <c>SpentAmount = 0m</c> with the comment "Will be calculated from
/// transactions", and named each row <c>"Category " + b.CategoryId.ToString()[..4]</c>. So every
/// progress bar read 0%, <c>IsExceeded</c> could never be true — the exceeded warning in
/// <c>BudgetsView.axaml</c> was unreachable markup — and the category was four hex digits of a
/// GUID. The screen worked; it just was not about the user's money.
/// </para>
/// <para>
/// IT LIVES IN THE APPLICATION LAYER because it is a read model over three aggregates. A view model
/// that joins budgets to categories to transactions is a view model that cannot be exercised
/// without a UI, and this is the part worth testing.
/// </para>
/// <para>
/// THE JOIN IS BY CATEGORY LABEL, AND SINCE PLAN ITEM 2.3 THAT IS NOT A COMPROMISE, IT IS THE MODEL.
/// <c>Budget.Category</c>, <c>TransactionSplit.Category</c>, <c>Rule.Category</c> and the
/// categoriser's training set are all the same kind of thing: a label a person typed or a CSV
/// column supplied. Until TD-132 was decided, <c>Budget</c> held a <see cref="Guid"/> into a
/// <c>Categories</c> table that nothing in the shipping application ever wrote to, so the join had
/// to go <c>CategoryId → Category.Name → split.Category</c> and broke at the first hop for every
/// budget a real user could create.
/// </para>
/// <para>
/// WHAT THAT USED TO COST, AND DOES NOT ANY MORE. This handler returned <c>Spent = null</c>
/// whenever the id resolved to no row — which was always — so the budgets screen reported "not
/// known" for every budget it could ever be given. The remaining reason for a null spend is a
/// window that cannot be determined (<see cref="Budget.ResolveWindow"/>: a bi-weekly budget with no
/// explicit dates has no calendar anchor). That is one honest unknown instead of a universal one.
/// </para>
/// <para>
/// NAMES ARE COMPARED TRIMMED AND ORDINALLY, CASE INTACT, exactly as
/// <c>CategorisationTrainingData</c> compares them. Two implementations of "the same category"
/// disagreeing would mean the categoriser and the budgets screen grouping a user's spending
/// differently, so the rule is copied deliberately rather than re-decided here: folding case would
/// silently merge categories the user chose to spell apart, which is a decision for the
/// category-management feature that does not exist yet.
/// </para>
/// <para>
/// ONLY EXPENSES COUNT, AND VOIDED TRANSACTIONS DO NOT. A voided transaction is excluded on the
/// same grounds it is excluded from an account balance: <c>VoidedAt</c> means it no longer counts
/// (see <c>ITransactionRepository.GetAmountsInForceAsync</c>). Transfers are movements between the
/// user's own accounts and are not spending. Income is excluded too, and that is a choice with a
/// visible consequence: a refund banked as income against the same category does NOT reduce the
/// spend shown. Netting it off would require distinguishing a refund from a salary, and nothing in
/// the model does — <see cref="TransactionType.Income"/> is all either of them is. A split that is
/// positive INSIDE an expense transaction does net off, because there the transaction itself has
/// already said what kind of movement it is.
/// </para>
/// <para>
/// A SPLIT IN ANOTHER CURRENCY IS NOT ADDED. Pine has no exchange-rate source, so a EUR split
/// cannot be counted against a USD limit — the same reason the dashboard's total-balance tile refuses to add
/// balances across currencies rather than producing "1900 EUR" from 1 000 USD and 900 EUR. Such
/// splits are counted in <see cref="BudgetProgress.UncountedSplitCount"/> and logged, so that
/// exclusion is visible somewhere rather than silent.
/// </para>
/// <para>
/// THE LEDGER IS FILTERED IN MEMORY. There is no repository call that takes a date window, and the
/// alternative — pushing the window into SQL — would compare <c>DateUtc</c> against text bounds in
/// SQLite, which is the class of defect TD-68 closed. A personal vault's ledger is small enough
/// that reading it once per screen load is the cheaper mistake.
/// </para>
/// </remarks>
public sealed class GetBudgetProgressQueryHandler
    : IRequestHandler<GetBudgetProgressQuery, IReadOnlyList<BudgetProgress>>
{
    private readonly IBudgetRepository _budgets;
    private readonly ITransactionRepository _transactions;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<GetBudgetProgressQueryHandler>? _logger;
    private readonly IAccountRepository? _accounts;

    public GetBudgetProgressQueryHandler(
        IBudgetRepository budgets,
        ITransactionRepository transactions,
        TimeProvider timeProvider,
        ILogger<GetBudgetProgressQueryHandler>? logger = null,
        IAccountRepository? accounts = null)
    {
        _budgets = budgets;
        _transactions = transactions;
        _timeProvider = timeProvider;
        _logger = logger;
        _accounts = accounts;
    }

    public async Task<IReadOnlyList<BudgetProgress>> Handle(
        GetBudgetProgressQuery request,
        CancellationToken cancellationToken)
    {
        var budgets = (await _budgets.GetAllAsync(cancellationToken).ConfigureAwait(false)).ToList();
        if (request.AccountIds is { Count: > 0 } scopeIds)
        {
            var allowed = new HashSet<Guid>(scopeIds);
            budgets = budgets.Where(b => b.AccountId.HasValue && allowed.Contains(b.AccountId.Value)).ToList();
        }
        if (budgets.Count == 0)
        {
            return Array.Empty<BudgetProgress>();
        }

        // Only the transactions that can contribute: still in force, and actual spending.
        var spending = (await _transactions.GetAllAsync(cancellationToken).ConfigureAwait(false))
            .Where(transaction => !transaction.IsVoided && transaction.Type == TransactionType.Expense)
            .ToList();

        var accountNames = new Dictionary<Guid, string>();
        if (_accounts is not null && budgets.Any(b => b.AccountId.HasValue))
        {
            var allAccounts = await _accounts.GetAllAsync(cancellationToken).ConfigureAwait(false);
            foreach (var account in allAccounts)
            {
                accountNames[account.Id] = account.Name;
            }
        }

        var budgetsPerAccount = budgets
            .Where(b => b.AccountId.HasValue)
            .GroupBy(b => b.AccountId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        var now = _timeProvider.GetUtcNow();

        return budgets
            .Select(budget => Measure(budget, spending, accountNames, budgetsPerAccount, now))
            .ToList();
    }

    private BudgetProgress Measure(
        Budget budget,
        List<Transaction> spending,
        Dictionary<Guid, string> accountNames,
        Dictionary<Guid, int> budgetsPerAccount,
        DateTimeOffset now)
    {
        var window = budget.ResolveWindow(now, _timeProvider.LocalTimeZone);
        var accountName = budget.AccountId is { } acctId && accountNames.TryGetValue(acctId, out var resolved)
            ? resolved
            : null;

        // EF materialises a budget without running its constructor, so the aggregate's "non-blank,
        // trimmed" rule holds for budgets CREATED in this process and not necessarily for rows read
        // back from a vault. A blank label would match every uncategorised split — measured by the
        // independent review of plan item 2.3 as a confident spend against "no category" — so it is
        // treated as unmeasurable rather than as a category.
        var name = (budget.Category ?? string.Empty).Trim();

        if (window is null || name.Length == 0)
        {
#pragma warning disable CA1848, CA1873 // Same scoped exemption as CategorizationTrainingBackgroundService.
            _logger?.LogInformation(
                "Budget {BudgetId} ({Category}) has no measurable spend: window resolved = {WindowResolved}, category present = {CategoryPresent}.",
                budget.Id,
                name,
                window is not null,
                name.Length > 0);
#pragma warning restore CA1848, CA1873

            return new BudgetProgress(
                budget.Id,
                name,
                budget.Limit,
                Spent: null,
                PeriodStart: window?.Start,
                PeriodEnd: window?.End,
                UncountedSplitCount: 0,
                Period: budget.Period,
                AccountId: budget.AccountId,
                AccountName: accountName);
        }

        var (start, end) = window.Value;
        var currency = budget.Limit.Currency.Value;

        var spent = Money.Zero(budget.Limit.Currency);
        var uncounted = 0;

        foreach (var transaction in spending)
        {
            if (transaction.DateUtc < start.UtcDateTime || transaction.DateUtc >= end.UtcDateTime)
            {
                continue;
            }

            if (budget.AccountId.HasValue && transaction.AccountId != budget.AccountId.Value)
            {
                continue;
            }

            var singleBudgetOnAccount = budget.AccountId is { } scopedId
                && budgetsPerAccount.TryGetValue(scopedId, out var count)
                && count == 1;

            foreach (var split in transaction.Splits)
            {
                var splitCategory = split.Category?.Trim();
                // Match uncategorized splits only when this is the sole budget scoped to that account,
                // preventing double-counting when an account has multiple category budgets.
                var matchesCategory = string.Equals(splitCategory, name, StringComparison.Ordinal)
                    || (singleBudgetOnAccount && string.IsNullOrEmpty(splitCategory));

                if (!matchesCategory)
                {
                    continue;
                }

                if (!string.Equals(split.Amount.Currency.Value, currency, StringComparison.Ordinal))
                {
                    uncounted++;
                    continue;
                }

                // An expense is stored negative; spend is the positive quantity a user reads.
                spent = spent.Add(split.Amount.Negate());
            }
        }

        if (uncounted > 0)
        {
#pragma warning disable CA1848
            _logger?.LogWarning(
                "Budget {BudgetId} ({Category}) ignored {Count} split(s) denominated in another currency than {Currency}; Pine has no exchange rates, so they cannot be counted against the limit.",
                budget.Id,
                name,
                uncounted,
                currency);
#pragma warning restore CA1848
        }

        return new BudgetProgress(
            budget.Id,
            name,
            budget.Limit,
            spent,
            start,
            end,
            uncounted,
            budget.Period,
            budget.AccountId,
            accountName);
    }
}
