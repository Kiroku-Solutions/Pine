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
/// What an unvoid actually did: which transactions are back in force, and which accounts were saved.
/// </summary>
/// <param name="UnvoidedTransactionIds">Every transaction put back in force, in the order processed.</param>
/// <param name="AdjustedAccountIds">
/// Every account that owns an unvoided transaction and was therefore saved. "Touched", not "moved": a
/// zero-amount transaction contributes nothing to its account's balance.
/// </param>
public sealed record UnvoidTransactionResult(
    IReadOnlyList<Guid> UnvoidedTransactionIds,
    IReadOnlyList<Guid> AdjustedAccountIds);

/// <summary>
/// Puts a voided transaction back in force and re-applies its effect on the account balance, in one
/// database transaction (TD-69).
/// </summary>
/// <param name="TransactionId">The voided transaction to restore.</param>
public record UnvoidTransactionCommand(Guid TransactionId) : IRequest<UnvoidTransactionResult>, ITransactionalRequest;

/// <summary>
/// Handles <see cref="UnvoidTransactionCommand"/>: the exact mirror of
/// <see cref="VoidTransactionCommandHandler"/>.
/// </summary>
/// <remarks>
/// <para>
/// THE PAIRING IS THE SAME AS FOR A VOID. Clearing the void flag and re-applying the amount are two
/// writes to two aggregates; the command is <see cref="ITransactionalRequest"/>, so both commit or
/// neither does. The incremental <c>Deposit</c>/<c>Withdraw</c> mutators are used, not a recalculation,
/// for the reason the void handler gives.
/// </para>
/// <para>
/// TRANSFERS ARE UNVOIDED AS A UNIT, as they are voided as one: restoring a single leg would put money
/// back on one account that never left the other. A far leg that is missing or already in force is
/// skipped rather than treated as an error, mirroring the void handler's tolerance.
/// </para>
/// </remarks>
public class UnvoidTransactionCommandHandler : IRequestHandler<UnvoidTransactionCommand, UnvoidTransactionResult>
{
    private readonly ITransactionRepository _transactionRepository;
    private readonly IAccountRepository _accountRepository;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initialises the handler.</summary>
    /// <param name="transactionRepository">Repository for the transaction and its linked leg.</param>
    /// <param name="accountRepository">Repository for the accounts whose balances are re-applied.</param>
    /// <param name="timeProvider">Clock used to stamp the balance updates.</param>
    public UnvoidTransactionCommandHandler(
        ITransactionRepository transactionRepository,
        IAccountRepository accountRepository,
        TimeProvider timeProvider)
    {
        _transactionRepository = transactionRepository;
        _accountRepository = accountRepository;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<UnvoidTransactionResult> Handle(UnvoidTransactionCommand request, CancellationToken cancellationToken)
    {
        // Existence, "is voided" and currency agreement are established by the validator, outside this
        // transaction; these throws are nullability-flow defence in depth.
        var transaction = await _transactionRepository.GetByIdAsync(request.TransactionId, cancellationToken)
                          ?? throw new InvalidOperationException("Transaction not found.");

        var legs = new List<Transaction> { transaction };

        if (transaction.LinkedTransactionId is { } linkedId)
        {
            var linked = await _transactionRepository.GetByIdAsync(linkedId, cancellationToken);
            if (linked is not null && linked.IsVoided)
            {
                legs.Add(linked);
            }
        }

        var unvoidedIds = new List<Guid>(legs.Count);
        var adjustedAccountIds = new List<Guid>(legs.Count);
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

            leg.Unvoid();
            ReapplyBalanceEffect(account, leg.TotalAmount);

            await _transactionRepository.SaveAsync(leg, cancellationToken);
            unvoidedIds.Add(leg.Id);
        }

        foreach (var account in accounts.Values)
        {
            await _accountRepository.SaveAsync(account, cancellationToken);
            adjustedAccountIds.Add(account.Id);
        }

        return new UnvoidTransactionResult(unvoidedIds, adjustedAccountIds);
    }

