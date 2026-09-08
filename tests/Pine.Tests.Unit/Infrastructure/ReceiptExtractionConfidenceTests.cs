using System;
using System.Collections.Generic;
using FluentAssertions;
using Pine.Core.Ai;
using Pine.Infrastructure.Ai.Extraction;
using Xunit;

namespace Pine.Tests.Unit.Infrastructure;

public class ReceiptExtractionConfidenceTests
{
    private static OcrWord Word(string text, float x, float y, float confidence) => new()
    {
        Text = text,
        Confidence = confidence,
        BoundingBox = new BoundingBox { X = x, Y = y, Width = 10 * text.Length, Height = 10 },
    };

    private static ReceiptExtractionResult Extract(params OcrWord[] words)
    {
        // Not `using` any more: the extractor stopped being IDisposable when its unused
        // InferenceSession was deleted (TD-36). It holds nothing that needs releasing.
        var extractor = new HeuristicFieldExtractor();
        return extractor.ExtractFields(words);
    }

    [Fact]
    public void A_badly_read_total_reports_a_low_band()
    {
        var result = Extract(
            Word("ACME", 0, 0, 0.99f),
            Word("TOTAL", 0, 100, 0.98f),
            Word("24.50", 100, 100, 0.41f));

        result.Confidence.Total.Band.Should().Be(ConfidenceBand.Low);
        result.Confidence.Total.Score.Should().BeApproximately(0.41, 0.01);
    }

    [Fact]
    public void A_merchant_that_was_never_on_the_page_is_NotFound()
    {
        var result = Extract(
            Word("TOTAL", 0, 100, 0.98f),
            Word("24.50", 100, 100, 0.97f));

        result.Confidence.Merchant.Band.Should().Be(ConfidenceBand.NotFound);
    }

    [Fact]
    public void An_empty_word_list_produces_no_confidence_and_no_invented_date()
    {
        var result = Extract();

        result.Confidence.Should().Be(ReceiptConfidence.None);
        result.Date.Should().BeNull();
    }

    [Fact]
    public void An_ambiguous_receipt_date_is_flagged_with_its_alternative()
    {
        var result = Extract(
            Word("FECHA:", 0, 50, 0.99f),
            Word("05/03/2026", 100, 50, 0.99f),
            Word("TOTAL", 0, 100, 0.98f),
            Word("24.50", 100, 100, 0.97f));

        result.DateIsAmbiguous.Should().BeTrue();
        result.AlternativeDate.Should().NotBeNull();
        result.AlternativeDate!.Value.Date.Should().NotBe(result.Date!.Value.Date);
    }

    [Fact]
    public void A_NaN_confidence_on_the_total_does_not_throw_and_reports_low_band()
    {
        var result = Extract(
            Word("ACME", 0, 0, 0.99f),
            Word("TOTAL", 0, 100, 0.98f),
            Word("24.50", 100, 100, float.NaN));

        result.Confidence.Total.Band.Should().Be(ConfidenceBand.Low);
        result.Confidence.Total.Score.Should().BeApproximately(0.0, 0.01);
    }

    [Fact]
    public void An_infinite_confidence_on_the_total_does_not_throw_and_reports_low_band()
    {
        var result = Extract(
            Word("ACME", 0, 0, 0.99f),
            Word("TOTAL", 0, 100, 0.98f),
            Word("24.50", 100, 100, float.PositiveInfinity));

        result.Confidence.Total.Band.Should().Be(ConfidenceBand.Low);
        result.Confidence.Total.Score.Should().BeApproximately(0.0, 0.01);
    }
}
