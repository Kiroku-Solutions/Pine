using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Pine.Application.Common;
using Pine.Core.Accounts;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Transactions;

namespace Pine.Application.Transactions.Commands;

/// <summary>
/// One split to leave on an edited transaction. Carries the same sign convention as
/// <see cref="UpdateTransactionCommand.Amount"/>, exactly as <see cref="SplitDefinition"/> does.
/// </summary>
/// <param name="Id">
/// The split's existing <see cref="Core.Common.Entity.Id"/>, or <see langword="null"/> for a split
/// being added by this edit. Passed straight through to <see cref="SplitDefinition"/>, so a split
/// named here keeps its identity (TD-60) instead of being deleted and re-inserted under a new id.
/// </param>
/// <param name="Amount">The split's signed amount.</param>
/// <param name="Category">The category label.</param>
/// <param name="Notes">Free text for this split.</param>
public sealed record UpdateTransactionSplitCommand(
    Guid? Id,
    decimal Amount,
    string Category,
    string Notes = "");

/// <summary>
/// Edits a transaction's date, payee, amount, currency, notes and splits in place, adjusting its
/// account's balance by the difference -- without voiding it and re-entering it as a new row (TD-68,
/// closing the edit half TD-02 deferred).
/// </summary>
/// <remarks>
/// <para>
/// WHAT THIS DOES NOT TOUCH. <see cref="AccountId"/> is immutable: moving a transaction to a
/// different account is a different operation (it would need to move the balance effect across two
/// accounts the way a transfer does), and nothing asked for it. <see cref="Transaction.ImportKey"/>
/// is untouched for the same reason <c>VoidTransactionCommand</c> leaves it alone -- it answers
/// "was this external row already imported?", and editing the transaction does not change where it
/// came from. Both are exactly what <c>NaturalKeys.TransactionKey</c> is built from
/// (<c>AccountId</c> + <c>ImportKey</c>), so this command can never change a transaction's natural
/// key -- a synced edit stays recognisable as an edit of the same row on a peer, never as a new one.
/// </para>
/// <para>
/// <see cref="Splits"/> is always the FULL set, exactly like <see cref="Transaction.ReplaceSplits"/>
/// it is handed to -- anything not named here is gone. An empty list clears the splits, which is a
/// valid state, same as it is on create.
/// </para>
/// </remarks>
/// <param name="TransactionId">The transaction being edited.</param>
/// <param name="Date">The new date.</param>
/// <param name="Payee">The new payee.</param>
/// <param name="Amount">The new signed total amount.</param>
/// <param name="Currency">The new ISO 4217 currency code; must match the account's own currency.</param>
/// <param name="Notes">The new notes.</param>
/// <param name="Splits">The full replacement split set.</param>
public sealed record UpdateTransactionCommand(
    Guid TransactionId,
    DateTimeOffset Date,
    string Payee,
    decimal Amount,
    string Currency,
    string Notes,
    IReadOnlyList<UpdateTransactionSplitCommand> Splits
) : IRequest<Guid>, ITransactionalRequest;

/// <summary>
/// Handles <see cref="UpdateTransactionCommand"/>.
/// </summary>
/// <remarks>
/// <para>
/// THE BALANCE MOVE IS A REVERSE-THEN-APPLY, IN ONE DATABASE TRANSACTION. This mirrors
/// <c>VoidTransactionCommandHandler</c>'s own reasoning exactly: the old amount's effect on the
/// account is undone with the same incremental mutators (<c>Deposit</c>/<c>Withdraw</c>) the write
/// path used to apply it, and the new amount's effect is applied the same way
/// <c>AddTransactionCommandHandler</c> applies a fresh one. Both the transaction row and the account
/// row are saved inside the one <see cref="ITransactionalRequest"/> boundary <c>TransactionBehavior</c>
/// opens, so a failure partway through (the account write, in particular -- see
/// <c>UpdateTransactionAtomicityTests</c>) leaves neither the edited row nor the moved balance behind.
/// </para>
/// <para>
/// BOTH READS HAPPEN BEFORE EITHER WRITE. The old amount is captured from the transaction BEFORE
/// <see cref="Transaction.Update"/> mutates it in place -- there is no second copy of "what it used to
/// be" anywhere else to reverse against.
/// </para>
/// </remarks>
public sealed class UpdateTransactionCommandHandler : IRequestHandler<UpdateTransactionCommand, Guid>
{
    private readonly ITransactionRepository _transactionRepository;
    private readonly IAccountRepository _accountRepository;
    private readonly TimeProvider _timeProvider;

    public UpdateTransactionCommandHandler(
        ITransactionRepository transactionRepository,
        IAccountRepository accountRepository,
        TimeProvider timeProvider)
    {
        _transactionRepository = transactionRepository;
        _accountRepository = accountRepository;
        _timeProvider = timeProvider;
    }

