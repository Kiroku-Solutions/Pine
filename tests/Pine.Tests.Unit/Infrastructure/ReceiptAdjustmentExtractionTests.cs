using FluentAssertions;
using Pine.Core.Ai;
using Pine.Core.Transactions;
using Pine.Infrastructure.Ai.Extraction;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Pine.Tests.Unit.Infrastructure;

/// <summary>
/// The extractor already parsed the tax, tip and discount totals of a receipt in order to
/// cross-validate the printed total against them -- and then threw them away, using them only to
/// nudge a confidence score by 0.1. TD-19 is the reason that was waste: those are exactly the lines
/// that make a receipt reconcile.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS MATTERS TO THE USER AND NOT ONLY TO THE MODEL. Without this wiring, every taxed receipt
/// would arrive in the scanner with a difference the user has to attribute by hand before they are
/// allowed to save -- turning the invariant into a tax on using the feature. With it, the common
/// case arrives already reconciled and the reconciliation panel only appears when the extractor
/// genuinely could not explain the gap.
/// </para>
/// <para>
/// ONLY WHAT WAS READ IS EMITTED. No residual "unattributed" line is invented, because a gap the
/// extractor cannot explain is the user's to resolve; closing it automatically would make the
/// reconciliation unfalsifiable.
/// </para>
/// </remarks>
public class ReceiptAdjustmentExtractionTests
{
    /// <summary>Builds one OCR line as a row of words at a shared vertical position.</summary>
    private static IEnumerable<OcrWord> Line(float y, params string[] words)
    {
        float x = 0f;
        foreach (var word in words)
        {
            yield return new OcrWord
            {
                Text = word,
                Confidence = 0.99f,
                BoundingBox = new BoundingBox { X = x, Y = y, Width = word.Length * 8f, Height = 10f },
            };

            x += word.Length * 8f + 8f;
        }
    }

    private static ReceiptExtractionResult Extract(params IEnumerable<OcrWord>[] lines)
    {
        using var extractor = new HeuristicFieldExtractor();
        return extractor.ExtractFields(lines.SelectMany(l => l).ToList());
    }

    [Fact]
    public void ATaxedReceipt_ArrivesWithItsTaxAsATypedAdjustmentLine()
    {
        var result = Extract(
            Line(0, "SUPERMERCADO", "CENTRAL"),
            Line(20, "DESCRIPCION", "CANT", "PRECIO"),
            Line(40, "PAN", "2", "25.00", "50.00"),
            Line(60, "LECHE", "1", "50.00", "50.00"),
            Line(80, "SUBTOTAL", "100.00"),
            Line(100, "IVA", "21.00"),
            Line(120, "TOTAL", "121.00"));

        result.TotalAmount.Should().Be(121.00m);
        result.LineItems.Should().NotBeNull();

        var tax = result.LineItems!.SingleOrDefault(li => li.Kind == LineItemKind.Tax);
        tax.Should().NotBeNull("the IVA line was parsed to cross-validate the total and must not be discarded");
        tax!.TotalPrice.Should().Be(21.00m);

        // And the whole set now accounts for the printed total, which is what makes the receipt
        // saveable without the user having to attribute anything by hand.
        result.LineItems!.Sum(li => li.TotalPrice).Should().Be(121.00m);
    }

    [Fact]
    public void AnUntaxedReceipt_GainsNoAdjustmentLines()
    {
        // Nothing is invented. A receipt whose products already add up gets exactly its products.
        var result = Extract(
            Line(0, "SUPERMERCADO", "CENTRAL"),
            Line(20, "DESCRIPCION", "CANT", "PRECIO"),
            Line(40, "PAN", "2", "25.00", "50.00"),
            Line(60, "LECHE", "1", "50.00", "50.00"),
            Line(80, "SUBTOTAL", "100.00"),
            Line(100, "TOTAL", "100.00"));

        result.LineItems.Should().NotBeNull();
        result.LineItems!.Should().OnlyContain(li => li.Kind == LineItemKind.Product);
        result.LineItems!.Sum(li => li.TotalPrice).Should().Be(100.00m);
    }

    [Fact]
    public void AReceiptWithNoLineItemsAtAll_GainsNoAdjustmentLines()
    {
        // An adjustment line with nothing to adjust would be a set that cannot reconcile with any
        // total but its own, so the promotion is skipped entirely when no products were read.
        var result = Extract(
            Line(0, "SUPERMERCADO", "CENTRAL"),
            Line(20, "IVA", "21.00"),
            Line(40, "TOTAL", "121.00"));

        (result.LineItems ?? []).Should().NotContain(li => li.Kind == LineItemKind.Tax);
    }
}
