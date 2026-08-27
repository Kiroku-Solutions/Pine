using System;
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
