using System.Text.Json;
using FluentAssertions;
using Pine.Infrastructure.Ai.Vision;

namespace Pine.Tests.Unit.Ai.Ocr;

/// <summary>
/// Golden tests for the DB post-processing: the probability maps and the expected boxes come from the Python
/// reference (research/scripts/gen_onnx_ocr_csharp_fixtures.py), so the C# port is held to it.
/// </summary>
public sealed class DbPostProcessorTests
{
    private static JsonElement Cases() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(OcrTestAssets.FixtureDirectory(), "onnx_ocr_db_fixtures.json"))).RootElement.GetProperty("cases");

    public static TheoryData<string> CaseNames()
    {
        var data = new TheoryData<string>();
        foreach (var c in Cases().EnumerateArray())
        {
            data.Add(c.GetProperty("name").GetString()!);
        }

        return data;
    }

    private static float[] Decode(string base64)
    {
        var bytes = Convert.FromBase64String(base64);
        return bytes.Select(b => b / 255f).ToArray();
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Boxes_match_the_python_reference_within_one_pixel(string name)
    {
        var c = Cases().EnumerateArray().Single(x => x.GetProperty("name").GetString() == name);
        var map = Decode(c.GetProperty("map").GetString()!);

        var boxes = DbPostProcessor.Process(
            map,
            c.GetProperty("mapWidth").GetInt32(),
            c.GetProperty("mapHeight").GetInt32(),
            c.GetProperty("origWidth").GetInt32(),
            c.GetProperty("origHeight").GetInt32(),
            c.GetProperty("thresh").GetSingle(),
            c.GetProperty("boxThresh").GetSingle(),
            c.GetProperty("unclip").GetDouble());

        var expected = c.GetProperty("boxes").EnumerateArray().ToArray();
        var scores = c.GetProperty("scores").EnumerateArray().Select(s => s.GetSingle()).ToArray();

        boxes.Should().HaveCount(expected.Length, "the reference found {0} boxes in '{1}'", expected.Length, name);
        for (var i = 0; i < expected.Length; i++)
        {
            for (var p = 0; p < 4; p++)
            {
                boxes[i].Quad[p].X.Should().BeApproximately(expected[i][p][0].GetDouble(), 1.0, $"box {i} corner {p} x");
                boxes[i].Quad[p].Y.Should().BeApproximately(expected[i][p][1].GetDouble(), 1.0, $"box {i} corner {p} y");
            }

            boxes[i].Score.Should().BeApproximately(scores[i], 0.01f, $"box {i} score");
        }
    }

    [Fact]
    public void The_real_receipt_map_yields_several_lines_so_the_golden_cases_are_not_vacuous()
    {
        var real = Cases().EnumerateArray().Single(x => x.GetProperty("name").GetString() == "receipt_small_same_size");

        real.GetProperty("boxes").GetArrayLength().Should().BeGreaterThan(3);
        Cases().EnumerateArray().Single(x => x.GetProperty("name").GetString() == "synthetic_tilted_tall_tiny_weak")
            .GetProperty("boxes").GetArrayLength().Should().Be(2, "the tilted bar and the tall bar survive; the tiny blob and the weak bar are dropped");
    }

    [Fact]
    public void An_empty_map_has_no_boxes()
    {
        DbPostProcessor.Process(new float[32 * 32], 32, 32, 64, 64).Should().BeEmpty();
    }

    [Fact]
    public void A_map_that_does_not_match_its_stated_size_is_refused()
    {
        var act = () => DbPostProcessor.Process(new float[10], 32, 32, 32, 32);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Boxes_are_scaled_to_the_original_image_and_clamped_to_it()
    {
        var map = new float[64 * 64];
        for (var y = 20; y < 36; y++)
        {
            for (var x = 4; x < 60; x++)
            {
                map[(y * 64) + x] = 0.95f;
            }
        }

        var boxes = DbPostProcessor.Process(map, 64, 64, 128, 256);

        boxes.Should().HaveCount(1);
        boxes[0].Quad.Should().OnlyContain(p => p.X >= 0 && p.X <= 128 && p.Y >= 0 && p.Y <= 256);
        boxes[0].Quad[0].Y.Should().BeLessThan(boxes[0].Quad[3].Y, "top-left is above bottom-left");
        boxes[0].Quad[1].X.Should().BeGreaterThan(boxes[0].Quad[0].X, "top-right is right of top-left");
    }

    [Fact]
    public void Two_boxes_on_one_row_are_ordered_left_to_right()
    {
        var map = new float[64 * 160];
        void Bar(int x0, int x1)
        {
            for (var y = 20; y < 36; y++)
            {
                for (var x = x0; x < x1; x++)
                {
                    map[(y * 160) + x] = 0.95f;
                }
            }
        }

        Bar(90, 150);
        Bar(10, 70);

        var boxes = DbPostProcessor.Process(map, 160, 64, 160, 64);

        boxes.Should().HaveCount(2);
        boxes[0].Quad[0].X.Should().BeLessThan(boxes[1].Quad[0].X);
    }
}
