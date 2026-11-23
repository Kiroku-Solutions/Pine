using FluentAssertions;
using Pine.Core.Ai;
using Pine.Infrastructure.Ai.Extraction.LayoutLm;

namespace Pine.Tests.Unit.Ai.LayoutLm;

public sealed class LayoutLmMappingAndFallbackTests
{
    private static ReceiptExtractionResult Heuristic() => new(
        "Unknown", 10m, "USD", null, ReceiptConfidence.None,
        [new ReceiptLineItem("Item", 1, 10m, 10m)]);

    [Theory]
    [InlineData("$12.50", 12.50)]
    [InlineData("1,591,600", 1591600)]
    [InlineData("1.591.600", 1591600)]
    [InlineData("1,234.56", 1234.56)]
    [InlineData("1.234,56", 1234.56)]
    [InlineData("60.000", 60000)]
    [InlineData("TOTAL: 9,99 EUR", 9.99)]
    public void Amounts_parse_with_the_heuristic_separator_rules(string text, double expected)
    {
        LayoutLmFieldExtractor.TryParseAmount(text, out var amount).Should().BeTrue();
        amount.Should().Be((decimal)expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("TOTAL")]
    [InlineData("0.00")]
    public void Text_without_a_positive_amount_is_not_an_amount(string text)
    {
        LayoutLmFieldExtractor.TryParseAmount(text, out _).Should().BeFalse();
    }

    [Fact]
    public void A_prediction_maps_to_a_receipt_with_tax_date_and_per_field_confidence()
    {
        var spans = new Dictionary<LayoutLmField, LayoutLmSpan>
        {
            [LayoutLmField.Merchant] = new(LayoutLmField.Merchant, "SUPER  MART", 0.95, 0, 2),
            [LayoutLmField.Date] = new(LayoutLmField.Date, "2026-03-12", 0.80, 3, 1),
            [LayoutLmField.Total] = new(LayoutLmField.Total, "$ 9.50", 0.60, 6, 2),
            [LayoutLmField.Tax] = new(LayoutLmField.Tax, "0.90", 0.99, 8, 1),
        };

        var result = LayoutLmFieldExtractor.ToReceipt(new LayoutLmPrediction(spans));

        result.Merchant.Should().Be("SUPER MART");
        result.TotalAmount.Should().Be(9.50m);
        result.TaxAmount.Should().Be(0.90m);
        result.Date!.Value.Date.Should().Be(new DateTime(2026, 3, 12));
        result.Confidence.Merchant.Band.Should().Be(ConfidenceBand.High);
        result.Confidence.Total.Score.Should().Be(0.60);
        result.Confidence.Date.Band.Should().Be(ConfidenceBand.Medium);
    }

    [Fact]
    public void An_unparseable_field_is_left_null_so_the_heuristics_keep_theirs()
    {
        var spans = new Dictionary<LayoutLmField, LayoutLmSpan>
        {
            [LayoutLmField.Total] = new(LayoutLmField.Total, "abc", 0.9, 0, 1),
            [LayoutLmField.Date] = new(LayoutLmField.Date, "not a date", 0.9, 1, 1),
        };

        var result = LayoutLmFieldExtractor.ToReceipt(new LayoutLmPrediction(spans));

        result.TotalAmount.Should().BeNull();
        result.Date.Should().BeNull();
        result.Confidence.Total.Band.Should().Be(ConfidenceBand.NotFound);
    }

    [Fact]
    public void Overlay_replaces_only_the_fields_the_model_found_and_keeps_line_items()
    {
        var layout = new ReceiptExtractionResult(
            "Mart", null, null, new DateTimeOffset(2026, 3, 12, 0, 0, 0, TimeSpan.Zero),
            new ReceiptConfidence(new FieldConfidence(0.9, ConfidenceBand.High), FieldConfidence.NotFound, new FieldConfidence(0.8, ConfidenceBand.Medium), []),
            TaxAmount: 1.5m);

        var merged = LayoutLmResultMerger.Overlay(Heuristic(), layout);

        merged.Merchant.Should().Be("Mart");
        merged.TotalAmount.Should().Be(10m, "the model did not find a total");
        merged.Date.Should().NotBeNull();
        merged.TaxAmount.Should().Be(1.5m);
        merged.CurrencyCode.Should().Be("USD");
        merged.LineItems.Should().HaveCount(1);
        merged.Confidence.Merchant.Score.Should().Be(0.9);
    }

    private sealed class ThrowingExtractor : IVisualFieldExtractor
    {
        public int Calls { get; private set; }

        public ReceiptExtractionResult ExtractFields(IReadOnlyList<OcrWord> ocrWords, ReadOnlyMemory<byte> encodedImage)
        {
            Calls++;
            throw new FileNotFoundException("model missing");
        }
    }

    [Fact]
    public async Task A_failing_model_never_breaks_the_scan_and_is_logged_without_content()
    {
        var extractor = new ThrowingExtractor();
        var logger = new CapturingLogger();
        var guard = new VisualExtractionGuard(extractor, logger);
        var heuristic = Heuristic();
        using var image = new MemoryStream([1, 2, 3]);

        var result = await guard.OverlayAsync(heuristic, [LayoutLmTestAssets.Word("SECRET MERCHANT 42.00", 0, 0, 1, 1)], image, CancellationToken.None);

        result.Should().BeSameAs(heuristic);
        logger.Messages.Should().ContainSingle().Which.Should().NotContain("SECRET");
    }

    [Fact]
    public async Task After_repeated_failures_the_model_is_left_alone()
    {
        var extractor = new ThrowingExtractor();
        var guard = new VisualExtractionGuard(extractor, null);
        using var image = new MemoryStream([1]);

        for (var i = 0; i < 6; i++)
        {
            await guard.OverlayAsync(Heuristic(), [], image, CancellationToken.None);
        }

        extractor.Calls.Should().Be(VisualExtractionGuard.MaxConsecutiveFailures);
    }

    [Fact]
    public async Task Without_a_model_the_heuristic_result_is_returned_untouched()
    {
        var guard = new VisualExtractionGuard(null, null);
        var heuristic = Heuristic();
        using var image = new MemoryStream();

        (await guard.OverlayAsync(heuristic, [], image, CancellationToken.None)).Should().BeSameAs(heuristic);
    }

    [Fact]
    public void The_extractor_reports_a_missing_model_by_throwing_so_the_caller_can_fall_back()
    {
        using var extractor = new LayoutLmFieldExtractor("does-not-exist.onnx", "does-not-exist.json");

        var act = () => extractor.ExtractFields([LayoutLmTestAssets.Word("a", 0, 0, 1, 1)], new byte[] { 1 });

        act.Should().Throw<FileNotFoundException>();
        extractor.InferenceCount.Should().Be(0);
    }

    [Fact]
    public void Options_resolve_the_tokenizer_beside_the_model_and_are_off_without_a_model()
    {
        new LayoutLmOptions().IsConfigured.Should().BeFalse();
        var options = new LayoutLmOptions { ModelPath = Path.Combine("models", "m.onnx") };
        options.IsConfigured.Should().BeTrue();
        options.ResolveTokenizerPath().Should().Be(Path.Combine("models", "tokenizer.json"));
        new LayoutLmOptions { ModelPath = "m.onnx", Enabled = false }.IsConfigured.Should().BeFalse();
    }

    private sealed class CapturingLogger : Microsoft.Extensions.Logging.ILogger
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));
    }
}
