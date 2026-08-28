using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Pine.Application.Common;
using Pine.Core.Accounts;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Transactions;

namespace Pine.Application.Transactions.Commands;

/// <summary>
/// What a void actually did: which transactions stopped counting, and which balances moved as a
/// result.
/// </summary>
/// <remarks>
/// Both collections are returned because a transfer is TWO transactions across TWO accounts and
/// voiding one leg necessarily voids the other. A caller that assumed a void touched one row and one
/// balance would refresh half the screen.
/// </remarks>
/// <param name="VoidedTransactionIds">Every transaction voided by this command, in the order voided.</param>
/// <param name="AdjustedAccountIds">
/// Every account that owns a voided transaction, and was therefore saved by this command. Note that
/// this is "touched", not "moved": a transaction of zero is a legitimate record and contributes
/// nothing, so its account appears here with its balance unchanged. Stated precisely because a caller
/// that read this as "these balances are now different" would be wrong in exactly that case.
/// </param>
public sealed record VoidTransactionResult(
    IReadOnlyList<Guid> VoidedTransactionIds,
    IReadOnlyList<Guid> AdjustedAccountIds);

/// <summary>
/// Voids a transaction and undoes its effect on the account balance, in one database transaction.
/// </summary>
/// <param name="TransactionId">The transaction to void.</param>
/// <param name="Reason">Why it is being voided. Required; it is the audit trail.</param>
public record VoidTransactionCommand(
    Guid TransactionId,
    string Reason) : IRequest<VoidTransactionResult>, ITransactionalRequest;

