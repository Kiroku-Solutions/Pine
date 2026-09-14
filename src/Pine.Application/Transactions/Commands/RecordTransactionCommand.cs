using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Pine.Application.Common;
using Pine.Core.Accounts;
using Pine.Core.Transactions;

using Pine.Core.Common.ValueObjects;

namespace Pine.Application.Transactions.Commands;

public record RecordTransactionCommand(Guid AccountId, DateTimeOffset Date, string Payee, decimal TotalAmount, string CurrencyCode, string Notes) : IRequest<Guid>, ITransactionalRequest;

public class RecordTransactionCommandHandler : IRequestHandler<RecordTransactionCommand, Guid>
{
    private readonly ITransactionRepository _transactionRepository;
    private readonly IAccountRepository _accountRepository;
    private readonly TimeProvider _timeProvider;

    public RecordTransactionCommandHandler(ITransactionRepository transactionRepository, IAccountRepository accountRepository, TimeProvider timeProvider)
    {
        _transactionRepository = transactionRepository;
        _accountRepository = accountRepository;
        _timeProvider = timeProvider;
    }

    public async Task<Guid> Handle(RecordTransactionCommand request, CancellationToken cancellationToken)
    {
        var account = await _accountRepository.GetByIdAsync(request.AccountId, cancellationToken);
        if (account == null)
            throw new InvalidOperationException($"Account with ID {request.AccountId} not found.");

        var currency = new CurrencyCode(request.CurrencyCode);
        var transactionAmount = new Money(request.TotalAmount, currency);

        var type = transactionAmount.Amount >= 0 ? Pine.Core.Transactions.TransactionType.Income : Pine.Core.Transactions.TransactionType.Expense;
        var transaction = Transaction.Create(request.AccountId, request.Date, request.Payee, transactionAmount, type, _timeProvider, request.Notes);
        
        // Update account balance
        var absoluteAmount = new Money(Math.Abs(request.TotalAmount), currency);
        if (request.TotalAmount < 0)
        {
            account.Withdraw(absoluteAmount, _timeProvider);
        }
        else
        {
            account.Deposit(absoluteAmount, _timeProvider);
        }

        // The transaction and the account balance it moves are staged on the same scoped
        // DbContext; TransactionBehavior commits them together, or not at all (TD-01).
        await _transactionRepository.SaveAsync(transaction, cancellationToken);
        await _accountRepository.SaveAsync(account, cancellationToken);
        
        return transaction.Id;
    }
}

/// <summary>
/// Rejects a <see cref="RecordTransactionCommand"/> whose currency can never be applied to the target
/// account, before the pipeline opens a database transaction and before a single row is written.
/// </summary>
/// <remarks>
/// <para>
/// SAME DEFECT, SAME SHAPE AS TD-12. This handler orders its writes more carefully than
/// <c>AddTransactionCommandHandler</c> -- it moves the balance before saving -- but the failure mode
/// is identical: a currency the account does not use is discovered inside <c>Money.Add</c>, and the
/// domain invariant's own wording ("Cannot add money of different currencies: USD and EUR") is what
/// reaches the user. Nothing is persisted, because <c>TransactionBehavior</c> rolls the command back;
/// what is wrong is the layer, the wording, and the fact that a database transaction was opened at
/// all for a command that could have been rejected on inspection.
/// </para>
/// <para>
/// The rules and their order match <see cref="AddTransactionCommandValidator"/> deliberately. Two
/// commands that fail for the same reason should fail with the same message, and one idiom that
/// appears twice is cheaper to keep correct than two idioms that agree by accident. See that type's
/// remarks for why the ISO 4217 shape check is hand-rolled and why normalising before comparing is
/// load-bearing.
/// </para>
/// </remarks>
public sealed class RecordTransactionCommandValidator : IRequestValidator<RecordTransactionCommand>
{
    private readonly IAccountRepository _accountRepository;

    /// <summary>
    /// Initialises the validator with the account repository used to resolve the target account.
    /// </summary>
    /// <param name="accountRepository">Repository used to look up the account being posted to.</param>
    public RecordTransactionCommandValidator(IAccountRepository accountRepository)
    {
        _accountRepository = accountRepository;
    }

    /// <summary>
    /// Checks that the target account exists and that the requested currency is both well formed and
    /// the account's own currency.
    /// </summary>
    /// <param name="request">The command about to be handled.</param>
    /// <param name="cancellationToken">Token observed while loading the account.</param>
    /// <returns>
    /// An empty list when the command may proceed, otherwise the errors that stop it. The rules
    /// short-circuit: a missing account leaves nothing to compare a currency against, and a malformed
    /// currency leaves nothing meaningful to compare.
    /// </returns>
    public async Task<IReadOnlyList<ValidationError>> ValidateAsync(
        RecordTransactionCommand request,
        CancellationToken cancellationToken = default)
    {
        var account = await _accountRepository.GetByIdAsync(request.AccountId, cancellationToken);
        if (account == null)
        {
            return new[]
            {
                new ValidationError(
                    nameof(RecordTransactionCommand.AccountId),
                    $"Account {request.AccountId} was not found.",
                    ValidationText.AccountNotFound()),
            };
        }

        if (!CurrencyCodeText.TryNormalise(request.CurrencyCode, out var normalised))
        {
            return new[]
            {
                new ValidationError(
                    nameof(RecordTransactionCommand.CurrencyCode),
                    $"'{request.CurrencyCode}' is not a valid ISO 4217 currency code.",
                    ValidationText.CurrencyCodeInvalid(request.CurrencyCode)),
            };
        }

        var accountCurrency = account.Balance.Currency.Value;
        if (!string.Equals(normalised, accountCurrency, StringComparison.Ordinal))
        {
            return new[]
            {
                new ValidationError(
                    nameof(RecordTransactionCommand.CurrencyCode),
                    $"Transaction currency {normalised} does not match account currency {accountCurrency}.",
                    ValidationText.CurrencyMismatch(normalised, accountCurrency)),
            };
        }

        return Array.Empty<ValidationError>();
    }
}
