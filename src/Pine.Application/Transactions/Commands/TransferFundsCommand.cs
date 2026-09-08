using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Pine.Application.Common;
using Pine.Core.Accounts;
using Pine.Core.Transactions;

namespace Pine.Application.Transactions.Commands;

public record TransferFundsCommand(
    Guid SourceAccountId,
    Guid DestinationAccountId,
    decimal Amount,
    string CurrencyCode,
    DateTimeOffset Date,
    string Notes) : IRequest, ITransactionalRequest;

public class TransferFundsCommandHandler : IRequestHandler<TransferFundsCommand>
{
    private readonly IAccountRepository _accountRepository;
    private readonly ITransactionRepository _transactionRepository;
    private readonly TimeProvider _timeProvider;

    public TransferFundsCommandHandler(
        IAccountRepository accountRepository,
        ITransactionRepository transactionRepository,
        TimeProvider timeProvider)
    {
        _accountRepository = accountRepository;
        _transactionRepository = transactionRepository;
        _timeProvider = timeProvider;
    }

    public async Task Handle(TransferFundsCommand request, CancellationToken cancellationToken)
    {
        // Distinct accounts, a positive amount, both accounts resolvable, and one shared currency are
        // all enforced by TransferFundsCommandValidator before this handler -- and before the pipeline
        // opens a database transaction. Only the null-coalescing below survives here, and only because
        // nullability flow analysis needs it; it is defence in depth, not the primary check.
        var sourceAccount = await _accountRepository.GetByIdAsync(request.SourceAccountId, cancellationToken)
                            ?? throw new InvalidOperationException("Source account not found.");

        var destAccount = await _accountRepository.GetByIdAsync(request.DestinationAccountId, cancellationToken)
                          ?? throw new InvalidOperationException("Destination account not found.");

        var currency = new Pine.Core.Common.ValueObjects.CurrencyCode(request.CurrencyCode);
        
        // The transferred amount, normalised once by Money's constructor. Both legs and both
        // balance movements are derived from THIS instance rather than from request.Amount, so the
        // four figures cannot disagree about the currency's scale.
        var transferAmount = new Pine.Core.Common.ValueObjects.Money(request.Amount, currency);

        // 1. Both legs, created together and already linked to each other. The domain owns the
        //    pairing (TD-26) so this handler has no way to produce a transfer whose two legs
        //    disagree about being a transfer -- which is what voiding one of them depends on.
        var (sourceTx, destTx) = Transaction.CreateTransferPair(
            sourceAccount.Id,
            destAccount.Id,
            request.Date,
            $"Transfer to {destAccount.Name}",
            $"Transfer from {sourceAccount.Name}",
            transferAmount,
            _timeProvider,
            request.Notes);

        // 2. Update account balances
        sourceAccount.Withdraw(transferAmount, _timeProvider);
        destAccount.Deposit(transferAmount, _timeProvider);

        // 3. Stage both legs and both balances. All four writes land in the single transaction
        //    opened by TransactionBehavior for this command, so a half-finished transfer -- money
        //    withdrawn but never deposited -- cannot be committed (TD-01).
        await _transactionRepository.SaveAsync(sourceTx, cancellationToken);
        await _transactionRepository.SaveAsync(destTx, cancellationToken);
        
        await _accountRepository.SaveAsync(sourceAccount, cancellationToken);
        await _accountRepository.SaveAsync(destAccount, cancellationToken);
    }
}

