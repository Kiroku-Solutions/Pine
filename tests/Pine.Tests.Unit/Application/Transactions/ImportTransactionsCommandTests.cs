using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using Pine.Application.Transactions.Commands;
using Pine.Core.Importing;
using Pine.Core.Transactions;
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
        
        mockImporter.ImportAsync(Arg.Any<Stream>(), Arg.Any<ImportProfile>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult((IReadOnlyList<Transaction>)expectedTransactions));

        var handler = new ImportTransactionsCommandHandler();
        var profile = ImportProfile.Create("Test Profile", TimeProvider.System);
        var command = new ImportTransactionsCommand(new MemoryStream(), mockImporter, profile);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.Same(expectedTransactions, result);
        await mockImporter.Received(1).ImportAsync(command.FileStream, profile, Arg.Any<CancellationToken>());
    }
}
