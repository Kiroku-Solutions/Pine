using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Pine.Core.Ai;
using Pine.Core.Transactions;

namespace Pine.Application.Transactions.Commands;

/// <summary>
/// Runs receipt detection and OCR extraction over an image and returns the extracted fields for
/// the user to confirm. Nothing is persisted: the confirmed result is submitted separately as an
/// <see cref="AddTransactionCommand"/>.
/// </summary>
/// <remarks>
/// DELIBERATELY NOT <c>ITransactionalRequest</c> (TD-01): the handler depends only on
/// <c>IAIProvider</c> and performs no repository writes, so there is nothing to commit. Wrapping
/// it would hold a database transaction open for the whole (slow) inference run for no benefit.
/// </remarks>
/// <param name="ImageStream">The image to scan. The handler rewinds it between stages.</param>
/// <param name="Progress">
/// Optional stage reporter. OPTIONAL ON PURPOSE: the scan is correct without it, and a caller with
/// nowhere to display progress (a test, a future batch import) should not be forced to invent a
/// sink. When supplied, the handler reports each stage as it begins -- so the report is "this is
/// what is happening now", never "this is what just finished", which is the one that leaves a user
/// staring at a finished-sounding message while the slow part runs.
/// </param>
public record ScanReceiptCommand(
    Stream ImageStream,
    IProgress<ScanReceiptStage>? Progress = null) : IRequest<ReceiptExtractionResult>;

public sealed class ScanReceiptCommandHandler : IRequestHandler<ScanReceiptCommand, ReceiptExtractionResult>
{
    private readonly IAIProvider _aiProvider;

    public ScanReceiptCommandHandler(IAIProvider aiProvider)
    {
        _aiProvider = aiProvider;
    }

    public async Task<ReceiptExtractionResult> Handle(ScanReceiptCommand request, CancellationToken cancellationToken)
    {
        // 1. Image -> Receipt Detection
        request.Progress?.Report(ScanReceiptStage.DetectingReceipt);
        bool isReceipt = await _aiProvider.DetectReceiptAsync(request.ImageStream, cancellationToken);
        if (!isReceipt)
        {
            throw new InvalidOperationException("The provided image does not appear to be a receipt.");
        }

        // Reset stream position before passing it to the OCR
        request.ImageStream.Position = 0;

        // 2. Extract Data (OCR -> Layout -> Fields)
        request.Progress?.Report(ScanReceiptStage.ExtractingText);
        var result = await _aiProvider.ExtractReceiptDataAsync(request.ImageStream, cancellationToken);
        
        // 3. Validation / Suggestion (Return to UI for user confirmation)
        return result;
    }
}
