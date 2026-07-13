using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Pine.Core.Importing;
using Pine.Core.Transactions;
using Pine.Core.Accounts;
using Pine.Core.Ai;
using Pine.Core.Categories;

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
    private readonly IAIProvider _aiProvider;
    private readonly ICategoryRepository _categoryRepository;

    public ImportTransactionsCommandHandler(
        ITransactionRepository transactionRepository, 
        IAccountRepository accountRepository, 
        TimeProvider timeProvider,
        IAIProvider aiProvider,
        ICategoryRepository categoryRepository)
    {
        _transactionRepository = transactionRepository;
        _accountRepository = accountRepository;
        _timeProvider = timeProvider;
        _aiProvider = aiProvider;
        _categoryRepository = categoryRepository;
    }

    public async Task<IReadOnlyList<Transaction>> Handle(ImportTransactionsCommand request, CancellationToken cancellationToken)
    {
        // 1. Verify account exists
        var account = await _accountRepository.GetByIdAsync(request.TargetAccountId, cancellationToken);
        if (account == null)
            throw new ArgumentException("Target account not found.");

        // 2. Parse transactions from file
        var transactions = await request.Importer.ImportAsync(account, request.FileStream, request.Profile, cancellationToken);
        
        // 3. Deduplication logic
        var existingTransactions = await _transactionRepository.GetByAccountIdAsync(request.TargetAccountId, cancellationToken);
        var importedTransactions = new List<Transaction>();

        foreach (var tx in transactions)
        {
            bool isDuplicate = existingTransactions.Any(existing => 
                existing.Date.Date == tx.Date.Date && 
                existing.TotalAmount.Amount == tx.TotalAmount.Amount);

            if (!isDuplicate)
            {
                importedTransactions.Add(tx);
            }
        }
        
        // 4. Save each unique transaction and update account balance
        foreach (var tx in importedTransactions)
        {
            // Apply AI Categorization if no category was provided by the CSV
            if (tx.Splits.Count == 0)
            {
                string transactionText = $"{tx.Payee} {tx.Notes}".Trim();
                var aiResult = await _aiProvider.CategorizeTransactionAsync(transactionText, tx.TotalAmount.Amount, cancellationToken);
                
                if (aiResult.CategoryId != Guid.Empty && aiResult.ConfidenceScore >= 0.3)
                {
                    var category = await _categoryRepository.GetByIdAsync(aiResult.CategoryId, cancellationToken);
                    if (category != null)
                    {
                        tx.AddSplit(tx.TotalAmount, category.Name, "AI");
                    }
                }
            }

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
        
        return importedTransactions;
    }
}