    public async Task<Guid> Handle(UpdateTransactionCommand request, CancellationToken cancellationToken)
    {
        // Existence, "not voided", "not a transfer leg" and currency agreement are all established
        // by UpdateTransactionCommandValidator, outside this transaction. These are
        // nullability-flow defence in depth, not the primary guard -- same posture as
        // VoidTransactionCommandHandler.
        var transaction = await _transactionRepository.GetByIdAsync(request.TransactionId, cancellationToken)
                          ?? throw new InvalidOperationException("Transaction not found.");

        var account = await _accountRepository.GetByIdAsync(transaction.AccountId, cancellationToken)
                     ?? throw new InvalidOperationException($"Account {transaction.AccountId} for transaction {transaction.Id} was not found.");

        // Captured before Update mutates the aggregate in place.
        var oldAmount = transaction.TotalAmount;

        var currency = new CurrencyCode(request.Currency);
        var newAmount = new Money(request.Amount, currency);

        transaction.Update(request.Date, request.Payee, newAmount, request.Notes, _timeProvider);

        transaction.ReplaceSplits(request.Splits.Select(split =>
            new SplitDefinition(new Money(split.Amount, currency), split.Category, split.Notes ?? string.Empty, split.Id ?? Guid.Empty)));

        // If the transaction carries receipt line items and the edit changed its total amount,
        // keep the TD-19 line-item reconciliation invariant intact instead of throwing at SaveAsync.
        if (transaction.LineItems.Count > 0 && !transaction.HasLineItemsPredatingKinds)
        {
            var flippedSign = (oldAmount.Amount < 0m && newAmount.Amount > 0m)
                || (oldAmount.Amount > 0m && newAmount.Amount < 0m);

            if (flippedSign)
            {
                transaction.ReplaceLineItems(Array.Empty<LineItemDefinition>());
            }
            else
            {
                var diff = transaction.LineItemDifference();
                if (diff.Amount != 0m)
                {
                    var existing = transaction.LineItems.ToList();
                    var adjustmentIndex = existing.FindLastIndex(li => li.Kind == LineItemKind.Unattributed);
                    var updated = new List<LineItemDefinition>(existing.Count + 1);

                    for (var i = 0; i < existing.Count; i++)
                    {
                        var li = existing[i];
                        if (i == adjustmentIndex)
                        {
                            var adjustedTotal = li.TotalPrice + diff;
                            if (adjustedTotal.Amount != 0m)
                            {
                                updated.Add(new LineItemDefinition(li.Description, 1m, adjustedTotal, adjustedTotal, LineItemKind.Unattributed, li.Id));
                            }
                        }
                        else
                        {
                            updated.Add(new LineItemDefinition(li.Description, li.Quantity, li.UnitPrice, li.TotalPrice, li.Kind, li.Id));
                        }
                    }

                    if (adjustmentIndex < 0)
                    {
                        updated.Add(new LineItemDefinition("Adjustment", 1m, diff, diff, LineItemKind.Unattributed));
                    }

                    transaction.ReplaceLineItems(updated);
                }
            }
        }

        await _transactionRepository.SaveAsync(transaction, cancellationToken);

        ReverseBalanceEffect(account, oldAmount);
        ApplyBalanceEffect(account, newAmount);

        await _accountRepository.SaveAsync(account, cancellationToken);

        return transaction.Id;
    }

    /// <summary>Undoes the transaction's OLD contribution to its account's balance. Mirrors
    /// <c>VoidTransactionCommandHandler.ReverseBalanceEffect</c> exactly.</summary>
    private void ReverseBalanceEffect(Account account, Money amount)
    {
        if (amount.Amount > 0)
        {
            account.Withdraw(amount, _timeProvider);
        }
        else if (amount.Amount < 0)
        {
            account.Deposit(-amount, _timeProvider);
        }
    }

    /// <summary>Applies the transaction's NEW contribution to its account's balance. Mirrors
    /// <c>AddTransactionCommandHandler</c>'s own deposit/withdraw choice.</summary>
    private void ApplyBalanceEffect(Account account, Money amount)
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
/// Rejects an <see cref="UpdateTransactionCommand"/> that cannot be carried out, before the pipeline
/// opens a database transaction and before anything is written.
/// </summary>
/// <remarks>
/// Every rule here has a domain throw behind it that would otherwise reach the view model verbatim --
/// the same TD-12/TD-24 wrong-layer failure class <c>AddTransactionCommandValidator</c> and
/// <c>VoidTransactionCommandValidator</c> both close for their own paths.
/// <see cref="Transaction.Update"/> throws <c>InvalidOperationException</c> for a voided transaction
/// or a transfer leg; <see cref="Transaction.ReplaceSplits"/> throws for a split set that does not sum
/// to the total. None of those sentences names a field a form can highlight; this validator does.
/// </remarks>
public sealed class UpdateTransactionCommandValidator : IRequestValidator<UpdateTransactionCommand>
{
    private readonly ITransactionRepository _transactionRepository;
    private readonly IAccountRepository _accountRepository;
    private readonly TimeProvider _timeProvider;

