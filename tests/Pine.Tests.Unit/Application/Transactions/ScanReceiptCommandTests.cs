using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using Pine.Application.Transactions.Commands;
using Pine.Core.Ai;
using Xunit;

namespace Pine.Tests.Unit.Application.Transactions;

public class ScanReceiptCommandTests
{
    [Fact]
    public async Task Handle_WhenImageIsNotReceipt_ThrowsInvalidOperationException()
    {
        // Arrange
        var mockAiProvider = Substitute.For<IAIProvider>();
        mockAiProvider.DetectReceiptAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));

        var handler = new ScanReceiptCommandHandler(mockAiProvider);
        var command = new ScanReceiptCommand(new MemoryStream());

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenImageIsReceipt_ReturnsExtractedData()
    {
        // Arrange
        var mockAiProvider = Substitute.For<IAIProvider>();
        mockAiProvider.DetectReceiptAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        var extractionResult = new ReceiptExtractionResult("Test Merchant", 10.0m, "USD", DateTimeOffset.UtcNow, 0.9);
        mockAiProvider.ExtractReceiptDataAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(extractionResult));

        var handler = new ScanReceiptCommandHandler(mockAiProvider);
        var command = new ScanReceiptCommand(new MemoryStream());

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.Equal("Test Merchant", result.Merchant);
        Assert.Equal(10.0m, result.TotalAmount);
    }
}
