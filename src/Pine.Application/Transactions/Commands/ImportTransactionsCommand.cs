using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Pine.Core.Importing;
using Pine.Core.Transactions;
using Pine.Core.Accounts;

namespace Pine.Application.Transactions.Commands;

public record ImportTransactionsCommand(
    Guid TargetAccountId,
    Stream FileStream,
    IImporter Importer,
    ImportProfile Profile
) : IRequest<IReadOnlyList<Transaction>>;

public sealed class ImportTransactionsCommandHandler : IRequestHandler<ImportTransactionsCommand, IReadOnlyList<Transaction>>
{
    private readonly ITransactionRepository _transactionRepository;
    private readonly IAccountRepository _accountRepository;
    private readonly TimeProvider _timeProvider;

    public ImportTransactionsCommandHandler(ITransactionRepository transactionRepository, IAccountRepository accountRepository, TimeProvider timeProvider)
    {
        _transactionRepository = transactionRepository;
        _accountRepository = accountRepository;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<Transaction>> Handle(ImportTransactionsCommand request, CancellationToken cancellationToken)
    {
        // 1. Verify account exists
        var account = await _accountRepository.GetByIdAsync(request.TargetAccountId, cancellationToken);
        if (account == null)
            throw new ArgumentException("Target account not found.");

        // 2. Parse transactions from file
        var transactions = await request.Importer.ImportAsync(request.TargetAccountId, request.FileStream, request.Profile, cancellationToken);
        
        // 3. Save each transaction and update account balance
        // Note: For large imports, a bulk insert or unit of work would be more efficient, 
        // but for now we reuse the domain logic per transaction.
        foreach (var tx in transactions)
        {
            await _transactionRepository.SaveAsync(tx, cancellationToken);
            
            var absoluteAmount = new Pine.Core.Common.ValueObjects.Money(Math.Abs(tx.TotalAmount.Amount), tx.TotalAmount.Currency);
            if (tx.TotalAmount.Amount < 0)
            {
                account.Withdraw(absoluteAmount, _timeProvider);
            }
            else
            {
                account.Deposit(absoluteAmount, _timeProvider);
            }
        }

        await _accountRepository.SaveAsync(account, cancellationToken);
        
        return transactions;
    }
}
