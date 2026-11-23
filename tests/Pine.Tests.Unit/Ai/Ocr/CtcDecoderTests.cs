using System.Text.Json;
using FluentAssertions;
using Pine.Infrastructure.Ai.Vision;

namespace Pine.Tests.Unit.Ai.Ocr;

/// <summary>CTC decoding: hand-made matrices for the rules, and golden matrices from the real recogniser (Python reference).</summary>
public sealed class CtcDecoderTests
{
    private static string[] Charset => ["blank", "a", "b", "c", " "];

    private static float[] Steps(params int[] argmax)
    {
        var classes = Charset.Length;
        var data = new float[argmax.Length * classes];
        for (var t = 0; t < argmax.Length; t++)
        {
            data[(t * classes) + argmax[t]] = 0.9f;
        }

        return data;
    }

    [Fact]
    public void Repeats_collapse_and_blanks_separate_genuine_repeats()
    {
        // a a blank a b b -> "aab"
        var (text, confidence) = CtcDecoder.Decode(Steps(1, 1, 0, 1, 2, 2), 6, 5, Charset);

        text.Should().Be("aab");
        confidence.Should().BeApproximately(0.9f, 1e-6f);
    }

    [Fact]
    public void All_blank_is_an_empty_line_with_zero_confidence()
    {
        var (text, confidence) = CtcDecoder.Decode(Steps(0, 0, 0), 3, 5, Charset);

        text.Should().BeEmpty();
        confidence.Should().Be(0f);
    }

    [Fact]
    public void A_class_beyond_the_charset_is_dropped_but_still_breaks_repeats()
    {
        var charset = new[] { "blank", "a" };
        var data = new float[4 * 3];
        data[(0 * 3) + 1] = 1;
        data[(1 * 3) + 2] = 1; // class 2 does not exist
        data[(2 * 3) + 1] = 1;
        data[(3 * 3) + 0] = 1;

        CtcDecoder.Decode(data, 4, 3, charset).Text.Should().Be("aa");
    }

    [Fact]
    public void The_charset_is_blank_then_the_dictionary_then_a_space_and_survives_crlf()
    {
        var charset = CtcDecoder.BuildCharset(["x\r", "y\r"]);

        charset.Should().Equal("blank", "x", "y", " ");
    }

    [Fact]
    public void The_shipped_dictionary_gives_838_classes()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Ai", "ocr", "dict_latin_v5.txt");
        File.Exists(path).Should().BeTrue("the dictionary is a plain text asset that ships with the build");

        CtcDecoder.LoadCharset(path).Should().HaveCount(838);
    }

    [Fact]
    public void Real_recogniser_output_decodes_to_the_reference_text()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Ai", "ocr", "dict_latin_v5.txt");
        var charset = CtcDecoder.LoadCharset(path);
        var cases = JsonDocument.Parse(File.ReadAllText(Path.Combine(OcrTestAssets.FixtureDirectory(), "onnx_ocr_ctc_fixtures.json")))
            .RootElement.GetProperty("cases").EnumerateArray().ToArray();

        cases.Should().NotBeEmpty();
        foreach (var c in cases)
        {
            var steps = c.GetProperty("steps").GetInt32();
            var classes = c.GetProperty("classes").GetInt32();
            var matrix = new float[steps * classes];
            var t = 0;
            foreach (var step in c.GetProperty("sparse").EnumerateArray())
            {
                foreach (var entry in step.EnumerateArray())
                {
                    matrix[(t * classes) + entry[0].GetInt32()] = entry[1].GetSingle();
                }

                t++;
            }

            var (text, confidence) = CtcDecoder.Decode(matrix, steps, classes, charset);

            text.Should().Be(c.GetProperty("text").GetString());
            confidence.Should().BeApproximately(c.GetProperty("confidence").GetSingle(), 0.005f);
        }
    }
}
