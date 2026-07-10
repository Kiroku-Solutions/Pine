using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Pine.Core.Ai;
using Pine.Core.Transactions;

namespace Pine.Application.Transactions.Commands;

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

        // 2. Extract Data (OCR -> Layout -> Fields)
        var result = await _aiProvider.ExtractReceiptDataAsync(request.ImageStream, cancellationToken);
        
        // 3. Validation / Suggestion (Return to UI for user confirmation)
        return result;
    }
}
