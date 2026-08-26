using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
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
) : IRequest<Guid>;

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
