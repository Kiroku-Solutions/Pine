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

public record AddTransactionLineItemCommand(
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal TotalPrice
);

public record AddTransactionCommand(
    Guid AccountId,
    DateTimeOffset Date,
    string Payee,
    decimal Amount,
    string Currency,
    string Notes,
    string Category,
    System.Collections.Generic.IReadOnlyList<AddTransactionLineItemCommand>? LineItems = null
) : IRequest<Guid>, ITransactionalRequest;

public class AddTransactionCommandHandler : IRequestHandler<AddTransactionCommand, Guid>
{
    private readonly ITransactionRepository _transactionRepository;
    private readonly IAccountRepository _accountRepository;
    private readonly TimeProvider _timeProvider;

    public AddTransactionCommandHandler(ITransactionRepository transactionRepository, IAccountRepository accountRepository, TimeProvider timeProvider)
    {
        _transactionRepository = transactionRepository;
        _accountRepository = accountRepository;
        _timeProvider = timeProvider;
    }

    public async Task<Guid> Handle(AddTransactionCommand request, CancellationToken cancellationToken)
    {
        var account = await _accountRepository.GetByIdAsync(request.AccountId, cancellationToken);
        if (account == null)
            throw new ArgumentException("Account not found.", nameof(request));

        // Note: For income the amount is positive, for expense it should be negative.
        // We'll trust the UI/command passes the signed decimal.
        var moneyAmount = new Money(request.Amount, new CurrencyCode(request.Currency));
        
        var type = moneyAmount.Amount >= 0 ? Pine.Core.Transactions.TransactionType.Income : Pine.Core.Transactions.TransactionType.Expense;
        var transaction = Transaction.Create(
            request.AccountId, 
            request.Date, 
            request.Payee, 
            moneyAmount, 
            type,
            _timeProvider, 
            request.Notes);

        // Add a single split for the whole amount to the specified category
        transaction.AddSplit(moneyAmount, request.Category, request.Notes);

        if (request.LineItems != null && request.LineItems.Count > 0)
        {
            foreach (var li in request.LineItems)
            {
                // In receipts, the line item prices are usually positive.
                // If the transaction is an expense, should the line item totals be negative? 
                // Line items are usually just recorded as positive values.
                transaction.AddLineItem(
                    li.Description, 
                    li.Quantity, 
                    new Money(li.UnitPrice, moneyAmount.Currency), 
                    new Money(li.TotalPrice, moneyAmount.Currency));
            }
        }

        await _transactionRepository.SaveAsync(transaction, cancellationToken);
        
        // Also update account balance. In a real CQRS system, this might be handled 
        // by listening to the TransactionRegisteredEvent, but for simplicity we do it here.
        if (moneyAmount.Amount >= 0)
        {
            account.Deposit(moneyAmount, _timeProvider);
        }
        else
        {
            account.Withdraw(new Money(-moneyAmount.Amount, moneyAmount.Currency), _timeProvider);
        }
        
        await _accountRepository.SaveAsync(account, cancellationToken);
        
        return transaction.Id;
    }
}

/// <summary>
/// Rejects an <see cref="AddTransactionCommand"/> whose currency can never be applied to the target
/// account, before the pipeline opens a database transaction and before a single row is written.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS IS A VALIDATOR AND NOT A HANDLER CHECK (TD-12). The handler builds the
/// <see cref="Money"/>, creates the transaction, saves it, and only THEN calls
/// <c>Account.Deposit</c>/<c>Account.Withdraw</c>. On a currency mismatch the failure therefore
/// surfaces from deep inside <c>Money.Add</c> as
/// <c>InvalidOperationException("Cannot add money of different currencies: USD and EUR")</c>. That
/// is not a data-corruption bug -- <c>TransactionBehavior</c> rolls the whole command back, so
/// nothing is persisted -- but it is a failure at the wrong layer: a domain invariant message,
/// phrased for the domain, escapes all the way to the view model, which renders it verbatim. The
/// user is told about "money of different currencies" when what they need to be told is which field
/// they got wrong and what the account actually expects.
/// </para>
/// <para>
/// Running the check here rather than at the top of the handler also means a rejected command never
/// opens a transaction at all: <c>ValidationBehavior</c> sits OUTSIDE <c>TransactionBehavior</c>, so
/// the failure is a rejection, not a rollback. That distinction matters for the vault, which is a
/// single-writer SQLCipher file -- work that is never begun cannot contend with work that is.
/// </para>
/// <para>
/// WHY THE SHAPE CHECK IS HAND-ROLLED. Validating the currency by simply constructing a
/// <see cref="CurrencyCode"/> and catching the failure would reintroduce exactly the leak being
/// removed: its constructor throws <see cref="ArgumentException"/> with a domain-phrased message.
/// <see cref="CurrencyCodeText.TryNormalise"/> applies the same rule that constructor applies
/// (trim, upper-case, then exactly three A-Z characters) without throwing. It is shared by all
/// three transaction validators rather than copied into each, and its own remarks record that
/// duplicating a domain rule outside the domain is debt (TD-61).
/// </para>
/// <para>
/// WHY THE NORMALISATION IS LOAD-BEARING. <see cref="CurrencyCode"/> trims and upper-cases its
/// input, so a command carrying <c>"usd"</c> against a USD account is accepted by the handler today
/// and must keep being accepted. Comparing the raw string would turn a working case into a reported
/// mismatch -- a regression dressed up as a validation improvement.
/// </para>
/// </remarks>
public sealed class AddTransactionCommandValidator : IRequestValidator<AddTransactionCommand>
{
    private readonly IAccountRepository _accountRepository;

    /// <summary>
    /// Initialises the validator with the account repository used to resolve the target account.
    /// </summary>
    /// <param name="accountRepository">Repository used to look up the account being posted to.</param>
    public AddTransactionCommandValidator(IAccountRepository accountRepository)
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
    /// An empty list when the command may proceed, otherwise the errors that stop it. The rules are
    /// ordered and short-circuit deliberately: with no account there is nothing to compare a currency
    /// against, and with a malformed currency there is nothing meaningful to compare. Reporting a
    /// "mismatch" derived from a value that is not a currency at all would be noise, not help.
    /// </returns>
    public async Task<IReadOnlyList<ValidationError>> ValidateAsync(
        AddTransactionCommand request,
        CancellationToken cancellationToken = default)
    {
        var account = await _accountRepository.GetByIdAsync(request.AccountId, cancellationToken);
        if (account == null)
        {
            return new[]
            {
                new ValidationError(
                    nameof(AddTransactionCommand.AccountId),
                    $"Account {request.AccountId} was not found."),
            };
        }

        if (!CurrencyCodeText.TryNormalise(request.Currency, out var normalised))
        {
            return new[]
            {
                new ValidationError(
                    nameof(AddTransactionCommand.Currency),
                    $"'{request.Currency}' is not a valid ISO 4217 currency code."),
            };
        }

        var accountCurrency = account.Balance.Currency.Value;
        if (!string.Equals(normalised, accountCurrency, StringComparison.Ordinal))
        {
            return new[]
            {
                new ValidationError(
                    nameof(AddTransactionCommand.Currency),
                    $"Transaction currency {normalised} does not match account currency {accountCurrency}."),
            };
        }

        return Array.Empty<ValidationError>();
    }
}