/// <summary>
/// Handles <see cref="VoidTransactionCommand"/>.
/// </summary>
/// <remarks>
/// <para>
/// THE PAIRING IS THE WHOLE POINT (TD-02). Flagging the transaction and moving the balance back are
/// two writes to two aggregates, and a ledger in which one lands without the other is corrupt in a
/// way nothing downstream can detect. They are paired the same way every other money movement in
/// this codebase is: the command is marked <see cref="ITransactionalRequest"/>, so
/// <c>TransactionBehavior</c> wraps the whole handler in one SQLite transaction and rolls all of it
/// back on any throw. Nothing here calls <c>SaveChangesAsync</c>.
/// </para>
/// <para>
/// TRANSFERS ARE VOIDED AS A UNIT. A transfer is two linked transactions on two accounts. Voiding
/// only the leg the user clicked would leave money that left one account but never arrived anywhere,
/// which is a worse state than the one they were correcting. Both legs are therefore voided and both
/// balances adjusted, and the result says so.
/// </para>
/// <para>
/// THE BALANCE IS MOVED WITH <c>Deposit</c>/<c>Withdraw</c>, NOT <c>RecalculateBalance</c>. This is
/// an incremental correction of a known amount, and the incremental mutators are the ones that
/// express it. Recomputing the whole ledger here would silently repair unrelated drift as a side
/// effect of an unrelated user action — the user asked to void one transaction, and would get a
/// balance that changed by more than that transaction with no explanation. Repair is a separate,
/// explicit command.
/// </para>
/// </remarks>
public class VoidTransactionCommandHandler : IRequestHandler<VoidTransactionCommand, VoidTransactionResult>
{
    private readonly ITransactionRepository _transactionRepository;
    private readonly IAccountRepository _accountRepository;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initialises the handler.
    /// </summary>
    /// <param name="transactionRepository">Repository for the transaction being voided and its linked leg.</param>
    /// <param name="accountRepository">Repository for the accounts whose balances are undone.</param>
    /// <param name="timeProvider">Clock used to stamp the void and the balance updates.</param>
    public VoidTransactionCommandHandler(
        ITransactionRepository transactionRepository,
        IAccountRepository accountRepository,
        TimeProvider timeProvider)
    {
        _transactionRepository = transactionRepository;
        _accountRepository = accountRepository;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<VoidTransactionResult> Handle(VoidTransactionCommand request, CancellationToken cancellationToken)
    {
        // Existence, "not already voided" and currency agreement are all established by
        // VoidTransactionCommandValidator, outside this transaction. The null checks below are
        // nullability-flow defence in depth, not the primary guard.
        var transaction = await _transactionRepository.GetByIdAsync(request.TransactionId, cancellationToken)
                          ?? throw new InvalidOperationException("Transaction not found.");

        var legs = new List<Transaction> { transaction };

        if (transaction.LinkedTransactionId is { } linkedId)
        {
            var linked = await _transactionRepository.GetByIdAsync(linkedId, cancellationToken);

            // A linked leg that is missing, or already void, is not an error: the link can dangle on
            // data written before transfers were linked in both directions, and a leg voided by an
            // earlier partial attempt must not block the correction of the one that remains.
            if (linked is not null && !linked.IsVoided)
            {
                legs.Add(linked);
            }
        }

        var voidedIds = new List<Guid>(legs.Count);
        var adjustedAccountIds = new List<Guid>(legs.Count);

        // One Account instance per account id. Two legs of a transfer normally sit on two different
        // accounts, but nothing in the schema forbids both being on one — and loading that account
        // twice would produce two objects, of which only the last one saved would keep its change.
        var accounts = new Dictionary<Guid, Account>();

        foreach (var leg in legs)
        {
            if (!accounts.TryGetValue(leg.AccountId, out var account))
            {
                account = await _accountRepository.GetByIdAsync(leg.AccountId, cancellationToken)
                          ?? throw new InvalidOperationException(
                              $"Account {leg.AccountId} for transaction {leg.Id} was not found.");
                accounts.Add(account.Id, account);
            }

            leg.Void(request.Reason, _timeProvider);
            ReverseBalanceEffect(account, leg.TotalAmount);

            await _transactionRepository.SaveAsync(leg, cancellationToken);
            voidedIds.Add(leg.Id);
        }

        foreach (var account in accounts.Values)
        {
            await _accountRepository.SaveAsync(account, cancellationToken);
            adjustedAccountIds.Add(account.Id);
        }

        return new VoidTransactionResult(voidedIds, adjustedAccountIds);
    }

    /// <summary>
    /// Undoes a transaction's contribution to its account's balance.
    /// </summary>
    /// <remarks>
    /// The amount is signed as stored: an expense is negative, income positive. Undoing it therefore
    /// inverts the operation the write path performed. A zero amount is a legitimate transaction and
    /// contributes nothing, so it is skipped rather than passed to <c>Deposit</c>/<c>Withdraw</c>,
    /// both of which reject a non-positive amount.
    /// </remarks>
    private void ReverseBalanceEffect(Account account, Money amount)
    {
        if (amount.Amount > 0)
        {
            // The write path deposited it; take it back out.
            account.Withdraw(amount, _timeProvider);
        }
        else if (amount.Amount < 0)
        {
            // The write path withdrew its magnitude; put that magnitude back.
            account.Deposit(new Money(-amount.Amount, amount.Currency), _timeProvider);
        }
    }
}

/// <summary>
/// Rejects a <see cref="VoidTransactionCommand"/> that cannot be carried out, before the pipeline
/// opens a database transaction and before anything is written.
/// </summary>
/// <remarks>
/// <para>
/// Every rule here has a domain throw behind it that would otherwise reach the view model verbatim —
/// the wrong-layer failure class TD-12 closed for the write path and this validator closes for the
/// correction path. <c>Transaction.Void</c> throws <c>InvalidOperationException("This transaction has
/// already been voided.")</c>; <c>Money.Subtract</c> throws about "money of different currencies";
/// <c>Account.Withdraw</c> throws about a "strictly positive" amount. None of those sentences tells a
/// user what to do, and all three are avoidable by asking first.
/// </para>
/// <para>
/// THE CURRENCY RULE IS NOT DECORATION. Undoing a transaction's effect subtracts its amount from its
/// account's balance, so a transaction denominated in anything other than its account's currency
/// cannot be voided at all — <c>Money.Subtract</c> would refuse. Such a row is not creatable through
/// any current write path (the four transaction validators see to that), but it is creatable by data
/// written before those validators existed, and this is the operation that would first trip over it.
/// Reporting it as an unvoidable mismatch names the real problem; letting it escape from <c>Money</c>
/// does not.
/// </para>
/// <para>
/// THE LINKED LEG IS CHECKED TOO. Voiding a transfer voids both legs, so a defect on the far leg
/// fails the command halfway through and rolls the whole thing back — leaving the user with a refusal
/// they cannot act on. Checking both up front means the refusal names the leg that is wrong.
/// </para>
/// </remarks>
public sealed class VoidTransactionCommandValidator : IRequestValidator<VoidTransactionCommand>
{
    private readonly ITransactionRepository _transactionRepository;
    private readonly IAccountRepository _accountRepository;

