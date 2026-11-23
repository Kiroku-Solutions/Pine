using System.Text.Json;
using FluentAssertions;
using Pine.Core.Ai;
using Pine.Infrastructure.Ai.Vision;
using Xunit.Abstractions;

namespace Pine.Tests.Unit.Ai.Ocr;

public sealed class OnnxOcrEngineTests(ITestOutputHelper output)
{
    private static string FixtureImage() => Path.Combine(OcrTestAssets.FixtureDirectory(), "onnx_ocr_receipt_synthetic.png");

    [Fact]
    public async Task Missing_models_are_reported_as_an_unavailable_capability_not_a_crash()
    {
        using var engine = new OnnxOcrEngine(OnnxOcrOptions.ForDirectory(Path.Combine(Path.GetTempPath(), "pine-no-ocr-models-" + Guid.NewGuid().ToString("N"))));
        await using var image = File.OpenRead(FixtureImage());

        var act = async () => await engine.ExtractTextAsync(image, CancellationToken.None);

        await act.Should().ThrowAsync<AiCapabilityUnavailableException>();
    }

    [Fact]
    public async Task A_disposed_engine_refuses_work()
    {
        var engine = new OnnxOcrEngine(OnnxOcrOptions.ForDirectory(null));
        engine.Dispose();
        engine.Dispose(); // idempotent
        await using var image = File.OpenRead(FixtureImage());

        var act = async () => await engine.ExtractTextAsync(image, CancellationToken.None);

        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public void The_default_options_name_the_three_shipped_files_beside_the_application()
    {
        var options = OnnxOcrOptions.ForDirectory(null);

        options.DetectionModelPath.Should().Be(Path.Combine(AppContext.BaseDirectory, "Assets", "Ai", "ocr", "det.onnx"));
        options.RecognitionModelPath.Should().EndWith("rec.onnx");
        options.DictionaryPath.Should().EndWith("dict_latin_v5.txt");
    }

    [RequiresOcrModelsFact]
    public async Task The_synthetic_receipt_reads_like_the_python_reference()
    {
        using var engine = new OnnxOcrEngine(OcrTestAssets.Options());
        await using var image = File.OpenRead(FixtureImage());

        var words = await engine.ExtractTextAsync(image, CancellationToken.None);

        using var expected = JsonDocument.Parse(File.ReadAllText(Path.Combine(OcrTestAssets.FixtureDirectory(), "onnx_ocr_receipt_expected.json")));
        var reference = new Dictionary<string, List<OcrTestAssets.Line>>
        {
            ["receipt"] = expected.RootElement.GetProperty("words").EnumerateArray()
                .Select(w =>
                {
                    var b = w.GetProperty("box");
                    return new OcrTestAssets.Line(w.GetProperty("text").GetString()!, b.GetProperty("x").GetDouble(), b.GetProperty("y").GetDouble(), b.GetProperty("width").GetDouble(), b.GetProperty("height").GetDouble());
                })
                .ToList(),
        };
        var actual = new Dictionary<string, List<OcrTestAssets.Line>>
        {
            ["receipt"] = words.Select(w => new OcrTestAssets.Line(w.Text, w.BoundingBox.X, w.BoundingBox.Y, w.BoundingBox.Width, w.BoundingBox.Height)).ToList(),
        };

        var result = OcrComparison.Compare(reference, actual, ["receipt"]);
        output.WriteLine(result.ToString());

        result.MatchRate.Should().BeGreaterThanOrEqualTo(0.99, "every line the reference found is found in the same place");
        result.Cer.Should().BeLessThanOrEqualTo(0.015);
        words.Select(w => w.Text).Should().Contain(t => t.Contains("TOTAL", StringComparison.Ordinal));
        words.Should().OnlyContain(w => w.Confidence > 0 && w.BoundingBox.Width > 0 && w.BoundingBox.Height > 0);
    }

    [RequiresOcrModelsFact]
    public async Task Scanning_twice_with_one_engine_gives_the_same_answer_and_cancellation_is_honoured()
    {
        using var engine = new OnnxOcrEngine(OcrTestAssets.Options());
        await using var first = File.OpenRead(FixtureImage());
        var a = await engine.ExtractTextAsync(first, CancellationToken.None);
        await using var second = File.OpenRead(FixtureImage());
        var b = await engine.ExtractTextAsync(second, CancellationToken.None);

        a.Select(w => w.Text).Should().Equal(b.Select(w => w.Text));

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await using var third = File.OpenRead(FixtureImage());
        var act = async () => await engine.ExtractTextAsync(third, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
