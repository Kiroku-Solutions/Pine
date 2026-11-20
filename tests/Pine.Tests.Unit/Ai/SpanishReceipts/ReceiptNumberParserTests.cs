using System;
using FluentAssertions;
using Pine.Core.Ai;
using Xunit;

namespace Pine.Tests.Unit.Ai.SpanishReceipts;

/// <summary>The shared number reader: one table covers heuristics, LayoutLM decoding and the document-level separator.</summary>
public class ReceiptNumberParserTests
{
    // text, separator hint, expected value (null = not an amount)
    public static readonly TheoryData<string, DecimalSeparator, double?> Table = new()
    {
        { "1.234,56", DecimalSeparator.Unknown, 1234.56 },
        { "1,234.56", DecimalSeparator.Unknown, 1234.56 },
        { "2,08", DecimalSeparator.Unknown, 2.08 },
        { "2.08", DecimalSeparator.Unknown, 2.08 },
        { "$2,08", DecimalSeparator.Unknown, 2.08 },
        { "$ 2,08", DecimalSeparator.Unknown, 2.08 },
        { "2 08", DecimalSeparator.Unknown, 2.08 },
        { "0,5", DecimalSeparator.Unknown, 0.5 },
        { "0,500", DecimalSeparator.Unknown, 0.5 },
        { "1.591.600", DecimalSeparator.Unknown, 1591600 },
        { "1,591,600", DecimalSeparator.Unknown, 1591600 },
        { "1 234 567", DecimalSeparator.Unknown, 1234567 },
        { "TOTAL: 9,99 EUR", DecimalSeparator.Unknown, 9.99 },
        { "S/ 16.00", DecimalSeparator.Unknown, 16.00 },
        { "-5,00", DecimalSeparator.Unknown, -5.00 },
        { "(5.00)", DecimalSeparator.Unknown, -5.00 },
        { "1O,5O", DecimalSeparator.Unknown, 10.50 },      // OCR read zeros as the letter O
        { "l2,00", DecimalSeparator.Unknown, 12.00 },      // and a one as lower-case L
        // "x,yyy" / "x.yyy": thousands without evidence, a decimal when the document says that separator is the decimal one.
        { "1.000", DecimalSeparator.Unknown, 1000 },
        { "1,000", DecimalSeparator.Unknown, 1000 },
        { "2,500", DecimalSeparator.Unknown, 2500 },
        { "1.000", DecimalSeparator.Point, 1.0 },
        { "2,500", DecimalSeparator.Comma, 2.5 },
        { "1,000", DecimalSeparator.Point, 1000 },
        { "1.000", DecimalSeparator.Comma, 1000 },
        { "35.250", DecimalSeparator.Point, 35.25 },
        { "1178.100", DecimalSeparator.Unknown, 1178100 },      // OCR-damaged grouped number, not a 3-decimal quantity
        // A percentage is a rate, never an amount.
        { "12,00%", DecimalSeparator.Unknown, null },
        { "6.25 %", DecimalSeparator.Unknown, null },
        { "TOTAL", DecimalSeparator.Unknown, null },
    };

    [Theory]
    [MemberData(nameof(Table))]
    public void Text_parses_to_the_expected_value(string text, DecimalSeparator separator, double? expected)
    {
        var ok = ReceiptNumberParser.TryParse(text, separator, out var value);
        if (expected is null)
        {
            ok.Should().BeFalse();
        }
        else
        {
            ok.Should().BeTrue();
            value.Should().Be((decimal)expected.Value);
        }
    }

    [Theory]
    [InlineData(new[] { "SUBTOTAL 2,08", "IVA 0,25", "TOTAL 2,33", "LECHE 1,000 KG" }, DecimalSeparator.Comma)]
    [InlineData(new[] { "SUBTOTAL 2.08", "IVA 0.25", "TOTAL 2.33", "QTY 1.000" }, DecimalSeparator.Point)]
    [InlineData(new[] { "TOTAL 1.591.600", "EFECTIVO 2.000.000" }, DecimalSeparator.Comma)]
    [InlineData(new[] { "TOTAL 1,591,600", "EFECTIVO 2,000,000" }, DecimalSeparator.Point)]
    [InlineData(new[] { "TOTAL 60.000", "IVA 5.000" }, DecimalSeparator.Unknown)]
    [InlineData(new[] { "IVA 12,00%", "FECHA 05.03.2026" }, DecimalSeparator.Unknown)]
    [InlineData(new[] { "TAX 10.00 %", "TOTAL 54,600", "4.964" }, DecimalSeparator.Unknown)]
    public void Document_separator_is_inferred_from_every_amount(string[] lines, DecimalSeparator expected) =>
        ReceiptNumberParser.Infer(lines).Should().Be(expected);

    [Fact]
    public void Layoutlm_and_the_shared_parser_agree()
    {
        Pine.Infrastructure.Ai.Extraction.LayoutLm.LayoutLmFieldExtractor.TryParseAmount("1,591,600", out var a).Should().BeTrue();
        a.Should().Be(1591600m);
        Pine.Infrastructure.Ai.Extraction.LayoutLm.LayoutLmFieldExtractor.TryParseAmount("2,500", out var b, DecimalSeparator.Comma).Should().BeTrue();
        b.Should().Be(2.5m);
    }

    [Theory]
    [InlineData("20 de noviembre de 2026", "2026-11-20")]
    [InlineData("Guayaquil, 5 de Enero del 2026", "2026-01-05")]
    [InlineData("18-NOV-2026 21:07", "2026-11-18")]
    [InlineData("15 nov. 2026", "2026-11-15")]
    [InlineData("noviembre 15, 2026", "2026-11-15")]
    [InlineData("FECHA EMISION 15 de septiembre de 2026", "2026-09-15")]
    public void Dates_with_month_names_parse_without_ambiguity(string text, string expected)
    {
        var parsed = ReceiptDateParser.Parse(text, now: new DateTimeOffset(2026, 11, 20, 0, 0, 0, TimeSpan.Zero));
        parsed.Should().NotBeNull();
        parsed!.Value.IsAmbiguous.Should().BeFalse();
        parsed.Value.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture).Should().Be(expected);
    }

    [Fact]
    public void A_day_above_twelve_anywhere_on_the_receipt_settles_the_order_of_every_date()
    {
        ReceiptDateParser.InferDayFirst(["Invoice Date 11/02/2019", "Due Date 26/02/2019"]).Should().BeTrue();
        ReceiptDateParser.InferDayFirst(["Date 02/26/2019"]).Should().BeFalse();
        ReceiptDateParser.InferDayFirst(["Date 11/02/2019"]).Should().BeNull();

        var parsed = ReceiptDateParser.Parse("11/02/2019", dayFirst: true, now: new DateTimeOffset(2026, 11, 20, 0, 0, 0, TimeSpan.Zero));
        parsed!.Value.IsAmbiguous.Should().BeFalse();
        parsed.Value.Value.Month.Should().Be(2);
    }
}
