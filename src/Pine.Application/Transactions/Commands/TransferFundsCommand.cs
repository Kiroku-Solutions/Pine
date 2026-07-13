using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Pine.Core.Accounts;
using Pine.Core.Transactions;

namespace Pine.Application.Transactions.Commands;

public record TransferFundsCommand(
    Guid SourceAccountId,
    Guid DestinationAccountId,
    decimal Amount,
    string CurrencyCode,
    DateTimeOffset Date,
    string Notes) : IRequest;

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
        if (request.SourceAccountId == request.DestinationAccountId)
        {
            throw new InvalidOperationException("Source and Destination accounts cannot be the same.");
        }

        if (request.Amount <= 0)
        {
            throw new ArgumentException("Transfer amount must be greater than zero.");
        }

        var sourceAccount = await _accountRepository.GetByIdAsync(request.SourceAccountId, cancellationToken)
                            ?? throw new InvalidOperationException("Source account not found.");
                            
        var destAccount = await _accountRepository.GetByIdAsync(request.DestinationAccountId, cancellationToken)
                          ?? throw new InvalidOperationException("Destination account not found.");

        // Security Validation: Only same currency allowed for now
        if (sourceAccount.Balance.Currency.Value != request.CurrencyCode || destAccount.Balance.Currency.Value != request.CurrencyCode)
        {
            throw new InvalidOperationException("Both accounts must have the same currency as the transfer amount.");
        }

        var currency = new Pine.Core.Common.ValueObjects.CurrencyCode(request.CurrencyCode);
        
        // 1. Create Outflow (Expense)
        var outflowMoney = new Pine.Core.Common.ValueObjects.Money(-request.Amount, currency);
        var sourceTx = Transaction.Create(
            sourceAccount.Id,
            request.Date,
            $"Transfer to {destAccount.Name}",
            outflowMoney,
            TransactionType.Transfer,
            _timeProvider,
            request.Notes
            // LinkedTransactionId will be set via reflection or we can pass null and update later, 
            // but since constructor creates Id, we can create both and link them.
        );

        // 2. Create Inflow (Income)
        var inflowMoney = new Pine.Core.Common.ValueObjects.Money(request.Amount, currency);
        var destTx = Transaction.Create(
            destAccount.Id,
            request.Date,
            $"Transfer from {sourceAccount.Name}",
            inflowMoney,
            TransactionType.Transfer,
            _timeProvider,
            request.Notes,
            sourceTx.Id // Link destination to source
        );
        
        // Wait, source needs to link to destination as well. 
        // Since constructor doesn't allow setting Id manually, we must use a workaround or just link one way.
        // Or we use reflection to set LinkedTransactionId.
        typeof(Transaction).GetProperty(nameof(Transaction.LinkedTransactionId))!
            .SetValue(sourceTx, destTx.Id);

        // 3. Update account balances
        sourceAccount.Withdraw(new Pine.Core.Common.ValueObjects.Money(request.Amount, currency), _timeProvider);
        destAccount.Deposit(new Pine.Core.Common.ValueObjects.Money(request.Amount, currency), _timeProvider);

        // 4. Save everything (in a real DB, these should be in a unit of work or transaction scope)
        await _transactionRepository.SaveAsync(sourceTx, cancellationToken);
        await _transactionRepository.SaveAsync(destTx, cancellationToken);
        
        await _accountRepository.SaveAsync(sourceAccount, cancellationToken);
        await _accountRepository.SaveAsync(destAccount, cancellationToken);
    }
}
