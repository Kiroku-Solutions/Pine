using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Pine.Core.Ai;

/// <summary>Fields a user can tag by drawing a bounding box directly on a scanned receipt.</summary>
public enum ReceiptTagField
{
    Merchant = 0,
    Total = 1,
    Date = 2,
    Tax = 3,
    LineItem = 4,
    Custom = 5,
}

/// <summary>A normalized rectangle inside a receipt image (0..1 on both axes).</summary>
public sealed record NormalizedRect(double X, double Y, double Width, double Height)
{
    public double Right => Math.Clamp(X + Width, 0.0, 1.0);
    public double Bottom => Math.Clamp(Y + Height, 0.0, 1.0);
    public double CenterX => Math.Clamp(X + (Width / 2.0), 0.0, 1.0);
    public double CenterY => Math.Clamp(Y + (Height / 2.0), 0.0, 1.0);

    public static NormalizedRect CreateClamped(double x1, double y1, double x2, double y2)
    {
        var left = Math.Clamp(Math.Min(x1, x2), 0.0, 1.0);
        var top = Math.Clamp(Math.Min(y1, y2), 0.0, 1.0);
        var right = Math.Clamp(Math.Max(x1, x2), 0.0, 1.0);
        var bottom = Math.Clamp(Math.Max(y1, y2), 0.0, 1.0);
        return new NormalizedRect(left, top, Math.Max(0.0, right - left), Math.Max(0.0, bottom - top));
    }
}

/// <summary>One user-tagged field box on a receipt together with its OCR/manual text and nearby anchor words.</summary>
public sealed record ReceiptFieldBox(
    ReceiptTagField Field,
    NormalizedRect Box,
    string ExtractedText,
    string? LeftAnchorText = null,
    string? AboveAnchorText = null,
    string? CustomTagName = null,
    Guid? BoxId = null)
{
    public string EffectiveKey => Field == ReceiptTagField.Custom && !string.IsNullOrWhiteSpace(CustomTagName)
        ? $"Custom:{CustomTagName.Trim()}"
        : Field.ToString();
}

/// <summary>A single annotated receipt sample used to train the user's local receipt layout adapter.</summary>
public sealed record ReceiptLayoutAnnotation(
    Guid Id,
    DateTimeOffset RecordedAtUtc,
    string? MerchantHint,
    IReadOnlyList<ReceiptFieldBox> Boxes);

/// <summary>Current state of the per-vault local receipt layout model.</summary>
public sealed record ReceiptTrainingStatus(
    int SampleCount,
    DateTimeOffset? LastTrainedAtUtc,
    bool IsTraining,
    IReadOnlyList<string>? CustomTagNames = null);

/// <summary>
/// Per-vault on-device learner that adapts receipt field extraction to the user's local receipt layouts
/// (e.g. Spanish/LatAm, Indian GST, Chinese fapiao, store templates) from interactive bounding-box tags.
/// </summary>
public interface IReceiptFieldLearner
{
    /// <summary>
    /// Refines a baseline extraction result using the user's trained local spatial + lexical model.
    /// Fast (&lt;2 ms), offline, and safe to call even when no samples have been recorded yet.
    /// </summary>
    ReceiptExtractionResult ApplyLearnedModel(
        ReceiptExtractionResult baseline,
        IReadOnlyList<OcrWord> ocrWords,
        int imageWidth,
        int imageHeight);

    /// <summary>
    /// Records the user's tagged receipt boxes and schedules low-priority background retraining
    /// without blocking the UI thread.
    /// </summary>
    Task<ReceiptTrainingStatus> RecordAnnotationAndTrainInBackgroundAsync(
        ReceiptLayoutAnnotation annotation,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the current training status and learned custom tag names for the active vault.</summary>
    ReceiptTrainingStatus GetStatus();
}

/// <summary>
/// Crops a user-drawn normalized rectangle from the aligned receipt image and runs on-demand OCR
/// at full resolution so faint or small lines missed by full-page detection can still be read.
/// </summary>
public interface IReceiptRegionReader
{
    Task<string> ReadRegionAsync(
        byte[] alignedImageBytes,
        NormalizedRect region,
        CancellationToken cancellationToken = default);
}