/// <summary>
/// Rejects a <see cref="TransferFundsCommand"/> that cannot be executed -- same account on both
/// sides, a non-positive amount, an account that does not exist, or a currency that does not match
/// both accounts -- before the pipeline opens a database transaction.
/// </summary>
/// <remarks>
/// <para>
/// WHY THESE CHECKS MOVED OUT OF THE HANDLER (TD-12). This handler was already the best-behaved of
/// the transaction commands: unlike <c>AddTransactionCommandHandler</c> it checked currencies up
/// front instead of letting <c>Money.Add</c> discover the mismatch after the write. That was the
/// right instinct in the wrong place. Keeping it in the handler meant the codebase had two idioms
/// for the same concern -- an up-front guard here, a late domain throw there -- and the guard still
/// ran inside the transaction the pipeline had already opened, and still threw exception types
/// (<see cref="InvalidOperationException"/>, <see cref="ArgumentException"/>) that carry no
/// structure for a UI to bind to. Folding them into a validator leaves ONE idiom: every pre-condition
/// on a transaction command is a <see cref="ValidationError"/> against a named property, produced
/// before any work begins.
/// </para>
/// <para>
/// WHY THE ERRORS ARE COLLECTED RATHER THAN THROWN ONE AT A TIME. A transfer form has five fields a
/// user can get wrong at once. Reporting the first problem, then the next on resubmit, turns one
/// correction into several round trips. The rules short-circuit only where a later rule genuinely has
/// nothing to work with: a missing account has no currency to compare, and a malformed currency code
/// is not a currency to compare against.
/// </para>
/// <para>
/// The normalisation before comparison is load-bearing for the same reason as in
/// <see cref="AddTransactionCommandValidator"/>: <c>CurrencyCode</c> trims and upper-cases, so
/// <c>"usd"</c> against a USD account is legitimate input and must not be reported as a mismatch.
/// Note that the old handler check compared the RAW string with <c>!=</c> and so rejected <c>"usd"</c>
/// outright -- this validator is deliberately more permissive there, matching what the domain
/// actually accepts.
/// </para>
/// </remarks>
public sealed class TransferFundsCommandValidator : IRequestValidator<TransferFundsCommand>
{
    private readonly IAccountRepository _accountRepository;

    /// <summary>
    /// Initialises the validator with the account repository used to resolve both sides of the transfer.
    /// </summary>
    /// <param name="accountRepository">Repository used to look up the source and destination accounts.</param>
    public TransferFundsCommandValidator(IAccountRepository accountRepository)
    {
        _accountRepository = accountRepository;
    }

    /// <summary>
    /// Checks the transfer's shape (distinct accounts, positive amount), that both accounts exist, and
    /// that the transfer currency is well formed and shared by both accounts.
    /// </summary>
    /// <param name="request">The command about to be handled.</param>
    /// <param name="cancellationToken">Token observed while loading both accounts.</param>
    /// <returns>
    /// An empty list when the transfer may proceed, otherwise every error found. A source-side and a
    /// destination-side currency mismatch are reported as two separate errors so the user can see
    /// which leg of the transfer is the problem -- often it is only one.
    /// </returns>
    public async Task<IReadOnlyList<ValidationError>> ValidateAsync(
        TransferFundsCommand request,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<ValidationError>();

        if (request.SourceAccountId == request.DestinationAccountId)
        {
            errors.Add(new ValidationError(
                nameof(TransferFundsCommand.SourceAccountId),
                "Source and destination accounts cannot be the same."));
        }

        if (request.Amount <= 0)
        {
            errors.Add(new ValidationError(
                nameof(TransferFundsCommand.Amount),
                "Transfer amount must be greater than zero."));
        }

        var sourceAccount = await _accountRepository.GetByIdAsync(request.SourceAccountId, cancellationToken);
        if (sourceAccount == null)
        {
            errors.Add(new ValidationError(
                nameof(TransferFundsCommand.SourceAccountId),
                $"Account {request.SourceAccountId} was not found."));
        }

        var destinationAccount = await _accountRepository.GetByIdAsync(request.DestinationAccountId, cancellationToken);
        if (destinationAccount == null)
        {
            errors.Add(new ValidationError(
                nameof(TransferFundsCommand.DestinationAccountId),
                $"Account {request.DestinationAccountId} was not found."));
        }

        // Nothing below can say anything useful without both accounts in hand.
        if (sourceAccount == null || destinationAccount == null)
        {
            return errors;
        }

        if (!CurrencyCodeText.TryNormalise(request.CurrencyCode, out var normalised))
        {
            errors.Add(new ValidationError(
                nameof(TransferFundsCommand.CurrencyCode),
                $"'{request.CurrencyCode}' is not a valid ISO 4217 currency code."));
            return errors;
        }

        var sourceCurrency = sourceAccount.Balance.Currency.Value;
        if (!string.Equals(normalised, sourceCurrency, StringComparison.Ordinal))
        {
            errors.Add(new ValidationError(
                nameof(TransferFundsCommand.CurrencyCode),
                $"Transfer currency {normalised} does not match source account currency {sourceCurrency}."));
        }

        var destinationCurrency = destinationAccount.Balance.Currency.Value;
        if (!string.Equals(normalised, destinationCurrency, StringComparison.Ordinal))
        {
            errors.Add(new ValidationError(
                nameof(TransferFundsCommand.CurrencyCode),
                $"Transfer currency {normalised} does not match destination account currency {destinationCurrency}."));
        }

        return errors;
    }
}
