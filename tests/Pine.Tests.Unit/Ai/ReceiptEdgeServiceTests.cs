using FluentAssertions;
using Pine.Core.Ai;
using Pine.Infrastructure.Ai.Vision;
using SkiaSharp;

namespace Pine.Tests.Unit.Ai;

/// <summary>
/// The edge service behind the edge editor: find, crop, turn. Rotations matter because a person turns the device (and
/// a receipt lies sideways) while scanning.
/// </summary>
public sealed class ReceiptEdgeServiceTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static byte[] Sheet(int width = 900, int height = 700)
    {
        using var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(new SKColor(60, 60, 60));
            using var paper = new SKPaint { Color = new SKColor(240, 240, 240), IsAntialias = true };
            canvas.DrawRect(new SKRect(250, 80, 650, 620), paper); // a tall receipt, 400 x 540
        }

        using var image = SKImage.FromBitmap(bitmap);
        return image.Encode(SKEncodedImageFormat.Jpeg, 92).ToArray();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_point_found_in_a_turned_picture_maps_back_to_the_original(bool clockwise)
    {
        using var original = new SKBitmap(400, 250, SKColorType.Bgra8888, SKAlphaType.Opaque);
        original.Erase(SKColors.White);
        for (var y = 58; y < 62; y++)
        {
            for (var x = 118; x < 122; x++)
            {
                original.SetPixel(x, y, SKColors.Black);
            }
        }

        using var turned = SkiaReceiptDetector.RotateBitmap(original, clockwise);
        turned.Width.Should().Be(250);
        turned.Height.Should().Be(400);
        double sx = 0, sy = 0;
        var n = 0;
        for (var y = 0; y < turned.Height; y++)
        {
            for (var x = 0; x < turned.Width; x++)
            {
                if (turned.GetPixel(x, y).Red < 50)
                {
                    sx += x;
                    sy += y;
                    n++;
                }
            }
        }

        n.Should().BeGreaterThan(8);
        var back = SkiaReceiptDetector.UnrotatePoint((sx / n, sy / n), original.Width, original.Height, clockwise);
        back.X.Should().BeApproximately(120, 2);
        back.Y.Should().BeApproximately(60, 2);
    }

    [Fact]
    public async Task Edges_are_found_cropped_and_the_crop_has_the_receipts_shape()
    {
        using var detector = new SkiaReceiptDetector();

        var found = await detector.FindEdgesAsync(new MemoryStream(Sheet()));

        found.Quad.Should().NotBeNull();
        found.Quad!.TopLeft.X.Should().BeApproximately(250 / 900.0, 0.03);
        found.Quad.BottomRight.Y.Should().BeApproximately(620 / 700.0, 0.03);

        await using var crop = await detector.CropAsync(new MemoryStream(found.UprightJpeg), found.Quad);
        using var cropped = SKBitmap.Decode(crop);
        cropped.Width.Should().BeInRange(380, 420);
        cropped.Height.Should().BeInRange(520, 560);
    }

    [Fact]
    public async Task A_moved_corner_changes_the_crop_and_a_turn_swaps_the_sides()
    {
        using var detector = new SkiaReceiptDetector();
        var found = await detector.FindEdgesAsync(new MemoryStream(Sheet()));
        var smaller = found.Quad! with { BottomRight = new NormalizedPoint(0.6, 0.6), BottomLeft = new NormalizedPoint(0.3, 0.6) };

        await using var crop = await detector.CropAsync(new MemoryStream(found.UprightJpeg), smaller);
        using var cropped = SKBitmap.Decode(crop);
        cropped.Height.Should().BeLessThan(480);

        await using var turned = await detector.RotateAsync(new MemoryStream(found.UprightJpeg), clockwise: true);
        using var turnedBitmap = SKBitmap.Decode(turned);
        (turnedBitmap.Width, turnedBitmap.Height).Should().Be((700, 900));
    }

    [Fact]
    public async Task A_picture_without_a_receipt_gives_no_corners_but_still_an_image()
    {
        using var detector = new SkiaReceiptDetector();
        using var bitmap = new SKBitmap(300, 200, SKColorType.Bgra8888, SKAlphaType.Opaque);
        bitmap.Erase(new SKColor(90, 90, 90));
        using var image = SKImage.FromBitmap(bitmap);

        var found = await detector.FindEdgesAsync(new MemoryStream(image.Encode(SKEncodedImageFormat.Jpeg, 90).ToArray()));

        found.Quad.Should().BeNull();
        found.UprightJpeg.Should().NotBeEmpty();
    }

    [YoloRotationFact]
    public async Task The_model_still_finds_a_receipt_when_the_picture_is_turned()
    {
        var images = Path.Combine(FindRoot(), "research", "data", "roboflow", "raw_coco", "test");
        using var yolo = YoloReceiptSegmenter.TryCreate(null);
        using var detector = new SkiaReceiptDetector(yolo);
        var tried = 0;
        var found = new int[4]; // 0, 90, 180, 270
        foreach (var file in Directory.EnumerateFiles(images, "*.jpg").Take(30))
        {
            using var upright = SKBitmap.Decode(file);
            var reference = await detector.DetectEdgesAsync(new MemoryStream(File.ReadAllBytes(file)));
            if (reference is null)
            {
                continue;
            }

            tried++;
            found[0]++;
            var current = upright;
            for (var turn = 1; turn <= 3; turn++)
            {
                var next = SkiaReceiptDetector.RotateBitmap(current, clockwise: true);
                if (turn > 1)
                {
                    current.Dispose();
                }

                current = next;
                using var image = SKImage.FromBitmap(current);
                var quad = await detector.DetectEdgesAsync(new MemoryStream(image.Encode(SKEncodedImageFormat.Jpeg, 92).ToArray()));
                if (quad is not null)
                {
                    found[turn]++;
                }
            }

            current.Dispose();
        }

        output.WriteLine($"photos={tried} found at 0/90/180/270 = {string.Join("/", found)}");
        tried.Should().BeGreaterThan(5);
        found[1].Should().BeGreaterThanOrEqualTo((int)(tried * 0.8));
        found[3].Should().BeGreaterThanOrEqualTo((int)(tried * 0.8));
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "research", "data", "roboflow", "raw_coco", "test")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? string.Empty;
    }

    internal sealed class YoloRotationFactAttribute : FactAttribute
    {
        public YoloRotationFactAttribute()
        {
            if (FindRoot().Length == 0 || YoloReceiptSegmenter.TryCreate(null) is null)
            {
                Skip = "research/data/roboflow images or yolo-receipt-nano.onnx (Git LFS) not present.";
            }
        }
    }
}
