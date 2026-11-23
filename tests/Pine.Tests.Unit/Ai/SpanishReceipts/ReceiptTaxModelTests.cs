using System.Linq;
using FluentAssertions;
using Pine.Core.Ai;
using Pine.Core.Transactions;
using Pine.Infrastructure.Ai.Extraction;
using Xunit;

namespace Pine.Tests.Unit.Ai.SpanishReceipts;

/// <summary>
/// The tax model: a tax is a receipt field (TaxAmount, the sum of every tax row) and, when it is charged on top of
/// the prices, a separate Kind=Tax line per printed row. It is never a Product; tip, discount and rounding likewise
/// have their own Kind, and tax that is already inside the prices is a field only.
/// </summary>
public partial class ReceiptTaxModelTests
{
    private static ReceiptExtractionResult Run(params string[] lines) =>
        new HeuristicFieldExtractor().ExtractFields(new SpanishReceiptFixture("t", lines, "", "", 0m, null, "").ToWords());

    private static void AssertModel(ReceiptExtractionResult r, decimal total, decimal tax, int products)
    {
        r.TotalAmount.Should().Be(total);
        r.TaxAmount.Should().Be(tax);
        var items = r.LineItems!;
        items.Count(i => i.Kind == LineItemKind.Product).Should().Be(products);
        items.Where(i => i.Kind == LineItemKind.Product).Should().NotContain(
            i => AdjustmentWord().IsMatch(i.Description));
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"\b(tax|iva|vat|gst|hst|igv|ice|propina|servicio|subtotal|total|cash|change)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex AdjustmentWord();

    [Theory]
    [InlineData("Sales Tax 6.25% 9.06", 154.06, 9.06)]
    [InlineData("TAX 9.06", 154.06, 9.06)]
    [InlineData("T4X 9.06", 154.06, 9.06)]          // OCR read A as 4
    [InlineData("VAT 20% 9.06", 154.06, 9.06)]
    [InlineData("GST 9.06", 154.06, 9.06)]
    [InlineData("HST 13% 9.06", 154.06, 9.06)]
    [InlineData("State Tax 9.06", 154.06, 9.06)]
    [InlineData("I.V.A. 15% 9.06", 154.06, 9.06)]
    [InlineData("ITBMS 7% 9.06", 154.06, 9.06)]
    public void A_tax_row_is_a_tax_line_whatever_its_name(string taxRow, double total, double tax)
    {
        var r = Run("ACME STORE", "QTY DESCRIPTION UNIT PRICE AMOUNT", "1 Widget 100.00 100.00", "2 Gadget 15.00 30.00", "3 Labor 5.00 15.00",
            "Subtotal 145.00", taxRow, "TOTAL $154.06");
        AssertModel(r, (decimal)total, (decimal)tax, 3);
        r.LineItems!.Single(i => i.Kind == LineItemKind.Tax).TotalPrice.Should().Be((decimal)tax);
        r.LineItems!.Sum(i => i.TotalPrice).Should().Be((decimal)total);
    }

    [Fact]
    public void Several_taxes_are_summed_with_one_line_each_and_a_total_tax_row_is_not_counted_twice()
    {
        var r = Run("TIENDA ABC", "DESCRIPCION CANT PRECIO VALOR", "TV 1 100,00 100,00", "SUBTOTAL 100,00", "IVA 12% 12,00", "ICE 15% 15,00",
            "TOTAL IMPUESTOS 27,00", "VALOR TOTAL 127,00");
        r.TaxAmount.Should().Be(27.00m);
        r.LineItems!.Where(i => i.Kind == LineItemKind.Tax).Select(i => i.TotalPrice).Should().BeEquivalentTo([12.00m, 15.00m]);
        r.LineItems!.Sum(i => i.TotalPrice).Should().Be(127.00m);
    }

    [Fact]
    public void Tax_that_is_already_in_the_prices_is_reported_but_not_added_as_a_line()
    {
        var r = Run("BODEGA", "DESCRIPTION QTY PRICE", "PAN 2 1.50 3.00", "LECHE 1 2.00 2.00", "TOTAL 5.00", "IVA INCLUIDO 15% 0.65");
        r.TotalAmount.Should().Be(5.00m);
        r.TaxAmount.Should().Be(0.65m);
        r.LineItems!.Should().OnlyContain(i => i.Kind == LineItemKind.Product);
        r.LineItems!.Sum(i => i.TotalPrice).Should().Be(5.00m);
    }

    [Fact]
    public void Exempt_zero_and_rate_only_rows_produce_no_tax()
    {
        var r = Run("TIENDA", "DESCRIPCION CANT PRECIO VALOR", "ARROZ 1 2,00 2,00", "SUBTOTAL 0% 2,00", "TARIFA 0% PRODUCTO 2,00", "EXENTO 2,00",
            "IVA 15%", "TAX 0.00", "TOTAL 2,00");
        r.TotalAmount.Should().Be(2.00m);
        r.TaxAmount.Should().BeNull();
        r.LineItems!.Should().OnlyContain(i => i.Kind == LineItemKind.Product);
    }

    [Fact]
    public void Restaurant_service_charge_and_vat_are_a_tip_and_a_tax_not_products()
    {
        var r = Run("LA PARRILLA", "CANT DESCRIPCION VALOR", "2 LOMO FINO 20,00", "1 ENSALADA 5,00", "SUBTOTAL 25,00", "SERVICIO 10% 2,50", "IVA 15% 3,75",
            "TOTAL A PAGAR 31,25");
        AssertModel(r, 31.25m, 3.75m, 2);
        r.LineItems!.Single(i => i.Kind == LineItemKind.Tip).TotalPrice.Should().Be(2.50m);
        r.LineItems!.Sum(i => i.TotalPrice).Should().Be(31.25m);
    }

    [Fact]
    public void Tax_flag_letters_after_the_price_are_not_part_of_the_description_and_cash_and_change_are_not_items()
    {
        var r = Run("GROCER", "DESCRIPTION PRICE", "MILK 3.99 F", "SHIRT 10.00 T", "SUBTOTAL 13.99", "TAX 0.80", "TOTAL 14.79", "CASH 20.00", "CHANGE 5.21");
        r.TotalAmount.Should().Be(14.79m);
        r.LineItems!.Where(i => i.Kind == LineItemKind.Product).Select(i => i.Description).Should().BeEquivalentTo(["MILK", "SHIRT"]);
        r.TaxAmount.Should().Be(0.80m);
    }

    [Fact]
    public void Rounding_gets_its_own_kind()
    {
        var r = Run("KEDAI", "DESCRIPTION PRICE", "TEA 3.33 3.33", "COFFEE 6.34 6.34", "TOTAL 9.65", "ROUNDING 0.03", "NET TOTAL 9.70");
        r.LineItems!.Should().NotContain(i => i.Kind == LineItemKind.Product && i.Description.Contains("ROUND", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_row_that_is_exactly_the_gap_between_subtotal_and_total_is_a_tax_even_without_a_keyword()
    {
        var r = Run("ACME", "QTY DESCRIPTION PRICE AMOUNT", "1 Pump 100.00 100.00", "1 Hose 40.00 40.00", "1 Levy 6.25% 8.75 8.75", "Subtotal 140.00", "TOTAL 148.75");
        r.LineItems!.Where(i => i.Kind == LineItemKind.Product).Should().HaveCount(2);
        r.LineItems!.Single(i => i.Kind == LineItemKind.Tax).TotalPrice.Should().Be(8.75m);
        r.TaxAmount.Should().Be(8.75m);
    }

    [Fact]
    public void Ice_cream_is_a_product_not_the_excise_tax()
    {
        var r = Run("HELADOS", "DESCRIPTION QTY PRICE", "ICE CREAM 2 1.50 3.00", "TOTAL 3.00");
        r.LineItems!.Should().ContainSingle(i => i.Kind == LineItemKind.Product && i.Description.Contains("ICE CREAM"));
    }
}
