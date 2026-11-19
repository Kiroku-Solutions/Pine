using Pine.Core.Ai;

namespace Pine.Infrastructure.Ai.Extraction.LayoutLm;

/// <summary>Combines the heuristic extraction with what the layout model read.</summary>
/// <remarks>
/// The model overrides a field only when it FOUND (and could parse) it; everything it does not provide
/// (line items, currency, any field it missed) stays as the heuristics produced it. The heuristic logic
/// itself is untouched.
/// </remarks>
public static class LayoutLmResultMerger
{
    /// <summary>Overlays the fields the model found onto the heuristic result.</summary>
    public static ReceiptExtractionResult Overlay(ReceiptExtractionResult heuristic, ReceiptExtractionResult layout)
    {
        ArgumentNullException.ThrowIfNull(heuristic);
        ArgumentNullException.ThrowIfNull(layout);

        var confidence = heuristic.Confidence;
        var merchant = heuristic.Merchant;
        var total = heuristic.TotalAmount;
        var date = heuristic.Date;
        var ambiguous = heuristic.DateIsAmbiguous;
        var alternative = heuristic.AlternativeDate;

        if (!string.IsNullOrWhiteSpace(layout.Merchant))
        {
            merchant = layout.Merchant;
            confidence = confidence with { Merchant = layout.Confidence.Merchant };
        }

        if (layout.TotalAmount is { } layoutTotal)
        {
            total = layoutTotal;
            confidence = confidence with { Total = layout.Confidence.Total };
        }

        if (layout.Date is { } layoutDate)
        {
            date = layoutDate;
            ambiguous = layout.DateIsAmbiguous;
            alternative = layout.AlternativeDate;
            confidence = confidence with { Date = layout.Confidence.Date };
        }

        return heuristic with
        {
            Merchant = merchant,
            TotalAmount = total,
            Date = date,
            DateIsAmbiguous = ambiguous,
            AlternativeDate = alternative,
            Confidence = confidence,
            TaxAmount = layout.TaxAmount ?? heuristic.TaxAmount,
        };
    }
}
