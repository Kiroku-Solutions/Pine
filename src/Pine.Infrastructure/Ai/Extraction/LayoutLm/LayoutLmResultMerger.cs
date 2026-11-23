using Pine.Core.Ai;
using Pine.Core.Transactions;

namespace Pine.Infrastructure.Ai.Extraction.LayoutLm;

/// <summary>Combines the heuristic extraction with what the layout model read.</summary>
/// <remarks>
/// The model overrides a field only when it FOUND (and could parse) it; everything it does not provide
/// (line items, currency, any field it missed) stays as the heuristics produced it. The heuristic logic
/// itself is untouched.
/// </remarks>
public static partial class LayoutLmResultMerger
{
    [System.Text.RegularExpressions.GeneratedRegex(@"(\d+(?:[.,]\d+)?)\s*%")]
    private static partial System.Text.RegularExpressions.Regex RateRegex();

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

        var merged = heuristic with
        {
            Merchant = merchant,
            TotalAmount = total,
            Date = date,
            DateIsAmbiguous = ambiguous,
            AlternativeDate = alternative,
            Confidence = confidence,
        };

        return ReconcileTax(merged, layout.TaxAmount);
    }

    /// <summary>
    /// One source of truth for the tax: the amount in <c>TaxAmount</c> and the Tax lines shown must agree. When the
    /// layout model and the heuristics read different taxes, the one that reconciles arithmetically with the items,
    /// the other adjustments and the total wins; the other is kept in <c>TaxAlternative</c> as a warning.
    /// </summary>
    private static ReceiptExtractionResult ReconcileTax(ReceiptExtractionResult merged, decimal? layoutTax)
    {
        var heuristicTax = merged.TaxAmount;

        // A tax can never be the printed total (the model sometimes labels the TOTAL figure as tax), nor the sum of the
        // items: such a reading is dropped, and an empty field is preferred to an invented one.
        var all = merged.LineItems ?? [];
        var productsSum = all.Where(i => i.Kind == LineItemKind.Product).Sum(i => i.TotalPrice);
        bool Implausible(decimal t) =>
            (merged.TotalAmount is { } tot && Math.Abs(t - tot) <= 0.005m) || (productsSum > 0m && Math.Abs(t - productsSum) <= 0.005m);

        if (layoutTax is { } candidate && Implausible(candidate))
        {
            layoutTax = null;
        }

        if (heuristicTax is { } hCandidate && Implausible(hCandidate))
        {
            // Without the implausible figure there is no Tax line either.
            var withoutTax = all.Where(i => i.Kind != LineItemKind.Tax).ToList();
            merged = merged with { TaxAmount = null, TaxIncluded = false, LineItems = merged.LineItems is null ? null : withoutTax };
            heuristicTax = null;
        }

        if (layoutTax is not { } lt)
        {
            return merged;
        }

        if (heuristicTax is not { } ht)
        {
            return merged with { TaxAmount = lt };
        }

        if (Math.Abs(ht - lt) <= 0.01m)
        {
            return merged;
        }

        var items = merged.LineItems ?? [];
        if (merged.TotalAmount is not { } total || items.Count == 0)
        {
            // Nothing to test either reading against: keep the model's single figure, as before, and flag the other.
            return merged with { TaxAmount = lt, TaxAlternative = ht };
        }

        var others = items.Where(i => i.Kind != LineItemKind.Tax).Sum(i => i.TotalPrice);
        bool Fits(decimal tax, bool included) => Math.Abs(others + (included ? 0m : tax) - total) <= 0.02m;

        // The rate printed on the heuristic Tax line ("Sales Tax 6.25%") is a second witness: a tax that misses
        // rate x items, while the model's figure hits it, is the mis-read one.
        var rateMatch = RateRegex().Match(items.FirstOrDefault(i => i.Kind == LineItemKind.Tax)?.Description ?? string.Empty);
        bool RateFits(decimal tax) =>
            rateMatch.Success
            && decimal.TryParse(rateMatch.Groups[1].Value.Replace(',', '.'), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var pct)
            && productsSum > 0m
            && Math.Abs(tax - pct / 100m * productsSum) <= Math.Max(0.05m, productsSum * 0.005m);

        var heuristicFits = Fits(ht, merged.TaxIncluded) || (RateFits(ht) && !RateFits(lt));
        var layoutFitsOnTop = Fits(lt, false) || (RateFits(lt) && !RateFits(ht));
        var layoutFitsIncluded = Fits(lt, true);

        if (!heuristicFits && (layoutFitsOnTop || layoutFitsIncluded))
        {
            var kept = new List<ReceiptLineItem>();
            var keptConfidence = new List<FieldConfidence>();
            var perItem = merged.Confidence.LineItems;
            for (var i = 0; i < items.Count; i++)
            {
                if (items[i].Kind == LineItemKind.Tax)
                {
                    continue;
                }

                kept.Add(items[i]);
                keptConfidence.Add(i < perItem.Count ? perItem[i] : FieldConfidence.NotFound);
            }

            var onTop = layoutFitsOnTop;
            if (onTop)
            {
                kept.Add(new ReceiptLineItem("Tax", 1m, lt, lt, LineItemKind.Tax));
                keptConfidence.Add(FieldConfidence.NotFound);
            }

            return merged with
            {
                TaxAmount = lt,
                TaxIncluded = !onTop,
                TaxAlternative = ht,
                LineItems = kept,
                Confidence = merged.Confidence with { LineItems = keptConfidence },
            };
        }

        // The heuristic reading reconciles (or neither does): its lines stay, the model figure is the warning.
        return merged with { TaxAlternative = lt };
    }
}
