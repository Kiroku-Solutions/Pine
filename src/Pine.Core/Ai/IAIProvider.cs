using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Pine.Core.Ai;

/// <summary>
/// Contains the extracted data and confidence scores from a receipt.
/// </summary>
public record ReceiptExtractionResult(
    string? Merchant,
    decimal? TotalAmount,
    string? CurrencyCode,
    System.DateTimeOffset? Date,
    double ConfidenceScore
);

/// <summary>
/// Contains the categorisation prediction.
/// </summary>
public record CategorisationResult(
    System.Guid CategoryId,
    double ConfidenceScore
);

/// <summary>
/// Result of analyzing and optionally cropping a receipt.
/// </summary>
public record ReceiptAnalysisResult(
    float Confidence,
    bool PassedFilters,
    string? StatusMessage,
    Stream? CroppedImageStream,
    Stream? OcrImageStream = null
);

/// <summary>
/// Port for the AI Subsystem defining the receipt scanning pipeline and categorisation.
/// To be implemented via Microsoft.ML.OnnxRuntime.
/// </summary>
public interface IAIProvider
{
    /// <summary>
    /// Detects if an image contains a receipt.
    /// </summary>
    Task<bool> DetectReceiptAsync(Stream imageStream, CancellationToken cancellationToken = default);
    Task<float> DetectReceiptConfidenceAsync(Stream imageStream, CancellationToken cancellationToken = default);

    /// <summary>
    /// Analyzes the receipt for geometry filters and returns the cropped image if valid.
    /// </summary>
    Task<ReceiptAnalysisResult> AnalyzeAndCropReceiptAsync(Stream imageStream, CancellationToken cancellationToken = default);

    /// <summary>
    /// Extracts structured data from a receipt image (runs OCR, Layout, and Field Extraction).
    /// </summary>
    Task<ReceiptExtractionResult> ExtractReceiptDataAsync(Stream imageStream, CancellationToken cancellationToken = default);

    /// <summary>
    /// Predicts a category for a given transaction text and context.
    /// </summary>
    Task<CategorisationResult> CategorizeTransactionAsync(string transactionText, decimal amount, CancellationToken cancellationToken = default);
}
