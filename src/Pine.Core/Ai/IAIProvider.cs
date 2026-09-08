using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Pine.Core.Ai;

/// <summary>
/// Represents a single extracted line item from a receipt.
/// </summary>
/// <remarks>
/// AMOUNTS ARE AS PRINTED ON THE RECEIPT -- positive for a charge -- not in the transaction sign.
/// The extractor reads a document; it does not know whether the resulting transaction will be an
/// expense or an income, so converting to the ledger sign is the job of whoever builds the command.
/// <para>
/// <c>Kind</c> exists so that the tax, tip and discount totals the extractor ALREADY parses can
/// reach the reconciliation (TD-19) instead of being thrown away after nudging a confidence score.
/// </para>
/// </remarks>
public record ReceiptLineItem(
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal TotalPrice,
    Pine.Core.Transactions.LineItemKind Kind = Pine.Core.Transactions.LineItemKind.Product
);

/// <summary>
/// Contains the extracted data and per-field confidence from a receipt.
/// </summary>
/// <remarks>
/// <para>
/// WHY THERE IS NO AGGREGATE SCORE (TD-22). The score this record used to carry was a tally of
/// which fields were populated, never consulted the OCR engine's own per-word confidences, and
/// rated a fully-populated, entirely wrong extraction at roughly 0.9. <see cref="Confidence"/>
/// carries a real, per-field <c>FieldConfidence</c> instead, built from the words that actually
/// produced each value -- a single percentage cannot tell the user which field to check, which is
/// the only thing they can act on.
/// </para>
/// <para>
/// WHY THE DATE CARRIES AN ALTERNATIVE (TD-21). A date like <c>05/03/2026</c> has two equally
/// plausible readings depending on locale, and no amount of parsing skill can pick the right one.
/// <see cref="DateIsAmbiguous"/> and <see cref="AlternativeDate"/> surface the other reading instead
/// of silently guessing, so the person holding the receipt can make the call the parser cannot.
/// </para>
/// </remarks>
public record ReceiptExtractionResult(
    string? Merchant,
    decimal? TotalAmount,
    string? CurrencyCode,
    System.DateTimeOffset? Date,
    ReceiptConfidence Confidence,
    System.Collections.Generic.IReadOnlyList<ReceiptLineItem>? LineItems = null,
    bool DateIsAmbiguous = false,
    System.DateTimeOffset? AlternativeDate = null
);

/// <summary>
/// Contains the categorisation prediction.
/// </summary>
/// <remarks>
/// <para>
/// <paramref name="Category"/> IS A NAME, AND AN EMPTY STRING MEANS "NO SUGGESTION" (TD-32). This
/// record used to carry a <see cref="System.Guid"/> <c>CategoryId</c>, which every caller then had
/// to resolve back to a name through <c>ICategoryRepository</c> — against a <c>Categories</c> table
/// that no code path in this application has ever written a row to. Both call sites therefore
/// discarded every prediction the model made, and neither could report why.
/// </para>
/// <para>
/// The empty string is the "no suggestion" value rather than <c>null</c> so that a caller cannot
/// dereference it by accident, and it is checked with the same
/// <c>string.IsNullOrWhiteSpace</c> guard the ledger already uses for a split's category.
/// </para>
/// </remarks>
public record CategorisationResult(
    string Category,
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
