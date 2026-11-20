using FluentAssertions;
using Pine.Infrastructure.Ai.Vision;
using SkiaSharp;

namespace Pine.Tests.Unit.Ai;

/// <summary>
/// B2: the Skia receipt detector finds the sheet in synthetic pictures (a receipt drawn rotated or in perspective on a
/// background), straightens it, and falls back to the plain EXIF + downscale behaviour when there is nothing to find.
/// </summary>
public sealed class SkiaReceiptDetectorTests
{
    private const int Width = 1200;
    private const int Height = 900;

    private static byte[] Picture(
        (float X, float Y)[]? quad,
        byte background = 70,
        byte paper = 245,
        int width = Width,
        int height = Height)
    {
        using var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(new SKColor(background, background, background));
            if (quad is not null)
            {
                using var path = new SKPath();
                path.MoveTo(quad[0].X, quad[0].Y);
                for (var i = 1; i < 4; i++)
                {
                    path.LineTo(quad[i].X, quad[i].Y);
                }

                path.Close();
                using var fill = new SKPaint { Color = new SKColor(paper, paper, paper), IsAntialias = true };
                canvas.DrawPath(path, fill);

                // Printed lines inside the sheet, which must not break the detection.
                using var ink = new SKPaint { Color = new SKColor(165, 165, 165), StrokeWidth = 3, IsAntialias = true };
                canvas.Save();
                canvas.ClipPath(path, antialias: true);
                var minY = quad.Min(p => p.Y);
                var maxY = quad.Max(p => p.Y);
                for (var y = minY + 40; y < maxY - 20; y += 22)
                {
                    canvas.DrawLine(quad.Min(p => p.X) + 20, y, quad.Max(p => p.X) - 20, y, ink);
                }

                canvas.Restore();
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        return image.Encode(SKEncodedImageFormat.Jpeg, 92).ToArray();
    }

    /// <summary>A w x h rectangle centred at (cx, cy) and turned by <paramref name="degrees"/>, clockwise from top-left.</summary>
    private static (float X, float Y)[] Rotated(float cx, float cy, float w, float h, float degrees)
    {
        var rad = degrees * Math.PI / 180;
        var cos = (float)Math.Cos(rad);
        var sin = (float)Math.Sin(rad);
        (float X, float Y) Turn(float x, float y) => (cx + (x * cos) - (y * sin), cy + (x * sin) + (y * cos));
        return [Turn(-w / 2, -h / 2), Turn(w / 2, -h / 2), Turn(w / 2, h / 2), Turn(-w / 2, h / 2)];
    }

    private static SKBitmap Decode(Stream stream)
    {
        stream.Position = 0;
        return SKBitmap.Decode(stream);
    }

    /// <summary>The share of pixels in a thin strip along the outer edge that are background-dark (the crop must not include background; the printed lines are mid-grey).</summary>
    private static double BackgroundEdgeShare(SKBitmap bitmap, int inset = 6)
    {
        var dark = 0;
        var total = 0;
        for (var y = inset; y < bitmap.Height - inset; y++)
        {
            for (var x = inset; x < bitmap.Width - inset; x++)
            {
                var onEdge = x < inset + 4 || y < inset + 4 || x >= bitmap.Width - inset - 4 || y >= bitmap.Height - inset - 4;
                if (!onEdge)
                {
                    continue;
                }

                total++;
                var c = bitmap.GetPixel(x, y);
                if (c.Red < 100)
                {
                    dark++;
                }
            }
        }

        return dark / (double)total;
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(12f)]
    [InlineData(-25f)]
    [InlineData(70f)]
    public async Task A_rotated_receipt_is_found_and_straightened(float degrees)
    {
        var quad = Rotated(600, 450, 340, 760, degrees);
        using var detector = new SkiaReceiptDetector();

        var result = await detector.AnalyzeAndCropReceiptAsync(new MemoryStream(Picture(quad)));

        result.PassedFilters.Should().BeTrue();
        result.CroppedImageStream.Should().NotBeNull();
        result.OcrImageStream.Should().NotBeNull();
        using var cropped = Decode(result.CroppedImageStream!);
        var longSide = Math.Max(cropped.Width, cropped.Height);
        var shortSide = Math.Min(cropped.Width, cropped.Height);
        longSide.Should().BeInRange(720, 800, "the receipt is 760 long");
        shortSide.Should().BeInRange(315, 365, "the receipt is 340 wide");
        BackgroundEdgeShare(cropped).Should().BeLessThan(0.05, "the crop should be paper right up to its edges");
    }

    [Fact]
    public async Task A_receipt_seen_in_perspective_is_rectified()
    {
        // The top edge is shorter than the bottom edge, as when the phone is held tilted.
        (float X, float Y)[] quad = [(430, 90), (800, 110), (930, 820), (260, 790)];
        using var detector = new SkiaReceiptDetector();

        var result = await detector.AnalyzeAndCropReceiptAsync(new MemoryStream(Picture(quad)));

        result.PassedFilters.Should().BeTrue();
        using var cropped = Decode(result.CroppedImageStream!);
        cropped.Height.Should().BeGreaterThan(cropped.Width);
        BackgroundEdgeShare(cropped).Should().BeLessThan(0.08);
    }

    [Fact]
    public async Task A_dark_receipt_on_a_light_table_is_found()
    {
        var quad = Rotated(600, 450, 380, 700, 8);
        using var detector = new SkiaReceiptDetector();

        var result = await detector.AnalyzeAndCropReceiptAsync(new MemoryStream(Picture(quad, background: 235, paper: 60)));

        result.PassedFilters.Should().BeTrue();
        using var cropped = Decode(result.CroppedImageStream!);
        Math.Max(cropped.Width, cropped.Height).Should().BeInRange(660, 740);
    }

    [Fact]
    public async Task A_picture_with_no_receipt_falls_back_to_the_original_bytes_when_it_is_small()
    {
        using var detector = new SkiaReceiptDetector();

        var result = await detector.AnalyzeAndCropReceiptAsync(new MemoryStream(Picture(null)));

        result.PassedFilters.Should().BeFalse("the provider reads that as 'use the original bytes'");
        result.OcrImageStream.Should().BeNull();
    }

    [Fact]
    public async Task A_big_picture_with_no_receipt_is_still_downscaled()
    {
        using var detector = new SkiaReceiptDetector();

        var result = await detector.AnalyzeAndCropReceiptAsync(new MemoryStream(Picture(null, width: 3000, height: 2000)));

        result.PassedFilters.Should().BeTrue();
        using var scaled = Decode(result.OcrImageStream!);
        Math.Max(scaled.Width, scaled.Height).Should().BeInRange(1500, SkiaReceiptDetector.MaxSide);
    }

    [Fact]
    public async Task A_sheet_that_fills_the_frame_is_not_cropped()
    {
        (float X, float Y)[] quad = [(0, 0), (Width, 0), (Width, Height), (0, Height)];
        using var detector = new SkiaReceiptDetector();

        var result = await detector.AnalyzeAndCropReceiptAsync(new MemoryStream(Picture(quad)));

        result.CroppedImageStream.Should().BeNull("there is nothing to crop away");
    }

    [Fact]
    public async Task A_tiny_speck_is_not_taken_for_a_receipt()
    {
        var quad = Rotated(600, 450, 60, 90, 10);
        using var detector = new SkiaReceiptDetector();

        var result = await detector.AnalyzeAndCropReceiptAsync(new MemoryStream(Picture(quad)));

        result.PassedFilters.Should().BeFalse();
    }

    [Fact]
    public async Task Detection_confidence_is_one_for_a_sheet_and_zero_without()
    {
        using var detector = new SkiaReceiptDetector();

        (await detector.DetectReceiptConfidenceAsync(new MemoryStream(Picture(Rotated(600, 450, 340, 760, 15))))).Should().Be(1f);
        (await detector.DetectReceiptAsync(new MemoryStream(Picture(null)))).Should().BeFalse();
    }

    [Fact]
    public void The_finder_locates_the_corners_of_a_plain_rectangle_within_a_few_pixels()
    {
        const int w = 200;
        const int h = 160;
        var grey = new byte[w * h];
        Array.Fill(grey, (byte)40);
        for (var y = 30; y < 130; y++)
        {
            for (var x = 50; x < 140; x++)
            {
                grey[(y * w) + x] = 230;
            }
        }

        var quad = ReceiptQuadFinder.Find(grey, w, h);

        quad.Should().NotBeNull();
        (double X, double Y)[] expected = [(50, 30), (140, 30), (140, 130), (50, 130)];
        for (var i = 0; i < 4; i++)
        {
            quad![i].X.Should().BeApproximately(expected[i].X, 3);
            quad[i].Y.Should().BeApproximately(expected[i].Y, 3);
        }
    }

    [Fact]
    public void The_finder_returns_nothing_for_a_flat_picture()
    {
        var grey = new byte[100 * 100];
        Array.Fill(grey, (byte)128);

        ReceiptQuadFinder.Find(grey, 100, 100).Should().BeNull();
    }

    [Fact]
    public void The_homography_maps_each_corner_onto_its_target()
    {
        (double X, double Y)[] from = [(430, 90), (800, 110), (930, 820), (260, 790)];
        (double X, double Y)[] to = [(0, 0), (400, 0), (400, 700), (0, 700)];

        var h = SkiaReceiptDetector.Homography(from, to);

        h.Should().NotBeNull();
        for (var i = 0; i < 4; i++)
        {
            var d = (h![6] * from[i].X) + (h[7] * from[i].Y) + 1;
            ((h[0] * from[i].X) + (h[1] * from[i].Y) + h[2]).Should().BeApproximately(to[i].X * d, 1e-6 * Math.Abs(d) + 1e-6);
            ((h[3] * from[i].X) + (h[4] * from[i].Y) + h[5]).Should().BeApproximately(to[i].Y * d, 1e-6 * Math.Abs(d) + 1e-6);
        }
    }
}
