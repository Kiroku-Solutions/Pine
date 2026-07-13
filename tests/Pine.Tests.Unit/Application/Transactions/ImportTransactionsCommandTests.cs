using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using Pine.Application.Transactions.Commands;
using Pine.Core.Accounts;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Importing;
using Pine.Core.Transactions;
using Pine.Core.Categories;
using Pine.Core.Ai;
using Xunit;

namespace Pine.Tests.Unit.Application.Transactions;

public class ImportTransactionsCommandTests
{
    [Fact]
    public async Task Handle_CallsImporterAndReturnsTransactions()
    {
        // Arrange
        var mockImporter = Substitute.For<IImporter>();
        var expectedTransactions = new List<Transaction>();
        
        mockImporter.ImportAsync(Arg.Any<Account>(), Arg.Any<Stream>(), Arg.Any<ImportProfile>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult((IReadOnlyList<Transaction>)expectedTransactions));

        var mockAccountRepo = Substitute.For<IAccountRepository>();
        mockAccountRepo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Account.Create("Test", AccountType.Bank, new Money(0, new CurrencyCode("USD")), TimeProvider.System));

        var mockAiProvider = Substitute.For<IAIProvider>();
        mockAiProvider.CategorizeTransactionAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new CategorisationResult(Guid.Empty, 0.0)));

        var handler = new ImportTransactionsCommandHandler(
            Substitute.For<ITransactionRepository>(), 
            mockAccountRepo, 
            TimeProvider.System,
            mockAiProvider,
            Substitute.For<ICategoryRepository>());
            
        var profile = ImportProfile.Create("Test Profile", TimeProvider.System);
        var command = new ImportTransactionsCommand(Guid.NewGuid(), new MemoryStream(), mockImporter, profile);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.Equal(expectedTransactions, result);
        await mockImporter.Received(1).ImportAsync(Arg.Any<Account>(), command.FileStream, profile, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithDuplicates_SkipsDuplicates()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var date = DateTimeOffset.UtcNow;
        var amount = new Money(100, new CurrencyCode("USD"));
        
        var mockImporter = Substitute.For<IImporter>();
        
        // Imported contains 2 transactions (one duplicate, one new)
        var newTx = Transaction.Create(accountId, date.AddDays(1), "New Payee", amount, TimeProvider.System);
        var dupTx = Transaction.Create(accountId, date, "Old Payee", amount, TimeProvider.System);
        
        mockImporter.ImportAsync(Arg.Any<Account>(), Arg.Any<Stream>(), Arg.Any<ImportProfile>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult((IReadOnlyList<Transaction>)new List<Transaction> { newTx, dupTx }));

        var mockAccountRepo = Substitute.For<IAccountRepository>();
        mockAccountRepo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Account.Create("Test", AccountType.Bank, new Money(0, new CurrencyCode("USD")), TimeProvider.System));

        var mockTransactionRepo = Substitute.For<ITransactionRepository>();
        // Return existing transaction that matches dupTx (same Date and Amount)
        mockTransactionRepo.GetByAccountIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult((IEnumerable<Transaction>)new List<Transaction> { dupTx }));

        var mockAiProvider = Substitute.For<IAIProvider>();
        mockAiProvider.CategorizeTransactionAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new CategorisationResult(Guid.Empty, 0.0)));

        var handler = new ImportTransactionsCommandHandler(
            mockTransactionRepo, 
            mockAccountRepo, 
            TimeProvider.System,
            mockAiProvider,
            Substitute.For<ICategoryRepository>());
            
        var command = new ImportTransactionsCommand(accountId, new MemoryStream(), mockImporter, ImportProfile.Create("Test", TimeProvider.System));

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.Single(result); // Only the newTx should be imported
        Assert.Equal(newTx.Id, result[0].Id);
        
        // Verify SaveAsync was only called for the new transaction
        await mockTransactionRepo.Received(1).SaveAsync(Arg.Is<Transaction>(t => t.Id == newTx.Id), Arg.Any<CancellationToken>());
    }
}
