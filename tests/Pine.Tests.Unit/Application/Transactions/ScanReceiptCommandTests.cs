using System;
using System.Collections.Generic;
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

        var extractionResult = new ReceiptExtractionResult("Test Merchant", 10.0m, "USD", DateTimeOffset.UtcNow, ReceiptConfidence.None);
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

    // ── Stage reporting ──────────────────────────────────────────────────────
    //
    // WHY THIS IS TESTED AT ALL, given it only drives a label. The scan takes several seconds, and
    // the complaint that produced it was that the app looked hung. A stage that is reported late,
    // in the wrong order, or not at all puts the wrong sentence under the spinner for the whole
    // slow part -- which is precisely the symptom being fixed, reintroduced one layer up. The
    // reports are also the only externally visible evidence that the handler still runs its two
    // stages in the intended order.

    [Fact]
    public async Task Handle_ReportsEachStageAsItBegins_InOrder()
    {
        // Arrange
        var provider = Substitute.For<IAIProvider>();
        provider.DetectReceiptAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));
        provider.ExtractReceiptDataAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ReceiptExtractionResult("M", 1m, "USD", DateTimeOffset.UtcNow, ReceiptConfidence.None)));

        var stages = new List<ScanReceiptStage>();
        var handler = new ScanReceiptCommandHandler(provider);

        // A synchronous sink, not Progress<T>: Progress<T> marshals through the synchronization
        // context and would make the assertion race the test's own completion.
        var command = new ScanReceiptCommand(new MemoryStream(), new ImmediateProgress(stages.Add));

        // Act
        await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.Equal(
            new[] { ScanReceiptStage.DetectingReceipt, ScanReceiptStage.ExtractingText },
            stages);
    }

    [Fact]
    public async Task Handle_ReportsEachStage_BEFORE_TheSlowWorkForThatStageRuns()
    {
        // Arrange - this test exists because a falsification mutation proved the previous two could
        // not tell the difference. Moving both reports to AFTER their provider call left the
        // observed ORDER identical and the not-a-receipt case identical, so both kept passing while
        // the property that actually matters was gone.
        //
        // And it is the property that matters: the whole point is to put a sentence under the
        // spinner BEFORE the multi-second wait, not after it. Reporting late means the user stares
        // at a blank or stale label for exactly the duration the label exists to explain.
        //
        // So this asserts on WHEN, not on order: each substitute snapshots what had been reported
        // at the instant it was called.
        var provider = Substitute.For<IAIProvider>();
        var stages = new List<ScanReceiptStage>();

        ScanReceiptStage[]? reportedWhenDetectRan = null;
        ScanReceiptStage[]? reportedWhenExtractRan = null;

        provider.DetectReceiptAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                reportedWhenDetectRan = stages.ToArray();
                return Task.FromResult(true);
            });

        provider.ExtractReceiptDataAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                reportedWhenExtractRan = stages.ToArray();
                return Task.FromResult(new ReceiptExtractionResult("M", 1m, "USD", DateTimeOffset.UtcNow, ReceiptConfidence.None));
            });

        var handler = new ScanReceiptCommandHandler(provider);
        var command = new ScanReceiptCommand(new MemoryStream(), new ImmediateProgress(stages.Add));

        // Act
        await handler.Handle(command, CancellationToken.None);

        // Assert - detection had already announced itself by the time it started running.
        Assert.Equal(new[] { ScanReceiptStage.DetectingReceipt }, reportedWhenDetectRan);

        // And extraction had announced itself before IT started, with detection already behind it.
        Assert.Equal(
            new[] { ScanReceiptStage.DetectingReceipt, ScanReceiptStage.ExtractingText },
            reportedWhenExtractRan);
    }

    [Fact]
    public async Task Handle_WhenTheImageIsNotAReceipt_NeverClaimsToBeReadingText()
    {
        // Arrange
        var provider = Substitute.For<IAIProvider>();
        provider.DetectReceiptAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));

        var stages = new List<ScanReceiptStage>();
        var handler = new ScanReceiptCommandHandler(provider);
        var command = new ScanReceiptCommand(new MemoryStream(), new ImmediateProgress(stages.Add));

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.Handle(command, CancellationToken.None));

        // Assert - the run stopped at detection, so the last thing the user saw must be detection.
        // Reporting ExtractingText here would leave "Reading the text..." frozen on screen beneath
        // an error that says the image was never a receipt.
        Assert.Equal(new[] { ScanReceiptStage.DetectingReceipt }, stages);
    }

    [Fact]
    public async Task Handle_WithoutAProgressSink_StillCompletes()
    {
        // Arrange - Progress is optional, and a caller with nowhere to show it (a test, a batch
        // import) must not be forced to supply one. A null-reference here would turn an optional
        // convenience into a required dependency.
        var provider = Substitute.For<IAIProvider>();
        provider.DetectReceiptAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));
        provider.ExtractReceiptDataAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ReceiptExtractionResult("M", 2m, "USD", DateTimeOffset.UtcNow, ReceiptConfidence.None)));

        var handler = new ScanReceiptCommandHandler(provider);

        // Act
        var result = await handler.Handle(new ScanReceiptCommand(new MemoryStream()), CancellationToken.None);

        // Assert
        Assert.Equal(2m, result.TotalAmount);
    }

    private sealed class ImmediateProgress : IProgress<ScanReceiptStage>
    {
        private readonly Action<ScanReceiptStage> _onReport;

        public ImmediateProgress(Action<ScanReceiptStage> onReport) => _onReport = onReport;

        public void Report(ScanReceiptStage value) => _onReport(value);
    }
}