    /// <summary>
    /// Re-applies a transaction's contribution to its account's balance, as the write path did: income
    /// is deposited, an expense (stored negative) withdraws its magnitude, and zero does nothing.
    /// </summary>
    private void ReapplyBalanceEffect(Account account, Money amount)
    {
        if (amount.Amount > 0)
        {
            account.Deposit(amount, _timeProvider);
        }
        else if (amount.Amount < 0)
        {
            account.Withdraw(-amount, _timeProvider);
        }
    }
}

/// <summary>
/// Rejects an <see cref="UnvoidTransactionCommand"/> that cannot be carried out, before the pipeline
/// opens a database transaction (TD-12 discipline, as for the void).
/// </summary>
public sealed class UnvoidTransactionCommandValidator : IRequestValidator<UnvoidTransactionCommand>
{
    private readonly ITransactionRepository _transactionRepository;
    private readonly IAccountRepository _accountRepository;

    /// <summary>Initialises the validator.</summary>
    /// <param name="transactionRepository">Used to resolve the transaction and its linked leg.</param>
    /// <param name="accountRepository">Used to resolve the accounts those legs belong to.</param>
    public UnvoidTransactionCommandValidator(
        ITransactionRepository transactionRepository,
        IAccountRepository accountRepository)
    {
        _transactionRepository = transactionRepository;
        _accountRepository = accountRepository;
    }

    /// <summary>
    /// Checks that the transaction exists, is actually voided, and that every leg about to be
    /// restored has an account in its own currency.
    /// </summary>
    /// <param name="request">The command about to be handled.</param>
    /// <param name="cancellationToken">Token observed while loading.</param>
    /// <returns>An empty list when the command may proceed, otherwise the errors that stop it.</returns>
    public async Task<IReadOnlyList<ValidationError>> ValidateAsync(
        UnvoidTransactionCommand request,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<ValidationError>();

        var transaction = await _transactionRepository.GetByIdAsync(request.TransactionId, cancellationToken);
        if (transaction is null)
        {
            errors.Add(new ValidationError(
                nameof(UnvoidTransactionCommand.TransactionId),
                $"Transaction {request.TransactionId} was not found.",
                ValidationText.TransactionNotFound()));
            return errors;
        }

        if (!transaction.IsVoided)
        {
            errors.Add(new ValidationError(
                nameof(UnvoidTransactionCommand.TransactionId),
                "This transaction has not been voided.",
                ValidationText.UnvoidNotVoided()));
            return errors;
        }

        await ValidateLegAsync(transaction, errors, cancellationToken);

        if (transaction.LinkedTransactionId is { } linkedId)
        {
            var linked = await _transactionRepository.GetByIdAsync(linkedId, cancellationToken);
            if (linked is not null && linked.IsVoided)
            {
                await ValidateLegAsync(linked, errors, cancellationToken);
            }
        }

        return errors;
    }

    private async Task ValidateLegAsync(
        Transaction leg,
        List<ValidationError> errors,
        CancellationToken cancellationToken)
    {
        var account = await _accountRepository.GetByIdAsync(leg.AccountId, cancellationToken);
        if (account is null)
        {
            errors.Add(new ValidationError(
                nameof(UnvoidTransactionCommand.TransactionId),
                $"The account for transaction {leg.Id} was not found, so its effect cannot be re-applied.",
                ValidationText.UnvoidAccountMissing()));
            return;
        }

        if (leg.TotalAmount.Currency != account.Balance.Currency)
        {
            errors.Add(new ValidationError(
                nameof(UnvoidTransactionCommand.TransactionId),
                $"Transaction {leg.Id} is in {leg.TotalAmount.Currency} but its account is in "
                + $"{account.Balance.Currency}, so its effect on the balance cannot be re-applied.",
                ValidationText.UnvoidCurrencyMismatch(leg.TotalAmount.Currency.Value, account.Balance.Currency.Value)));
        }
    }
}
