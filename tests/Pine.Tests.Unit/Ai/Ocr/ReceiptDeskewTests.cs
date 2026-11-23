using FluentAssertions;
using Pine.Core.Ai;
using Pine.Core.Transactions;
using Pine.Infrastructure.Ai.Extraction;
using Pine.Infrastructure.Ai.Vision;
using Xunit.Abstractions;

namespace Pine.Tests.Unit.Ai.Ocr;

/// <summary>Levelling a tilted receipt before it is read (docs/65 section 14), with the real ONNX engine.</summary>
public sealed class ReceiptDeskewTests(ITestOutputHelper output)
{
    private static OcrWord Line(float angle, float width = 200, float height = 20) => new()
    {
        Text = "x",
        AngleDegrees = angle,
        BoundingBox = new BoundingBox { X = 0, Y = 0, Width = width, Height = height },
    };

    [Fact]
    public void The_dominant_angle_is_the_median_of_the_wide_lines_and_ignores_level_pages_and_tall_boxes()
    {
        ReceiptDeskew.DominantAngle([Line(8), Line(9), Line(7.5f), Line(8.2f), Line(30), Line(8.1f)]).Should().BeApproximately(8.1, 0.2);
        ReceiptDeskew.DominantAngle([Line(1), Line(2), Line(1.5f), Line(0.5f), Line(2.5f)]).Should().BeNull("under 3 degrees is level enough");
        ReceiptDeskew.DominantAngle([Line(8), Line(9), Line(7)]).Should().BeNull("too few lines to trust");
        ReceiptDeskew.DominantAngle([Line(60), Line(61), Line(62), Line(59), Line(60)]).Should().BeNull("a quarter turn is not a tilt");
        ReceiptDeskew.DominantAngle([Line(8, 20, 20), Line(8, 20, 20), Line(8, 20, 20), Line(8, 20, 20), Line(8, 20, 20)]).Should().BeNull("square boxes carry no direction");
    }

    private static byte[] Tilt(byte[] encoded, double degrees) => degrees == 0 ? encoded : ReceiptDeskew.RotateBy(encoded, degrees);

    [RequiresOcrModelsFact]
    public async Task A_tilted_clean_scan_is_found_tilted_and_read_level()
    {
        using var engine = new OnnxOcrEngine(OcrTestAssets.Options());
        var tilted = Tilt(File.ReadAllBytes(OcrTestAssets.ImagePath(OcrTestAssets.ParityIds[0])), 11);

        var plain = await engine.ExtractTextAsync(new MemoryStream(tilted), CancellationToken.None);
        var angle = ReceiptDeskew.DominantAngle(plain);
        output.WriteLine($"detected angle {angle:F1} (drawn: 11 clockwise)");
        angle.Should().NotBeNull();
        angle!.Value.Should().BeApproximately(11, 3);

        var level = await ReceiptDeskew.ReadLevelAsync(engine, tilted);
        level.RemovedDegrees.Should().BeApproximately(11, 3);
        ReceiptDeskew.DominantAngle(level.Words).Should().BeNull("after levelling the text runs level");
    }

    [RequiresOcrModelsFact]
    public async Task The_tilted_east_repair_invoice_reads_three_items_tax_and_total_at_every_angle()
    {
        var path = Environment.GetEnvironmentVariable("PINE_EASTREPAIR_PNG")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "invoice-template-us-classic-white-750px.png");
        if (!File.Exists(path))
        {
            output.WriteLine("invoice picture not found; set PINE_EASTREPAIR_PNG. Skipped.");
            return;
        }

        using var engine = new OnnxOcrEngine(OcrTestAssets.Options());
        var original = await File.ReadAllBytesAsync(path);
        var failures = new List<string>();
        foreach (var degrees in new double[] { 0, 5, 8, 12, 20, -12 })
        {
            var picture = Tilt(original, degrees);
            var without = Summarise(await engine.ExtractTextAsync(new MemoryStream(picture), CancellationToken.None));
            var level = await ReceiptDeskew.ReadLevelAsync(engine, picture);
            var with = Summarise(level.Words);
            output.WriteLine($"{degrees,4} deg  without: {without}   with deskew (removed {level.RemovedDegrees:F1}): {with}");
            if (!with.StartsWith("items=3 tax=9.06 total=154.06", StringComparison.Ordinal))
            {
                failures.Add($"{degrees}: {with}");
            }
        }

        failures.Should().BeEmpty("every tilt up to 20 degrees must read as 3 items, tax 9.06, total 154.06 after levelling");
    }

    private static string Summarise(IReadOnlyList<OcrWord> words)
    {
        var r = new HeuristicFieldExtractor().ExtractFields(words.ToList());
        var items = r.LineItems?.Count(i => i.Kind == LineItemKind.Product) ?? 0;
        return $"items={items} tax={r.TaxAmount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"} total={r.TotalAmount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"}";
    }
}
