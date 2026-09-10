using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Pine.Core.Budgets;
using Pine.Core.Categories;
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
/// <param name="CategoryId">The category the budget was created against.</param>
/// <param name="CategoryName">
/// The category's name, or <see langword="null"/> when <paramref name="CategoryId"/> matches no
/// stored category. The screen must say so rather than invent a name.
/// </param>
/// <param name="CategoryColorHex">The category's swatch, or <see langword="null"/> when unresolved.</param>
/// <param name="Limit">The budget's limit, in the currency the budget was created in.</param>
/// <param name="Spent">
/// What has been spent inside the window, in the same currency as <paramref name="Limit"/>, or
/// <see langword="null"/> when it cannot be computed.
/// </param>
/// <param name="PeriodStart">Inclusive start of the window measured, or <see langword="null"/>.</param>
/// <param name="PeriodEnd">Exclusive end of the window measured, or <see langword="null"/>.</param>
/// <param name="UncountedSplitCount">
/// How many splits matched the category and the window but were denominated in a currency other
/// than the budget's, and therefore could not be added to <paramref name="Spent"/>.
/// </param>
public sealed record BudgetProgress(
    Guid BudgetId,
    Guid CategoryId,
    string? CategoryName,
    string? CategoryColorHex,
    Money Limit,
    Money? Spent,
    DateTimeOffset? PeriodStart,
    DateTimeOffset? PeriodEnd,
    int UncountedSplitCount)
{
    /// <summary>Whether a real spend figure was computed for this budget.</summary>
    public bool IsSpendKnown => Spent is not null;

    /// <summary>Whether the known spend is over the limit. Never true while the spend is unknown.</summary>
    public bool IsExceeded => Spent is not null && Spent.Amount > Limit.Amount;
}

/// <summary>Every budget, each with the spend measured against it.</summary>
public sealed record GetBudgetProgressQuery() : IRequest<IReadOnlyList<BudgetProgress>>;

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
/// THE JOIN IS BY CATEGORY NAME, AND THIS IS THE JUDGEMENT CALL IN THIS FILE. <c>Budget</c> holds a
/// <c>CategoryId</c>, a real <see cref="Guid"/> pointing at the <c>Categories</c> table.
/// <c>TransactionSplit.Category</c> holds a NAME — a string a person typed or a CSV column
/// supplied. TD-32 recorded that mismatch and resolved it in favour of the NAME (see
/// <c>CategorisationTrainingData</c>): the string is the model that has instances, because nothing
/// in the shipping application writes a <c>Categories</c> row (TD-132). So the only join available
/// is <c>Budget.CategoryId → Category.Name → split.Category</c>, and it is exact where a category
/// row exists.
/// </para>
/// <para>
/// WHERE IT CANNOT BE MADE EXACT, IT REPORTS NOTHING RATHER THAN A NUMBER. If
/// <c>Budget.CategoryId</c> resolves to no category row — which, given TD-132, is what a
/// user-created budget looks like today — there is no name to compare splits against, and this
/// handler returns <c>Spent = null</c> instead of a zero or a guess. The same applies when the
/// budget's window cannot be determined (<see cref="Budget.ResolveWindow"/>). This is the closest
/// correct thing that can be done without inventing the relationship that TD-132 leaves open, and
/// it is deliberately visible: an unresolvable budget shows as unknown on the screen, which is a
/// prompt to finish TD-132, whereas a fabricated 0 was not.
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
/// cannot be counted against a USD limit — the same reason <c>DashboardViewModel</c> refuses to add
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
    private readonly ICategoryRepository _categories;
    private readonly ITransactionRepository _transactions;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<GetBudgetProgressQueryHandler>? _logger;

    public GetBudgetProgressQueryHandler(
        IBudgetRepository budgets,
        ICategoryRepository categories,
        ITransactionRepository transactions,
        TimeProvider timeProvider,
        ILogger<GetBudgetProgressQueryHandler>? logger = null)
    {
        _budgets = budgets;
        _categories = categories;
        _transactions = transactions;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<IReadOnlyList<BudgetProgress>> Handle(
        GetBudgetProgressQuery request,
        CancellationToken cancellationToken)
    {
        var budgets = (await _budgets.GetAllAsync(cancellationToken).ConfigureAwait(false)).ToList();
        if (budgets.Count == 0)
        {
            return Array.Empty<BudgetProgress>();
        }

        var categories = (await _categories.GetAllAsync(cancellationToken).ConfigureAwait(false))
            .GroupBy(category => category.Id)
            .ToDictionary(group => group.Key, group => group.First());

        // Only the transactions that can contribute: still in force, and actual spending.
        var spending = (await _transactions.GetAllAsync(cancellationToken).ConfigureAwait(false))
            .Where(transaction => !transaction.IsVoided && transaction.Type == TransactionType.Expense)
            .ToList();

        var now = _timeProvider.GetUtcNow();

        return budgets
            .Select(budget => Measure(budget, categories, spending, now))
            .ToList();
    }

    private BudgetProgress Measure(
        Budget budget,
        Dictionary<Guid, Category> categories,
        IReadOnlyList<Transaction> spending,
        DateTimeOffset now)
    {
        categories.TryGetValue(budget.CategoryId, out var category);
        var window = budget.ResolveWindow(now);

        if (category is null || window is null)
        {
#pragma warning disable CA1848, CA1873 // Same scoped exemption as CategorizationTrainingBackgroundService.
            _logger?.LogInformation(
                "Budget {BudgetId} has no measurable spend: category resolved = {CategoryResolved}, window resolved = {WindowResolved}.",
                budget.Id,
                category is not null,
                window is not null);
#pragma warning restore CA1848, CA1873

            return new BudgetProgress(
                budget.Id,
                budget.CategoryId,
                category?.Name,
                category?.ColorHex,
                budget.Limit,
                Spent: null,
                PeriodStart: window?.Start,
                PeriodEnd: window?.End,
                UncountedSplitCount: 0);
        }

        var (start, end) = window.Value;
        var name = category.Name.Trim();
        var currency = budget.Limit.Currency.Value;

        var spent = Money.Zero(budget.Limit.Currency);
        var uncounted = 0;

        foreach (var transaction in spending)
        {
            if (transaction.DateUtc < start.UtcDateTime || transaction.DateUtc >= end.UtcDateTime)
            {
                continue;
            }

            foreach (var split in transaction.Splits)
            {
                if (!string.Equals(split.Category?.Trim(), name, StringComparison.Ordinal))
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
            budget.CategoryId,
            category.Name,
            category.ColorHex,
            budget.Limit,
            spent,
            start,
            end,
            uncounted);
    }
}
