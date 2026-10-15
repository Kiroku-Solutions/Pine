using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Pine.Application.Common;
using Pine.Core.Rules;
using Pine.Core.Transactions;

namespace Pine.Application.Rules;

/// <summary>
/// Which existing transactions a retroactive rule run may touch (TD-142).
/// </summary>
/// <remarks>
/// <para>
/// ONLY AN UNCATEGORISED, IN-FORCE, NON-TRANSFER TRANSACTION. This is the same promise the create
/// path makes -- a rule fills a blank and never overrules a person -- carried to the ledger: a
/// transaction that already carries any category was either typed by the user or accepted from the
/// classifier, and a rule written later must not silently rewrite it. A voided row keeps its last
/// true values by design, and a transfer leg has no category of its own.
/// </para>
/// <para>
/// The preview and the apply handler share this one predicate, so what the user was shown and what
/// is written cannot drift apart.
/// </para>
/// </remarks>
public static class RetroactiveRuleEligibility
{
    /// <summary>Whether <paramref name="transaction"/> may be categorised by a retroactive run.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <returns><see langword="true"/> when it is in force, not a transfer, and has no category.</returns>
    public static bool IsEligible(Transaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        return !transaction.IsVoided
               && transaction.Type != TransactionType.Transfer
               && transaction.Splits.All(split => string.IsNullOrWhiteSpace(split.Category));
    }
}

/// <summary>One transaction a retroactive run would recategorise.</summary>
/// <param name="TransactionId">The transaction.</param>
/// <param name="DateUtc">Its date, in UTC.</param>
/// <param name="Payee">Its payee.</param>
/// <param name="Amount">Its signed amount.</param>
/// <param name="RuleTitle">The rule that would fire.</param>
/// <param name="NewCategory">The category it would receive.</param>
public sealed record RulePreviewItem(
    Guid TransactionId,
    DateTime DateUtc,
    string Payee,
    decimal Amount,
    string RuleTitle,
    string NewCategory);

/// <summary>
/// What applying the enabled rules to the existing ledger would change. Read-only: nothing is written.
/// </summary>
public sealed record PreviewRuleApplicationQuery : IRequest<IReadOnlyList<RulePreviewItem>>;

/// <summary>Handles <see cref="PreviewRuleApplicationQuery"/>.</summary>
public sealed class PreviewRuleApplicationQueryHandler
    : IRequestHandler<PreviewRuleApplicationQuery, IReadOnlyList<RulePreviewItem>>
{
    private readonly IRuleRepository _rules;
    private readonly ITransactionRepository _transactions;

    /// <summary>Creates the handler.</summary>
    /// <param name="rules">The rule repository.</param>
    /// <param name="transactions">The transaction repository.</param>
    public PreviewRuleApplicationQueryHandler(IRuleRepository rules, ITransactionRepository transactions)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(transactions);
        _rules = rules;
        _transactions = transactions;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RulePreviewItem>> Handle(
        PreviewRuleApplicationQuery request,
        CancellationToken cancellationToken)
    {
        var rules = await _rules.GetEnabledAsync(cancellationToken).ConfigureAwait(false);

        if (rules.Count == 0)
        {
            return [];
        }

        var transactions = await _transactions.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var items = new List<RulePreviewItem>();

        foreach (var transaction in transactions.Where(RetroactiveRuleEligibility.IsEligible))
        {
            var match = RuleEvaluator.FirstMatch(
                rules,
                new RuleInput(transaction.Payee ?? string.Empty, transaction.Notes ?? string.Empty, transaction.TotalAmount.Amount));

            if (match is not null)
            {
                items.Add(new RulePreviewItem(
                    transaction.Id,
                    transaction.DateUtc,
                    transaction.Payee ?? string.Empty,
                    transaction.TotalAmount.Amount,
                    match.Title,
                    match.Category));
            }
        }

        return items
            .OrderByDescending(item => item.DateUtc)
            .ThenBy(item => item.TransactionId)
            .ToList();
    }
}

/// <summary>
/// Applies the enabled rules to the named, previously previewed transactions.
/// </summary>
/// <param name="TransactionIds">The transactions the user saw in the preview and approved.</param>
/// <remarks>
/// IT RE-EVALUATES RATHER THAN TRUSTING THE PREVIEW. The ids say WHICH rows the user approved; the
/// category written is whatever the rules say now, and a row that is no longer eligible (edited,
/// voided or categorised since the preview) or no longer matches is skipped, not forced. The
/// ledger can have moved between the two calls; the preview was a view, not a contract.
/// </remarks>
public sealed record ApplyRulesToExistingCommand(IReadOnlyCollection<Guid> TransactionIds)
    : IRequest<int>, ITransactionalRequest;

/// <summary>Handles <see cref="ApplyRulesToExistingCommand"/>.</summary>
public sealed class ApplyRulesToExistingCommandHandler : IRequestHandler<ApplyRulesToExistingCommand, int>
{
    private readonly IRuleRepository _rules;
    private readonly ITransactionRepository _transactions;

    /// <summary>Creates the handler.</summary>
    /// <param name="rules">The rule repository.</param>
    /// <param name="transactions">The transaction repository.</param>
    public ApplyRulesToExistingCommandHandler(IRuleRepository rules, ITransactionRepository transactions)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(transactions);
        _rules = rules;
        _transactions = transactions;
    }

    /// <inheritdoc />
    /// <returns>How many transactions were recategorised.</returns>
    public async Task<int> Handle(ApplyRulesToExistingCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var rules = await _rules.GetEnabledAsync(cancellationToken).ConfigureAwait(false);
        var applied = 0;

        foreach (var id in request.TransactionIds.Distinct())
        {
            var transaction = await _transactions.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

            if (transaction is null || !RetroactiveRuleEligibility.IsEligible(transaction))
            {
                continue;
            }

            var match = RuleEvaluator.FirstMatch(
                rules,
                new RuleInput(transaction.Payee ?? string.Empty, transaction.Notes ?? string.Empty, transaction.TotalAmount.Amount));

            if (match is null)
            {
                continue;
            }

            // Amounts are untouched, so the account balance is untouched: only the label changes.
            if (transaction.Splits.Count == 0)
            {
                transaction.ReplaceSplits([new SplitDefinition(transaction.TotalAmount, match.Category, match.Title)]);
            }
            else
            {
                transaction.ReplaceSplits(transaction.Splits.Select(split => new SplitDefinition(
                    split.Amount,
                    match.Category,
                    string.IsNullOrWhiteSpace(split.Notes) ? match.Title : split.Notes,
                    split.Id)).ToList());
            }

            await _transactions.SaveAsync(transaction, cancellationToken).ConfigureAwait(false);
            applied++;
        }

        return applied;
    }
}
