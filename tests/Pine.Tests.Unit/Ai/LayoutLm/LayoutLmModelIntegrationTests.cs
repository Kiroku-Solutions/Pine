using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using OpenCvSharp;
using Pine.Core.Ai;
using Pine.Infrastructure.Ai.Extraction;
using Pine.Infrastructure.Ai.Extraction.LayoutLm;
using Xunit.Abstractions;

namespace Pine.Tests.Unit.Ai.LayoutLm;

/// <summary>
/// Runs the REAL model. Skipped (reported as skipped, not passed) when the model or tokenizer is absent:
/// set PINE_LAYOUTLM_MODEL / PINE_LAYOUTLM_TOKENIZER or keep the files under research/. TD-36 was an
/// extractor that never ran its model; these tests exist to prove this one does.
/// </summary>
public sealed partial class LayoutLmModelIntegrationTests(ITestOutputHelper output)
{
    private sealed record Sample(string Id, string Source, string ImagePath, int Width, int Height, List<OcrWord> Words, Dictionary<string, string?> Truth);

    [GeneratedRegex(@"[^\p{L}\p{N}]")]
    private static partial Regex NonAlphanumeric();

    private static LayoutLmFieldExtractor NewExtractor() =>
        new(LayoutLmTestAssets.ModelPath(), LayoutLmTestAssets.TokenizerPath());

    private static List<Sample> LoadSamples(int perSource)
    {
        var samples = new List<Sample>();
        var root = LayoutLmTestAssets.ProcessedDataDirectory();
        foreach (var source in new[] { "cord", "sroie", "wildreceipt" })
        {
            var labels = Path.Combine(root, source, "labels");
            if (!Directory.Exists(labels))
            {
                continue;
            }

            var taken = 0;
            foreach (var file in Directory.EnumerateFiles(labels, "*.json").Order(StringComparer.Ordinal))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var e = doc.RootElement;
                var truth = new Dictionary<string, string?>();
                foreach (var p in e.GetProperty("ground_truth").EnumerateObject())
                {
                    truth[p.Name] = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : null;
                }

                if (truth.Values.Count(v => v is not null) < 2)
                {
                    continue;
                }

                var words = e.GetProperty("words").EnumerateArray().Select(w =>
                {
                    var b = w.GetProperty("box");
                    float x0 = b[0].GetSingle(), y0 = b[1].GetSingle(), x1 = b[2].GetSingle(), y1 = b[3].GetSingle();
                    return LayoutLmTestAssets.Word(w.GetProperty("text").GetString() ?? string.Empty, x0, y0, x1 - x0, y1 - y0);
                }).ToList();

                samples.Add(new Sample(
                    e.GetProperty("id").GetString()!, source,
                    Path.Combine(root, source, e.GetProperty("image").GetString()!),
                    e.GetProperty("width").GetInt32(), e.GetProperty("height").GetInt32(), words, truth));
                if (++taken == perSource)
                {
                    break;
                }
            }
        }

