using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Pine.Infrastructure.Ai.Vision;
using Xunit.Abstractions;

namespace Pine.Tests.Unit.Ai.Ocr;

/// <summary>
/// docs/65 section 9, B1 step 4: the C# engine against the Python reference on the 20 test-split images of
/// section 5. Needs research/data (gitignored) and the reference run, so it is SKIPPED, not failed, in CI.
/// The small committed-fixture test that does run in CI is OnnxOcrEngineTests.
/// </summary>
public sealed class OnnxOcrParityTests(ITestOutputHelper output)
{
    [RequiresOcrParityDataFact]
    public async Task The_csharp_engine_reproduces_the_python_reference_on_the_twenty_test_images()
    {
        var reference = OcrTestAssets.LoadRun(OcrTestAssets.ParityReferencePath());
        var ids = OcrTestAssets.ParityIds;
        var actual = new Dictionary<string, List<OcrTestAssets.Line>>();
        var seconds = new List<double>();

        using (var engine = new OnnxOcrEngine(OcrTestAssets.Options()))
        {
            foreach (var id in ids)
            {
                await using var stream = File.OpenRead(OcrTestAssets.ImagePath(id));
                var clock = Stopwatch.StartNew();
                var words = await engine.ExtractTextAsync(stream, CancellationToken.None);
                seconds.Add(clock.Elapsed.TotalSeconds);
                actual[id] = words
                    .Select(w => new OcrTestAssets.Line(w.Text, w.BoundingBox.X, w.BoundingBox.Y, w.BoundingBox.Width, w.BoundingBox.Height))
                    .ToList();
                output.WriteLine($"{id}: {words.Count} lines in {seconds[^1]:F2} s");
            }
        }

        // The Python reference keeps lines that decoded to nothing; the engine does not return them.
        var nonEmptyReference = reference.ToDictionary(p => p.Key, p => p.Value.Where(l => !string.IsNullOrWhiteSpace(l.Text)).ToList());

        var vsReference = OcrComparison.Compare(nonEmptyReference, actual, ids);
        output.WriteLine("C# vs Python ONNX reference: " + vsReference);
        output.WriteLine($"latency: mean {seconds.Average():F2} s, max {seconds.Max():F2} s, total {seconds.Sum():F1} s (includes first-scan model load)");
        output.WriteLine($"GT word recall (len>=3): C# {Recall(actual, ids):P1}, Python reference {Recall(nonEmptyReference, ids):P1}");

        if (File.Exists(OcrTestAssets.ParityPaddlePath()))
        {
            var paddle = OcrTestAssets.LoadRun(OcrTestAssets.ParityPaddlePath());
            output.WriteLine("C# vs Paddle (pine960, section 5 baseline): " + OcrComparison.Compare(paddle, actual, ids));
            output.WriteLine("Python ONNX vs Paddle (section 5 for comparison): " + OcrComparison.Compare(paddle, nonEmptyReference, ids));
            output.WriteLine($"GT word recall: Paddle {Recall(paddle, ids):P1}");
        }

        vsReference.MatchRate.Should().BeGreaterThanOrEqualTo(0.99, "docs/65 section 9 item 4: at least 99 % line match against the Python reference");
        vsReference.Cer.Should().BeLessThanOrEqualTo(0.015, "docs/65 section 9 item 4: at most 1.5 % character error rate against the Python reference");
    }

    /// <summary>Ground-truth words of three or more characters found in the page text (the measure of eval2.py).</summary>
    private static double Recall(Dictionary<string, List<OcrTestAssets.Line>> run, IEnumerable<string> ids)
    {
        int total = 0, hit = 0;
        foreach (var id in ids)
        {
            var page = string.Concat(run[id].Select(l => OcrComparison.Norm(l.Text)));
            using var label = JsonDocument.Parse(File.ReadAllText(OcrTestAssets.LabelPath(id)));
            foreach (var w in label.RootElement.GetProperty("words").EnumerateArray())
            {
                var g = OcrComparison.Norm(w.GetProperty("text").GetString() ?? string.Empty);
                if (g.Length >= 3)
                {
                    total++;
                    hit += page.Contains(g, StringComparison.Ordinal) ? 1 : 0;
                }
            }
        }

        return total == 0 ? 0 : (double)hit / total;
    }
}
