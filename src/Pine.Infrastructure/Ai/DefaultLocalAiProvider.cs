using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Pine.Core.Ai;

namespace Pine.Infrastructure.Ai;

/// <summary>
/// Skeleton implementation for the on-device AI provider.
/// Will eventually use Microsoft.ML.OnnxRuntime.
/// </summary>
public sealed class DefaultLocalAiProvider : IAIProvider
{
    public Task<bool> DetectReceiptAsync(Stream imageStream, CancellationToken cancellationToken = default)
    {
        // TODO: Load ONNX model for receipt detection and run inference.
        // Returning true as a skeleton default.
        return Task.FromResult(true);
    }

    public Task<ReceiptExtractionResult> ExtractReceiptDataAsync(Stream imageStream, CancellationToken cancellationToken = default)
    {
        // TODO: Orchestrate OCR (e.g. PaddleOCR), Layout Analysis, and Field Extraction models via ONNX.
        var dummyResult = new ReceiptExtractionResult(
            Merchant: "Dummy Merchant",
            TotalAmount: 15.99m,
            CurrencyCode: "USD",
            Date: DateTimeOffset.UtcNow,
            ConfidenceScore: 0.85
        );
        
        return Task.FromResult(dummyResult);
    }

    public Task<CategorisationResult> CategorizeTransactionAsync(string transactionText, decimal amount, CancellationToken cancellationToken = default)
    {
        // TODO: Run inference on a custom small text classification ONNX model.
        var dummyResult = new CategorisationResult(
            CategoryId: Guid.NewGuid(),
            ConfidenceScore: 0.90
        );
        
        return Task.FromResult(dummyResult);
    }
}
