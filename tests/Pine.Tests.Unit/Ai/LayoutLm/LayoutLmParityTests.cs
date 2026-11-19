using System.Text.Json;
using FluentAssertions;
using OpenCvSharp;
using Pine.Core.Ai;
using Pine.Infrastructure.Ai.Extraction.LayoutLm;
using Xunit.Abstractions;

namespace Pine.Tests.Unit.Ai.LayoutLm;

/// <summary>
/// Parity with the Hugging Face reference. The expected ids and pixels come from
/// <c>research/scripts/gen_layoutlm_csharp_fixtures.py</c> (the <c>tokenizers</c> library and PIL).
/// </summary>
public sealed class LayoutLmParityTests(ITestOutputHelper output)
{
    private static JsonElement Fixtures() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(LayoutLmTestAssets.FixtureDirectory(), "layoutlm_tokenizer_fixtures.json"))).RootElement;

    [RequiresLayoutLmTokenizerFact]
    public void Every_single_word_fixture_has_exactly_the_reference_ids()
    {
        var tokenizer = LayoutLmTokenizer.Load(LayoutLmTestAssets.TokenizerPath());
        var matched = 0;
        var total = 0;

        foreach (var item in Fixtures().GetProperty("single").EnumerateArray())
        {
            var word = item.GetProperty("word").GetString()!;
            var expected = item.GetProperty("ids").EnumerateArray().Select(e => e.GetInt32()).ToArray();
            total++;
            tokenizer.EncodeWord(word).Should().Equal(expected, $"word '{word}'");
            matched++;
        }

        output.WriteLine($"single-word parity: {matched}/{total}");
        total.Should().BeGreaterThan(30);
    }

    [RequiresLayoutLmTokenizerFact]
    public void Document_windows_have_the_reference_ids_word_ids_and_overlap()
    {
        var tokenizer = LayoutLmTokenizer.Load(LayoutLmTestAssets.TokenizerPath());

        foreach (var doc in Fixtures().GetProperty("docs").EnumerateArray())
        {
            var name = doc.GetProperty("name").GetString();
            var words = doc.GetProperty("words").EnumerateArray()
                .Select((w, i) => LayoutLmTestAssets.Word(w.GetString()!, 0, i, 10, 5)).ToList();

            var chunks = LayoutLmEncoder.Encode(words, 100, 1000, tokenizer, out _);
            var expected = doc.GetProperty("chunks").EnumerateArray().ToList();

            // The 'long' fixture (10 reference windows) is deliberately beyond MaxChunks: the first MaxChunks must be identical.
            chunks.Should().HaveCount(Math.Min(expected.Count, LayoutLmEncoder.MaxChunks), $"windows of '{name}'");
            for (var c = 0; c < chunks.Count; c++)
            {
                chunks[c].InputIds.Should().Equal(expected[c].GetProperty("ids").EnumerateArray().Select(e => e.GetInt32()), $"'{name}' window {c} ids");
                chunks[c].WordIds.Should().Equal(expected[c].GetProperty("word_ids").EnumerateArray().Select(e => e.GetInt32()), $"'{name}' window {c} word ids");
            }

            output.WriteLine($"document '{name}': {chunks.Count} window(s) identical to the reference");
        }
    }

    [Theory]
    [InlineData("down")]
    [InlineData("up")]
    public void Resize_agrees_with_PIL_bilinear_within_one_grey_level(string name)
    {
        var dir = LayoutLmTestAssets.FixtureDirectory();
        var (source, sw, sh) = LoadRgb(Path.Combine(dir, $"img_{name}_src.png"));
        var (expected, _, _) = LoadRgb(Path.Combine(dir, $"img_{name}_expected224.png"));

        var actual = LayoutLmImagePreprocessor.ResizeBilinear(source, sw, sh, 224, 224);

        var maxDiff = 0;
        long sum = 0;
        for (var i = 0; i < actual.Length; i++)
        {
            var d = Math.Abs(actual[i] - expected[i]);
            maxDiff = Math.Max(maxDiff, d);
            sum += d;
        }

        output.WriteLine($"{name} ({sw}x{sh} -> 224x224): max |diff| = {maxDiff}/255, mean = {(double)sum / actual.Length:F4}");
        maxDiff.Should().BeLessThanOrEqualTo(1);
    }

    [Fact]
    public void Process_decodes_normalises_to_minus_one_one_and_reports_the_original_size()
    {
        var bytes = File.ReadAllBytes(Path.Combine(LayoutLmTestAssets.FixtureDirectory(), "img_down_src.png"));

        var image = LayoutLmImagePreprocessor.Process(bytes);

        image.Width.Should().Be(301);
        image.Height.Should().Be(421);
        image.PixelValues.Should().HaveCount(3 * 224 * 224);
        image.PixelValues.Min().Should().BeGreaterThanOrEqualTo(-1f);
        image.PixelValues.Max().Should().BeLessThanOrEqualTo(1f);
    }

    [Fact]
    public void Undecodable_bytes_throw()
    {
        var act = () => LayoutLmImagePreprocessor.Process([1, 2, 3]);

        act.Should().Throw<Exception>();
    }

    private static (byte[] Rgb, int Width, int Height) LoadRgb(string path)
    {
        using var mat = Cv2.ImRead(path, ImreadModes.Color);
        mat.GetArray(out Vec3b[] bgr);
        var rgb = new byte[bgr.Length * 3];
        for (var i = 0; i < bgr.Length; i++)
        {
            rgb[i * 3] = bgr[i].Item2;
            rgb[i * 3 + 1] = bgr[i].Item1;
            rgb[i * 3 + 2] = bgr[i].Item0;
        }

        return (rgb, mat.Width, mat.Height);
    }
}
