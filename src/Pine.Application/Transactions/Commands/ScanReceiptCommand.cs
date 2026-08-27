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
public record ScanReceiptCommand(Stream ImageStream) : IRequest<ReceiptExtractionResult>;

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
        bool isReceipt = await _aiProvider.DetectReceiptAsync(request.ImageStream, cancellationToken);
        if (!isReceipt)
        {
            throw new InvalidOperationException("The provided image does not appear to be a receipt.");
        }

        // Reset stream position before passing it to the OCR
        request.ImageStream.Position = 0;

        // 2. Extract Data (OCR -> Layout -> Fields)
        var result = await _aiProvider.ExtractReceiptDataAsync(request.ImageStream, cancellationToken);
        
        // 3. Validation / Suggestion (Return to UI for user confirmation)
        return result;
    }
}