    public UpdateTransactionCommandValidator(
        ITransactionRepository transactionRepository,
        IAccountRepository accountRepository,
        TimeProvider? timeProvider = null)
    {
        _transactionRepository = transactionRepository;
        _accountRepository = accountRepository;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<IReadOnlyList<ValidationError>> ValidateAsync(
        UpdateTransactionCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new List<ValidationError>();

        var transaction = await _transactionRepository.GetByIdAsync(request.TransactionId, cancellationToken);
        if (transaction is null)
        {
            // Nothing below can be evaluated without the transaction, so this returns rather than
            // accumulating -- same posture as VoidTransactionCommandValidator.
            errors.Add(new ValidationError(
                nameof(UpdateTransactionCommand.TransactionId),
                $"Transaction {request.TransactionId} was not found.",
                ValidationText.TransactionNotFound()));
            return errors;
        }

        // THE VOID REFUSAL (TD-68 decision point 4). Void's own contract is a cancellation that
        // deliberately keeps the row's last true values; editing one would rewrite the thing the
        // void preserved, through a door with nothing to do with TD-69 (unvoid). Refused cleanly,
        // here, rather than left to Transaction.Update's domain-phrased throw.
        if (transaction.IsVoided)
        {
            errors.Add(new ValidationError(
                nameof(UpdateTransactionCommand.TransactionId),
                "This transaction has been voided and cannot be edited. Unvoid it first.",
                ValidationText.TransactionVoided()));
            return errors;
        }

        // A transfer leg is refused rather than edited in isolation -- see the remarks on
        // Transaction.Update for why.
        if (transaction.Type == TransactionType.Transfer)
        {
            errors.Add(new ValidationError(
                nameof(UpdateTransactionCommand.TransactionId),
                "A transfer leg cannot be edited directly.",
                ValidationText.TransactionIsTransferLeg()));
            return errors;
        }

        var account = await _accountRepository.GetByIdAsync(transaction.AccountId, cancellationToken);
        if (account is null)
        {
            errors.Add(new ValidationError(
                nameof(UpdateTransactionCommand.TransactionId),
                $"The account for transaction {transaction.Id} was not found.",
                ValidationText.AccountNotFound()));
            return errors;
        }

        if (request.Amount == 0m)
        {
            errors.Add(new ValidationError(
                nameof(UpdateTransactionCommand.Amount),
                "Transaction amount cannot be zero.",
                ValidationText.TransactionAmountZero()));
        }

        if (request.Date > _timeProvider.GetUtcNow().AddDays(1))
        {
            errors.Add(new ValidationError(
                nameof(UpdateTransactionCommand.Date),
                "Transaction date cannot be in the future.",
                ValidationText.TransactionDateInFuture()));
        }

        if (!CurrencyCodeText.TryNormalise(request.Currency, out var normalised))
        {
            errors.Add(new ValidationError(
                nameof(UpdateTransactionCommand.Currency),
                $"'{request.Currency}' is not a valid ISO 4217 currency code.",
                ValidationText.CurrencyCodeInvalid(request.Currency)));
            return errors;
        }

        var accountCurrency = account.Balance.Currency.Value;
        if (!string.Equals(normalised, accountCurrency, StringComparison.Ordinal))
        {
            errors.Add(new ValidationError(
                nameof(UpdateTransactionCommand.Currency),
                $"Transaction currency {normalised} does not match account currency {accountCurrency}.",
                ValidationText.CurrencyMismatch(normalised, accountCurrency)));
            return errors;
        }

        // TD-19/TD-68, at the layer that can phrase it for a human -- shared arithmetic with
        // AddTransactionCommandValidator's line-item check via AmountSumCheck, not a second copy of
        // it. The aggregate's ReplaceSplits enforces the same rule at the persistence boundary as
        // the backstop.
        if (request.Splits is { Count: > 0 })
        {
            var currency = new CurrencyCode(normalised);
            if (!AmountSumCheck.Matches(request.Splits.Select(s => s.Amount), request.Amount, currency, out var splitSum, out var total))
            {
                var difference = total - splitSum;
                errors.Add(new ValidationError(
                    nameof(UpdateTransactionCommand.Splits),
                    $"The splits add up to {splitSum.Amount} but the transaction total is {total.Amount}. " +
                    $"The {difference.Amount} difference must be recorded.",
                    ValidationText.SplitsDoNotAddUp(splitSum.Amount, total.Amount, difference.Amount)));
            }
        }

        return errors;
    }
}
