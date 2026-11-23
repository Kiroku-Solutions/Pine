using System.Globalization;
using System.Linq;
using System.Text;
using FluentAssertions;
using Pine.Core.Ai;
using Pine.Core.Transactions;
using Pine.Infrastructure.Ai.Extraction;
using Xunit;
using Xunit.Abstractions;

namespace Pine.Tests.Unit.Ai.SpanishReceipts;

/// <summary>
/// Field accuracy of the heuristic extractor on the synthetic Spanish corpus (plus the East Repair invoice that
/// reproduced the address-as-item and tax-as-item defects). The corpus is hand-written transcripts, not photos.
/// </summary>
public class SpanishReceiptExtractionTests(ITestOutputHelper output)
{
    private static string Key(string? s) =>
        new((s ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static ReceiptExtractionResult Run(SpanishReceiptFixture f) => new HeuristicFieldExtractor().ExtractFields(f.ToWords());

    private static decimal? TaxOf(ReceiptExtractionResult r) =>
        r.TaxAmount ?? (r.LineItems is { Count: > 0 } items && items.Any(i => i.Kind == LineItemKind.Tax)
            ? items.Where(i => i.Kind == LineItemKind.Tax).Sum(i => i.TotalPrice)
            : null);

    [Theory]
    [MemberData(nameof(SpanishReceiptCorpus.Names), MemberType = typeof(SpanishReceiptCorpus))]
    public void Merchant_is_read(string name)
    {
        var f = SpanishReceiptCorpus.Get(name);
        Key(Run(f).Merchant).Should().Be(Key(f.Merchant));
    }

    [Theory]
    [MemberData(nameof(SpanishReceiptCorpus.Names), MemberType = typeof(SpanishReceiptCorpus))]
    public void Date_is_read(string name)
    {
        var f = SpanishReceiptCorpus.Get(name);
        Run(f).Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture).Should().Be(f.Date);
    }

    [Theory]
    [MemberData(nameof(SpanishReceiptCorpus.Names), MemberType = typeof(SpanishReceiptCorpus))]
    public void Total_is_read(string name)
    {
        var f = SpanishReceiptCorpus.Get(name);
        Run(f).TotalAmount.Should().Be(f.Total);
    }

    [Theory]
    [MemberData(nameof(SpanishReceiptCorpus.Names), MemberType = typeof(SpanishReceiptCorpus))]
    public void Tax_is_read_when_printed(string name)
    {
        var f = SpanishReceiptCorpus.Get(name);
        if (f.Tax is null)
        {
            return;
        }

        TaxOf(Run(f)).Should().Be(f.Tax);
    }

    [Theory]
    [MemberData(nameof(SpanishReceiptCorpus.Names), MemberType = typeof(SpanishReceiptCorpus))]
    public void Tax_bases_addresses_and_totals_are_never_line_items(string name)
    {
        var f = SpanishReceiptCorpus.Get(name);
        var products = (Run(f).LineItems ?? []).Where(i => i.Kind == LineItemKind.Product).ToList();
        foreach (var item in products)
        {
            item.Description.ToUpperInvariant().Should().NotContainAny("TARIFA", "BASE", "SUBTOTAL", "IVA", "IGV", "TAX", "TOTAL", "PROPINA", "NY ", "MA ");
            item.TotalPrice.Should().BeLessThan(f.Total + 0.01m, "no item can cost more than the whole receipt");
        }
    }

    [Fact]
    public void East_repair_invoice_has_three_items_that_add_up_to_the_subtotal()
    {
        var r = Run(SpanishReceiptCorpus.Get("us-east-repair-invoice"));
        var products = r.LineItems!.Where(i => i.Kind == LineItemKind.Product).ToList();
        products.Should().HaveCount(3);
        products.Sum(i => i.TotalPrice).Should().Be(145.00m);
        r.DateIsAmbiguous.Should().BeFalse("Due Date 26/02/2019 proves the receipt is day-first");
    }

    [Fact]
    public void Scoreboard()
    {
        int merchant = 0, date = 0, total = 0, tax = 0, taxDocs = 0;
        var sb = new StringBuilder();
        foreach (var f in SpanishReceiptCorpus.All)
        {
            var r = Run(f);
            var m = Key(r.Merchant) == Key(f.Merchant);
            var d = r.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) == f.Date;
            var t = r.TotalAmount == f.Total;
            merchant += m ? 1 : 0;
            date += d ? 1 : 0;
            total += t ? 1 : 0;
            if (f.Tax is not null)
            {
                taxDocs++;
                tax += TaxOf(r) == f.Tax ? 1 : 0;
            }

            sb.AppendLine(CultureInfo.InvariantCulture, $"{f.Name,-34} merchant={(m ? "ok" : "X")} date={(d ? "ok" : "X")} total={(t ? "ok" : "X " + r.TotalAmount)} tax={TaxOf(r)}");
        }

        var n = SpanishReceiptCorpus.All.Length;
        output.WriteLine(sb.ToString());
        output.WriteLine($"SCOREBOARD docs={n} merchant={merchant}/{n} date={date}/{n} total={total}/{n} tax={tax}/{taxDocs}");
    }
}
