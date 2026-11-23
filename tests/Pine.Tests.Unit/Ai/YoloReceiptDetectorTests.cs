using FluentAssertions;
using Pine.Infrastructure.Ai.Vision;
using SkiaSharp;

namespace Pine.Tests.Unit.Ai;

/// <summary>
/// The portable YOLO detector: post-processing on fabricated model output (no runtime), and the real model against
/// the OpenCV detector on real receipt photos when the research data is present.
/// </summary>
public sealed class YoloReceiptDetectorTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private const int Channels = 4 + 1 + YoloPostProcessor.MaskChannels;

    /// <summary>Model output for one confident anchor whose mask is a filled rectangle (in 160 x 160 mask cells).</summary>
    private static (float[] Output, float[] Protos) Fabricate(int anchor, float confidence, int x0, int y0, int x1, int y1)
    {
        var output = new float[Channels * YoloPostProcessor.AnchorCount];
        output[(2 * YoloPostProcessor.AnchorCount) + anchor] = 300; // w
        output[(3 * YoloPostProcessor.AnchorCount) + anchor] = 500; // h
        output[(4 * YoloPostProcessor.AnchorCount) + anchor] = confidence;
        output[((4 + 1) * YoloPostProcessor.AnchorCount) + anchor] = 8; // coefficient of prototype 0

        var protos = new float[YoloPostProcessor.MaskChannels * YoloPostProcessor.MaskSize * YoloPostProcessor.MaskSize];
        for (var y = 0; y < YoloPostProcessor.MaskSize; y++)
        {
            for (var x = 0; x < YoloPostProcessor.MaskSize; x++)
            {
                protos[(y * YoloPostProcessor.MaskSize) + x] = x >= x0 && x < x1 && y >= y0 && y < y1 ? 1f : -1f;
            }
        }

        return (output, protos);
    }

    [Fact]
    public void The_letterbox_matches_the_opencv_detectors_geometry()
    {
        var box = YoloPostProcessor.ComputeLetterbox(1000, 2000);

        box.Ratio.Should().BeApproximately(0.32f, 1e-6f);
        (box.Width, box.Height, box.Left, box.Top).Should().Be((320, 640, 160, 0));
    }

    [Fact]
    public void The_best_anchor_wins_and_small_boxes_are_ignored()
    {
        var (output, _) = Fabricate(1234, 0.93f, 0, 0, 1, 1);
        output[(2 * YoloPostProcessor.AnchorCount) + 7] = 10; // tiny box with a higher score must not win
        output[(3 * YoloPostProcessor.AnchorCount) + 7] = 10;
        output[(4 * YoloPostProcessor.AnchorCount) + 7] = 0.99f;

        YoloPostProcessor.TryPickAnchor(output, Channels, out var anchor, out var confidence).Should().BeTrue();

        anchor.Should().Be(1234);
        confidence.Should().BeApproximately(0.93f, 1e-6f);
    }

    [Fact]
    public void A_mask_becomes_a_quad_in_image_coordinates()
    {
        // 1000 x 2000 picture: letterbox ratio 0.32, left 160. Mask cells 50..110 x 30..130 are 640-space x 200..440, y 120..520.
        var (output, protos) = Fabricate(5, 0.95f, 50, 30, 110, 130);
        var box = YoloPostProcessor.ComputeLetterbox(1000, 2000);

        var quad = YoloPostProcessor.QuadFromAnchor(output, Channels, 5, protos, box);

        quad.Should().NotBeNull();
        // Image x = (640x - left) / ratio = (200 - 160) / 0.32 = 125 .. (440 - 160) / 0.32 = 875; y = 375 .. 1625.
        quad![0].X.Should().BeApproximately(125, 25);
        quad[0].Y.Should().BeApproximately(375, 25);
        quad[2].X.Should().BeApproximately(875, 25);
        quad[2].Y.Should().BeApproximately(1625, 25);
        YoloPostProcessor.Accept(quad, 1000, 2000).Should().BeTrue();
    }

    [Theory]
    [InlineData(70, 60, 90, 100)] // a receipt far away: about 4 % of the picture
    [InlineData(2, 2, 158, 158)] // a close photo: the mask fills nearly everything
    public void A_small_or_all_filling_mask_still_gives_a_quad(int x0, int y0, int x1, int y1)
    {
        var (output, protos) = Fabricate(9, 0.9f, x0, y0, x1, y1);
        var box = YoloPostProcessor.ComputeLetterbox(1000, 1000);

        var quad = YoloPostProcessor.QuadFromAnchor(output, Channels, 9, protos, box);

        quad.Should().NotBeNull("a mask under 12 % of the picture, or over 95 %, used to be refused as 'nomask' and the crop was lost");
        YoloPostProcessor.Accept(quad!, 1000, 1000, strict: false).Should().BeTrue();
    }

    [RequiresYoloParityFact]
    public async Task A_large_photo_with_a_small_receipt_is_still_cropped_by_the_model()
    {
        var images = ResearchImagesDirectory()!;
        using var yolo = YoloReceiptSegmenter.TryCreate(null);
        using var detector = new SkiaReceiptDetector(yolo);
        var found = 0;
        var tried = 0;
        foreach (var file in Directory.EnumerateFiles(images, "*.jpg").Take(12))
        {
            using var source = SKBitmap.Decode(file);
            // A 12 MP picture (3000 x 4000) with the 640 px original small in the middle, as when the receipt is far from the camera.
            using var big = new SKBitmap(3000, 4000, SKColorType.Bgra8888, SKAlphaType.Opaque);
            using (var canvas = new SKCanvas(big))
            {
                canvas.Clear(new SKColor(70, 60, 55));
                canvas.DrawBitmap(source, new SKRect(700, 1300, 2300, 2700));
            }

            using var image = SKImage.FromBitmap(big);
            tried++;
            var quad = await detector.DetectEdgesAsync(new MemoryStream(image.Encode(SKEncodedImageFormat.Jpeg, 90).ToArray()));
            if (quad is not null)
            {
                found++;
            }
        }

        output.WriteLine($"12 MP pictures with a small receipt: found {found}/{tried}");
        found.Should().BeGreaterThanOrEqualTo(tried - 1);
    }

    [Fact]
    public void A_quad_touching_the_frame_or_nearly_square_is_refused_like_on_windows()
    {
        YoloPostProcessor.Accept([(5, 100), (500, 100), (500, 900), (5, 900)], 1000, 1000).Should().BeFalse("touches the edge");
        YoloPostProcessor.Accept([(100, 100), (500, 100), (500, 480), (100, 480)], 1000, 1000).Should().BeFalse("aspect below 1.15");
        YoloPostProcessor.Accept([(400, 400), (500, 400), (500, 500), (400, 500)], 1000, 1000).Should().BeFalse("under 10 % of the frame");
        YoloPostProcessor.Accept([(200, 100), (600, 100), (600, 900), (200, 900)], 1000, 1000).Should().BeTrue();
    }

    internal sealed class RequiresYoloParityFactAttribute : FactAttribute
    {
        public RequiresYoloParityFactAttribute()
        {
            if (ResearchImagesDirectory() is null || YoloReceiptSegmenter.TryCreate(null) is null)
            {
                Skip = "research/data/roboflow images or yolo-receipt-nano.onnx (Git LFS) not present.";
            }
        }
    }

    private static string? ResearchImagesDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "research", "data", "roboflow", "raw_coco", "test")))
        {
            dir = dir.Parent;
        }

        return dir is null ? null : Path.Combine(dir.FullName, "research", "data", "roboflow", "raw_coco", "test");
    }

    [RequiresYoloParityFact]
    public async Task On_real_photos_the_portable_detector_agrees_with_the_opencv_detector()
    {
        var images = ResearchImagesDirectory()!;
        using var yolo = YoloReceiptSegmenter.TryCreate(null);

        yolo!.StrictFilters = true; // the OpenCV detector's own refusals
        using var opencv = new ReceiptDetector();
        var compared = 0;
        var opencvFound = 0;
        var yoloFound = 0;
        var agreeing = 0;
        foreach (var file in Directory.EnumerateFiles(images, "*.jpg").Take(80))
        {
            var bytes = await File.ReadAllBytesAsync(file);
            using var bitmap = SKBitmap.Decode(bytes);
            var quad = yolo.FindQuad(bitmap, out _, out _);
            var b = await opencv.AnalyzeAndCropReceiptAsync(new MemoryStream(bytes));
            compared++;
            if (quad is not null)
            {
                yoloFound++;
            }

            if (!b.PassedFilters || b.CroppedImageStream is null)
            {
                continue;
            }

            opencvFound++;
            if (quad is null)
            {
                continue;
            }

            using var theirs = SKBitmap.Decode(Rewind(b.CroppedImageStream));
            var wOurs = Math.Max(Math.Sqrt(Math.Pow(quad[1].X - quad[0].X, 2) + Math.Pow(quad[1].Y - quad[0].Y, 2)), Math.Sqrt(Math.Pow(quad[2].X - quad[3].X, 2) + Math.Pow(quad[2].Y - quad[3].Y, 2)));
            var hOurs = Math.Max(Math.Sqrt(Math.Pow(quad[3].X - quad[0].X, 2) + Math.Pow(quad[3].Y - quad[0].Y, 2)), Math.Sqrt(Math.Pow(quad[2].X - quad[1].X, 2) + Math.Pow(quad[2].Y - quad[1].Y, 2)));
            // The crop's size is the quad's size: both detectors must agree on the sheet to within a few percent.
            if (Math.Abs(wOurs - theirs.Width) / theirs.Width < 0.08 && Math.Abs(hOurs - theirs.Height) / theirs.Height < 0.08)
            {
                agreeing++;
            }
        }

        var passedBoth = opencvFound;
        output.WriteLine($"compared={compared} opencvFound={opencvFound} yoloFound={yoloFound} sizeAgrees={agreeing}");
        compared.Should().BeGreaterThan(0);
        // Where OpenCV finds a receipt the portable detector must find the same one in most cases.
        if (passedBoth > 0)
        {
            agreeing.Should().BeGreaterThanOrEqualTo((int)Math.Ceiling(passedBoth * 0.7), $"{agreeing}/{passedBoth} agreed");
        }
    }

    [RequiresYoloParityFact]
    public void Against_the_labelled_test_set_the_detector_finds_the_document_and_the_refusals_are_the_filters()
    {
        var images = ResearchImagesDirectory()!;
        using var yolo = YoloReceiptSegmenter.TryCreate(null);
        using var labels = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(images, "_annotations.coco.json")));
        var boxes = new Dictionary<string, double[]>();
        var byId = labels.RootElement.GetProperty("images").EnumerateArray().ToDictionary(i => i.GetProperty("id").GetInt32(), i => i.GetProperty("file_name").GetString()!);
        foreach (var a in labels.RootElement.GetProperty("annotations").EnumerateArray())
        {
            boxes[byId[a.GetProperty("image_id").GetInt32()]] = a.GetProperty("bbox").EnumerateArray().Select(v => v.GetDouble()).ToArray();
        }

        var reasons = new Dictionary<string, int>();
        var total = 0;
        var ious = new List<double>();
        foreach (var name in boxes.Keys.Take(150))
        {
            using var bitmap = SKBitmap.Decode(Path.Combine(images, name));
            yolo!.FindQuad(bitmap, out var confidence, out _, out var reason, out var raw);
            total++;
            reasons[reason] = reasons.GetValueOrDefault(reason) + 1; if (reason.StartsWith("nomask", StringComparison.Ordinal)) { output.WriteLine(name + " " + reason + " conf=" + confidence); }
            if (raw is not null)
            {
                var gt = boxes[name];
                double x0 = raw.Min(p => p.X), x1 = raw.Max(p => p.X), y0 = raw.Min(p => p.Y), y1 = raw.Max(p => p.Y);
                var ix = Math.Max(0, Math.Min(x1, gt[0] + gt[2]) - Math.Max(x0, gt[0]));
                var iy = Math.Max(0, Math.Min(y1, gt[1] + gt[3]) - Math.Max(y0, gt[1]));
                var inter = ix * iy;
                ious.Add(inter / (((x1 - x0) * (y1 - y0)) + (gt[2] * gt[3]) - inter));
            }
        }

        output.WriteLine($"images={total} reasons={string.Join(",", reasons.Select(kv => kv.Key + ":" + kv.Value))} maskBoxIoU>0.7={ious.Count(v => v > 0.7)}/{ious.Count}");
        total.Should().BeGreaterThan(0);
        // Regression: the Android detector refused every photo whose receipt touched the frame or was squarish (the Windows
        // live-scanner guidance), so YOLO never cropped. With the relaxed filters nearly every labelled document is found.
        reasons.GetValueOrDefault("ok").Should().BeGreaterThanOrEqualTo((int)(total * 0.97), string.Join(",", reasons.Select(kv => kv.Key + ":" + kv.Value)));
    }

    private static Stream Rewind(Stream stream)
    {
        stream.Position = 0;
        return stream;
    }
}
