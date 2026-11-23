using System.Diagnostics;
using FluentAssertions;
using Pine.Infrastructure.Ai;
using Pine.Infrastructure.Ai.Vision;
using SkiaSharp;
using Xunit.Abstractions;

namespace Pine.Tests.Unit.Ai.Ocr;

/// <summary>
/// Timing of the scan stages on this machine with a 12 MP photo of a receipt (docs/65 section 14). Not a pass/fail speed test (CI
/// machines vary); it prints the stage times so a change can be compared before and after, and asserts only that the result is sane.
/// </summary>
public sealed class ScanSpeedBenchmarkTests(ITestOutputHelper output)
{
    private static byte[] TwelveMegapixelPhoto()
    {
        var path = Environment.GetEnvironmentVariable("PINE_EASTREPAIR_PNG")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "invoice-template-us-classic-white-750px.png");
        using var invoice = File.Exists(path) ? SKBitmap.Decode(path) : SKBitmap.Decode(OcrTestAssets.ImagePath(OcrTestAssets.ParityIds[0]));
        using var photo = new SKBitmap(3000, 4000, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using (var canvas = new SKCanvas(photo))
        {
            canvas.Clear(new SKColor(75, 65, 60));
            var height = 3300f;
            var width = height * invoice.Width / invoice.Height;
            canvas.DrawBitmap(invoice, new SKRect((3000 - width) / 2, 350, (3000 + width) / 2, 350 + height));
        }

        using var image = SKImage.FromBitmap(photo);
        return image.Encode(SKEncodedImageFormat.Jpeg, 90).ToArray();
    }

    [RequiresOcrModelsFact]
    public async Task Stage_times_of_a_12_megapixel_photo()
    {
        var photo = TwelveMegapixelPhoto();
        var lines = new List<string>();
        void Capture(string line) => lines.Add(line);
        ScanDiagnostics.Reported += Capture;
        try
        {
            using var yolo = YoloReceiptSegmenter.TryCreate(null);
            using var detector = new SkiaReceiptDetector(yolo);
            using var engine = new OnnxOcrEngine(OcrTestAssets.Options());

            // First pass loads the models; the second is the one a person sees after the warm-up.
            for (var pass = 1; pass <= 2; pass++)
            {
                lines.Clear();
                var clock = Stopwatch.StartNew();
                var analysis = await detector.AnalyzeAndCropReceiptAsync(new MemoryStream(photo));
                var prep = clock.ElapsedMilliseconds;
                clock.Restart();
                var words = await engine.ExtractTextAsync(analysis.OcrImageStream ?? new MemoryStream(photo), CancellationToken.None);
                var ocr = clock.ElapsedMilliseconds;
                output.WriteLine($"pass {pass}: prep_ms={prep} ocr_ms={ocr} lines={words.Count} cropped={analysis.CroppedImageStream is not null}");
                foreach (var line in lines)
                {
                    output.WriteLine("   " + line);
                }

                words.Count.Should().BeGreaterThan(5);
            }
        }
        finally
        {
            ScanDiagnostics.Reported -= Capture;
        }
    }
}

/// <summary>LayoutLM's image preparation decodes a reduced picture; its pixel values must stay within rounding of the full-size path.</summary>
public sealed class LayoutLmReducedDecodeTests(ITestOutputHelper output)
{
    [Fact]
    public void Reduced_decode_gives_the_same_224_pixels_within_a_grey_level_or_two_and_is_faster()
    {
        using var photo = new SKBitmap(3000, 4000, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using (var canvas = new SKCanvas(photo))
        {
            canvas.Clear(SKColors.White);
            using var ink = new SKPaint { Color = SKColors.Black, StrokeWidth = 5, IsAntialias = true };
            for (var y = 200; y < 3800; y += 60)
            {
                canvas.DrawLine(200, y, 200 + (((y * 7) % 2200) + 400), y, ink);
            }
        }

        using var image = SKImage.FromBitmap(photo);
        var jpeg = image.Encode(SKEncodedImageFormat.Jpeg, 90).ToArray();

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var reduced = Pine.Infrastructure.Ai.Extraction.LayoutLm.LayoutLmImagePreprocessor.Process(jpeg);
        var reducedMs = clock.ElapsedMilliseconds;

        clock.Restart();
        var full = SkiaImagePreprocessor.Decode(jpeg);
        var rgb = new byte[full.Width * full.Height * 3];
        for (var i = 0; i < full.Width * full.Height; i++)
        {
            rgb[i * 3] = full.Data[(i * 3) + 2];
            rgb[(i * 3) + 1] = full.Data[(i * 3) + 1];
            rgb[(i * 3) + 2] = full.Data[i * 3];
        }

        var reference = Pine.Infrastructure.Ai.Extraction.LayoutLm.LayoutLmImagePreprocessor.ToPixelValues(
            Pine.Infrastructure.Ai.Extraction.LayoutLm.LayoutLmImagePreprocessor.ResizeBilinear(rgb, full.Width, full.Height, 224, 224));
        var fullMs = clock.ElapsedMilliseconds;

        reduced.Width.Should().Be(full.Width, "the OCR boxes live in the full picture's coordinates");
        reduced.Height.Should().Be(full.Height);
        double sum = 0;
        double max = 0;
        for (var i = 0; i < reference.Length; i++)
        {
            var d = Math.Abs(reference[i] - reduced.PixelValues[i]) * 127.5; // back to grey levels
            sum += d;
            max = Math.Max(max, d);
        }

        output.WriteLine($"12 MP: full decode+resize {fullMs} ms, reduced {reducedMs} ms; mean abs diff {sum / reference.Length:F2} grey levels, max {max:F0}");
        (sum / reference.Length).Should().BeLessThan(2.0);
        reducedMs.Should().BeLessThan(fullMs);
    }
}
