using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Pine.Core.Ai;
using Pine.Core.Transactions;
using Pine.Infrastructure.Ai.Extraction;
using Pine.Infrastructure.Ai.Extraction.LayoutLm;
using Xunit;

namespace Pine.Tests.Unit.Ai.SpanishReceipts;

/// <summary>
/// Regression for the tablet report: a receipt photographed at an angle (or shown on a screen) put the amounts of one
/// row lower than its label, so "DESCRIPTION" got the price 100 and "Sales Tax" got 154. Rows are now clustered in a
/// deskewed frame, headers are never items, and a tax is never the total, a subtotal or more than its rate allows.
/// </summary>
public class ReceiptTiltRobustnessTests
{
    private static List<OcrWord> Tilted(SpanishReceiptFixture f, double degrees)
    {
        var angle = degrees * Math.PI / 180.0;
        var (cos, sin) = (Math.Cos(angle), Math.Sin(angle));
        return f.ToWords().Select(w =>
        {
            var b = w.BoundingBox;
            double cx = b.X + b.Width / 2.0, cy = b.Y + b.Height / 2.0;
            double nx = cx * cos - cy * sin, ny = cx * sin + cy * cos;
            return new OcrWord
            {
                Text = w.Text,
                Confidence = w.Confidence,
                BoundingBox = new BoundingBox { X = (float)(nx - b.Width / 2.0), Y = (float)(ny - b.Height / 2.0), Width = b.Width, Height = b.Height },
            };
        }).ToList();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(8)]
    [InlineData(-6)]
    public void The_east_repair_invoice_reads_the_same_when_tilted(double degrees)
    {
        var f = SpanishReceiptCorpus.Get("us-east-repair-invoice");
        var r = new HeuristicFieldExtractor().ExtractFields(Tilted(f, degrees));

        r.TotalAmount.Should().Be(154.06m);
        r.TaxAmount.Should().Be(9.06m);
        r.LineItems!.Where(i => i.Kind == LineItemKind.Product).Select(i => i.TotalPrice).Should().Equal(100.00m, 30.00m, 15.00m);
        r.LineItems!.Should().NotContain(i => i.Description.Contains("DESCRIPTION", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_table_header_row_is_never_an_item()
    {
        var f = new SpanishReceiptFixture("t", ["SHOP", "DESCRIPTION QTY AMOUNT 100.00", "Widget 1 20.00 20.00", "TOTAL 20.00"], "", "", 0m, null, "");
        var r = new HeuristicFieldExtractor().ExtractFields(f.ToWords());
        (r.LineItems ?? []).Should().NotContain(i => i.Description.Contains("DESCRIPTION", StringComparison.OrdinalIgnoreCase)
            || i.Description.Contains("AMOUNT", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_misread_subtotal_word_is_still_a_subtotal()
    {
        var f = new SpanishReceiptFixture("t", ["SHOP", "QTY DESCRIPTION PRICE AMOUNT", "1 Pump 100.00 100.00", "1 Hose 45.00 45.00", "Sublotal 145.00", "Sales Tax 6.25% 9.06", "TOTAL 154.06"], "", "", 0m, null, "");
        var r = new HeuristicFieldExtractor().ExtractFields(f.ToWords());
        r.LineItems!.Where(i => i.Kind == LineItemKind.Product).Should().HaveCount(2);
        r.TaxAmount.Should().Be(9.06m);
    }

    [Theory]
    [InlineData("Sales Tax 6.25% 154.06")]   // the total read as tax
    [InlineData("Sales Tax 6.25% 145.00")]   // the subtotal read as tax
    [InlineData("Sales Tax 6.25% 30.00")]    // more than the rate allows on these items
    public void An_impossible_tax_is_left_empty_instead_of_invented(string taxRow)
    {
        var f = new SpanishReceiptFixture("t", ["SHOP", "QTY DESCRIPTION PRICE AMOUNT", "1 Pump 100.00 100.00", "1 Hose 45.00 45.00", "Subtotal 145.00", taxRow, "TOTAL 154.06"], "", "", 0m, null, "");
        var r = new HeuristicFieldExtractor().ExtractFields(f.ToWords());
        r.TaxAmount.Should().BeNull();
        r.LineItems!.Should().NotContain(i => i.Kind == LineItemKind.Tax);
    }

    [Fact]
    public void A_layout_tax_equal_to_the_total_is_ignored_and_the_one_that_fits_the_rate_wins()
    {
        var heuristic = new ReceiptExtractionResult("Shop", 154.06m, "USD", null, ReceiptConfidence.None,
            [new ReceiptLineItem("Pump", 1m, 145m, 145m), new ReceiptLineItem("Sales Tax 6.25%", 1m, 9.06m, 9.06m, LineItemKind.Tax)], TaxAmount: 9.06m);
        var layout = new ReceiptExtractionResult("Shop", 154.06m, null, null, ReceiptConfidence.None, TaxAmount: 154.06m);
        var merged = LayoutLmResultMerger.Overlay(heuristic, layout);
        merged.TaxAmount.Should().Be(9.06m);
        merged.TaxAlternative.Should().BeNull();

        var wrongHeuristic = heuristic with
        {
            TaxAmount = 30m,
            LineItems = [new ReceiptLineItem("Pump", 1m, 145m, 145m), new ReceiptLineItem("Sales Tax 6.25%", 1m, 30m, 30m, LineItemKind.Tax)],
        };
        var fixedByModel = LayoutLmResultMerger.Overlay(wrongHeuristic, new ReceiptExtractionResult("Shop", 154.06m, null, null, ReceiptConfidence.None, TaxAmount: 9.06m));
        fixedByModel.TaxAmount.Should().Be(9.06m);
        fixedByModel.TaxAlternative.Should().Be(30m);
    }
}