        return samples;
    }

    [RequiresLayoutLmModelFact]
    public void The_model_really_runs_and_its_output_depends_on_words_and_pixels()
    {
        var samples = LoadSamples(2);
        samples.Should().NotBeEmpty("research/data/processed holds receipts with ground truth");
        using var extractor = NewExtractor();
        extractor.InferenceCount.Should().Be(0);

        var a = samples[0];
        var b = samples[^1];
        var imageA = File.ReadAllBytes(a.ImagePath);
        var logitsA = extractor.RawLogitsOfFirstWindow(a.Words, imageA);
        extractor.InferenceCount.Should().Be(1, "session.Run was executed once");

        // Valid probability rows, not a constant: the model produces non-trivial, per-token outputs.
        for (var t = 0; t < logitsA.Length / 9; t++)
        {
            logitsA.AsSpan(t * 9, 9).ToArray().Sum().Should().BeApproximately(1f, 1e-3f);
        }

        var perTokenArgMax = Enumerable.Range(0, logitsA.Length / 9)
            .Select(t => Array.IndexOf(logitsA[(t * 9)..(t * 9 + 9)], logitsA[(t * 9)..(t * 9 + 9)].Max())).Distinct().Count();
        perTokenArgMax.Should().BeGreaterThan(1, "a real classifier labels different tokens differently");

        // Different words/boxes give different logits...
        var logitsB = extractor.RawLogitsOfFirstWindow(b.Words, File.ReadAllBytes(b.ImagePath));
        extractor.InferenceCount.Should().Be(2);
        (logitsA.Length != logitsB.Length || !logitsA.SequenceEqual(logitsB)).Should().BeTrue();

        // ...and the same words over a blank page differ from the real page: pixel_values are consumed.
        using var blank = new Mat(a.Height, a.Width, MatType.CV_8UC3, Scalar.All(255));
        Cv2.ImEncode(".png", blank, out var blankPng);
        var logitsBlank = extractor.RawLogitsOfFirstWindow(a.Words, blankPng);
        logitsBlank.Should().HaveSameCount(logitsA);
        logitsBlank.SequenceEqual(logitsA).Should().BeFalse("the image input changes the prediction");
        var maxDelta = logitsA.Zip(logitsBlank, (x, y) => Math.Abs(x - y)).Max();
        output.WriteLine($"max |p(real page) - p(blank page)| over {logitsA.Length / 9} tokens = {maxDelta:F4}");
    }

    [RequiresLayoutLmModelFact]
    public void Smoke_test_on_real_receipts_reports_how_many_fields_are_right()
    {
        var samples = LoadSamples(2).Take(6).ToList();
        samples.Should().NotBeEmpty();
        using var extractor = NewExtractor();
        int right = 0, scored = 0;

        foreach (var s in samples)
        {
            var prediction = extractor.Predict(s.Words, File.ReadAllBytes(s.ImagePath));
            var line = new List<string>();
            foreach (var (field, name) in new[] { (LayoutLmField.Merchant, "MERCHANT"), (LayoutLmField.Date, "DATE"), (LayoutLmField.Total, "TOTAL"), (LayoutLmField.Tax, "TAX") })
            {
                if (!s.Truth.TryGetValue(name, out var truth) || truth is null)
                {
                    continue;
                }

                scored++;
                var found = prediction.Spans.TryGetValue(field, out var span) ? span.Text : null;
                var ok = found is not null && Matches(field, found, truth);
                right += ok ? 1 : 0;
                line.Add($"{name}:{(ok ? "OK" : "MISS")}");
            }

            output.WriteLine($"{s.Source}/{s.Id}: {string.Join(' ', line)} (model fields found: {prediction.Spans.Count})");
        }

        output.WriteLine($"SMOKE RESULT: {right}/{scored} annotated fields correct on {samples.Count} real receipts (informational, not a pass criterion)");
        extractor.InferenceCount.Should().BeGreaterThanOrEqualTo(samples.Count);
    }

    private static bool Matches(LayoutLmField field, string found, string truth)
    {
        if (field == LayoutLmField.Total || field == LayoutLmField.Tax)
        {
            return LayoutLmFieldExtractor.TryParseAmount(found, out var a)
                && LayoutLmFieldExtractor.TryParseAmount(truth, out var b) && a == b;
        }

        return string.Equals(NonAlphanumeric().Replace(found, string.Empty), NonAlphanumeric().Replace(truth, string.Empty), StringComparison.OrdinalIgnoreCase);
    }

    [RequiresLayoutLmModelFact]
    public async Task The_provider_guard_overlays_the_models_fields_on_a_real_page()
    {
        var s = LoadSamples(1).First(x => x.Source == "sroie");
        using var extractor = NewExtractor();
        var guard = new VisualExtractionGuard(extractor, null);
        var heuristic = new HeuristicFieldExtractor().ExtractFields(s.Words);
        using var image = new MemoryStream(File.ReadAllBytes(s.ImagePath));

        var merged = await guard.OverlayAsync(heuristic, s.Words, image, CancellationToken.None);

        extractor.InferenceCount.Should().BeGreaterThan(0);
        guard.ConsecutiveFailures.Should().Be(0);
        output.WriteLine($"heuristic total={heuristic.TotalAmount}, model-overlaid total={merged.TotalAmount}, truth={s.Truth["TOTAL"]}");
    }
}
