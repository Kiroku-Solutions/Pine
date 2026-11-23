using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Pine.Core.Ai;
using Pine.Core.Transactions;
using Pine.Infrastructure.Ai.Extraction;
using Pine.Infrastructure.Ai.Extraction.LayoutLm;
using Xunit;

namespace Pine.Tests.Unit.Ai.SpanishReceipts;

/// <summary>Delivery as its own kind, the included-tax flag, and one source of truth for the tax after the overlay.</summary>
public class ReceiptTaxReconciliationTests
{
    [Theory]
    [InlineData("ENVÍO 5,00")]
    [InlineData("DOMICILIO 5,00")]
    [InlineData("Delivery 5,00")]
    [InlineData("SHIPPING 5,00")]
    [InlineData("FLETE 5,00")]
    public void Delivery_rows_are_a_delivery_line_not_a_product(string row)
    {
        var f = new SpanishReceiptFixture("t", ["TIENDA", "DESCRIPCION CANT PRECIO VALOR", "SILLA 1 40,00 40,00", "SUBTOTAL 40,00", row, "TOTAL 45,00"], "", "", 0m, null, "");
        var r = new HeuristicFieldExtractor().ExtractFields(f.ToWords());
        r.TotalAmount.Should().Be(45.00m);
        r.LineItems!.Where(i => i.Kind == LineItemKind.Product).Should().ContainSingle();
        r.LineItems!.Single(i => i.Kind == LineItemKind.Delivery).TotalPrice.Should().Be(5.00m);
    }

    [Fact]
    public void Included_tax_sets_the_flag_and_adds_no_line()
    {
        var f = new SpanishReceiptFixture("t", ["BODEGA", "DESCRIPTION QTY PRICE", "PAN 2 1.50 3.00", "LECHE 1 2.00 2.00", "TOTAL 5.00", "IVA INCLUIDO 15% 0.65"], "", "", 0m, null, "");
        var r = new HeuristicFieldExtractor().ExtractFields(f.ToWords());
        r.TaxAmount.Should().Be(0.65m);
        r.TaxIncluded.Should().BeTrue();
        r.LineItems!.Should().NotContain(i => i.Kind == LineItemKind.Tax);
    }

    private static ReceiptExtractionResult Heuristic(decimal tax) => new(
        "Shop", 110m, "USD", null, ReceiptConfidence.None,
        [new ReceiptLineItem("Chair", 1m, 100m, 100m), new ReceiptLineItem("Tax", 1m, tax, tax, LineItemKind.Tax)],
        TaxAmount: tax);

    private static ReceiptExtractionResult Layout(decimal? tax) => new("Shop", 110m, null, null, ReceiptConfidence.None, TaxAmount: tax);

    [Fact]
    public void When_the_readings_differ_the_one_that_reconciles_wins_and_the_other_is_the_warning()
    {
        // 100 + 10 = 110: the model is right, the heuristics read 7.
        var merged = LayoutLmResultMerger.Overlay(Heuristic(7m), Layout(10m));
        merged.TaxAmount.Should().Be(10m);
        merged.TaxAlternative.Should().Be(7m);
        merged.LineItems!.Where(i => i.Kind == LineItemKind.Tax).Select(i => i.TotalPrice).Should().Equal(10m);
        merged.LineItems!.Sum(i => i.TotalPrice).Should().Be(110m);

        // 100 + 10 = 110: the heuristics are right, the model read 8.
        var kept = LayoutLmResultMerger.Overlay(Heuristic(10m), Layout(8m));
        kept.TaxAmount.Should().Be(10m);
        kept.TaxAlternative.Should().Be(8m);
        kept.LineItems!.Single(i => i.Kind == LineItemKind.Tax).TotalPrice.Should().Be(10m);
    }

    [Fact]
    public void Equal_readings_carry_no_warning()
    {
        var merged = LayoutLmResultMerger.Overlay(Heuristic(10m), Layout(10m));
        merged.TaxAlternative.Should().BeNull();
        merged.TaxAmount.Should().Be(10m);
    }
}