    /// <summary>
    /// Initialises the validator.
    /// </summary>
    /// <param name="transactionRepository">Used to resolve the transaction and its linked leg.</param>
    /// <param name="accountRepository">Used to resolve the accounts those legs belong to.</param>
    public VoidTransactionCommandValidator(
        ITransactionRepository transactionRepository,
        IAccountRepository accountRepository)
    {
        _transactionRepository = transactionRepository;
        _accountRepository = accountRepository;
    }

    /// <summary>
    /// Checks that the transaction exists, is not already void, carries a usable reason, and that
    /// every leg about to be voided can actually have its effect undone.
    /// </summary>
    /// <param name="request">The command about to be handled.</param>
    /// <param name="cancellationToken">Token observed while loading.</param>
    /// <returns>An empty list when the command may proceed, otherwise the errors that stop it.</returns>
    public async Task<IReadOnlyList<ValidationError>> ValidateAsync(
        VoidTransactionCommand request,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<ValidationError>();

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            errors.Add(new ValidationError(
                nameof(VoidTransactionCommand.Reason),
                "A reason is required to void a transaction."));
        }
        else if (request.Reason.Trim().Length > Transaction.MaxVoidReasonLength)
        {
            errors.Add(new ValidationError(
                nameof(VoidTransactionCommand.Reason),
                $"The reason cannot exceed {Transaction.MaxVoidReasonLength} characters."));
        }

        var transaction = await _transactionRepository.GetByIdAsync(request.TransactionId, cancellationToken);
        if (transaction is null)
        {
            // Nothing below can be evaluated without the transaction, so this returns rather than
            // accumulating: errors derived from an absent row would be noise, not help.
            errors.Add(new ValidationError(
                nameof(VoidTransactionCommand.TransactionId),
                $"Transaction {request.TransactionId} was not found."));
            return errors;
        }

        if (transaction.IsVoided)
        {
            errors.Add(new ValidationError(
                nameof(VoidTransactionCommand.TransactionId),
                "This transaction has already been voided."));
            return errors;
        }

        await ValidateLegAsync(transaction, errors, cancellationToken);

        if (transaction.LinkedTransactionId is { } linkedId)
        {
            var linked = await _transactionRepository.GetByIdAsync(linkedId, cancellationToken);
            if (linked is not null && !linked.IsVoided)
            {
                await ValidateLegAsync(linked, errors, cancellationToken);
            }
        }

        return errors;
    }

    /// <summary>
    /// Checks that one leg's account exists and shares the leg's currency, so undoing its effect is
    /// arithmetically possible.
    /// </summary>
    private async Task ValidateLegAsync(
        Transaction leg,
        List<ValidationError> errors,
        CancellationToken cancellationToken)
    {
        var account = await _accountRepository.GetByIdAsync(leg.AccountId, cancellationToken);
        if (account is null)
        {
            errors.Add(new ValidationError(
                nameof(VoidTransactionCommand.TransactionId),
                $"The account for transaction {leg.Id} was not found, so its effect cannot be undone."));
            return;
        }

        if (leg.TotalAmount.Currency != account.Balance.Currency)
        {
            errors.Add(new ValidationError(
                nameof(VoidTransactionCommand.TransactionId),
                $"Transaction {leg.Id} is in {leg.TotalAmount.Currency} but its account is in "
                + $"{account.Balance.Currency}, so its effect on the balance cannot be undone."));
        }
    }
}
