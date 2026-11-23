using System.Text.Json;
using FluentAssertions;
using Pine.Infrastructure.Ai.Vision;
using SkiaSharp;
using Xunit.Abstractions;

namespace Pine.Tests.Unit.Ai.Ocr;

/// <summary>
/// docs/65 section 14: does the Windows binarisation help or hurt PP-OCRv5 on this corpus, and what survives a photo of a
/// screen? Ground-truth word recall (words of three or more characters, as in the parity test) over the 20 test images,
/// for the clean scan and for synthetic screen photos. Needs research/data, so it is skipped elsewhere.
/// </summary>
public sealed class ReceiptEnhancementExperimentTests(ITestOutputHelper output)
{
    private static SKBitmap Load(string id) => SKBitmap.Decode(OcrTestAssets.ImagePath(id));

    /// <summary>A photo of a screen: sub-pixel stripes, a moire beat, a glare, gamma, softness and noise.</summary>
    internal static SKBitmap ScreenPhoto(SKBitmap source, int seed)
    {
        var scale = 1400.0 / Math.Max(source.Width, source.Height);
        using var sized = scale < 1 || scale > 1
            ? source.Resize(new SKImageInfo((int)(source.Width * scale), (int)(source.Height * scale), SKColorType.Bgra8888, SKAlphaType.Opaque), new SKSamplingOptions(SKFilterMode.Linear))
            : source.Copy();
        var w = sized.Width;
        var h = sized.Height;
        var rng = new Random(seed);
        var result = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque);
        var src = sized.GetPixelSpan();
        var dst = result.GetPixelSpan();
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var i = (y * sized.RowBytes) + (x * 4);
                var o = (y * result.RowBytes) + (x * 4);
                var moire = 1 + (0.13 * Math.Sin(((x * 0.93) + (y * 0.41)) * 1.1));
                var glare = 1 + (0.35 * Math.Exp(-(Math.Pow(x - (w * 0.7), 2) + Math.Pow(y - (h * 0.25), 2)) / (2 * Math.Pow(w * 0.18, 2))));
                for (var ch = 0; ch < 3; ch++)
                {
                    // Sub-pixel stripes: each colour channel is dimmed on two of every three columns.
                    var stripe = (x % 3) == (2 - ch) ? 1.0 : 0.80;
                    var v = Math.Pow(src[i + ch] / 255.0, 1.4) * 255 * moire * stripe * glare;
                    v += rng.Next(-4, 5);
                    dst[o + ch] = (byte)Math.Clamp((int)v, 0, 255);
                }

                dst[o + 3] = 255;
            }
        }

        using var soft = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using (var canvas = new SKCanvas(soft))
        using (var paint = new SKPaint { ImageFilter = SKImageFilter.CreateBlur(0.8f, 0.8f) })
        {
            canvas.DrawBitmap(result, 0, 0, paint);
        }

        result.Dispose();
        return soft.Copy();
    }

    private static SKBitmap Blur(SKBitmap source, float sigma)
    {
        var result = new SKBitmap(source.Width, source.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(result);
        using var paint = new SKPaint { ImageFilter = SKImageFilter.CreateBlur(sigma, sigma) };
        canvas.DrawBitmap(source, 0, 0, paint);
        return result;
    }

    private static SKBitmap Downscale(SKBitmap source, double factor)
        => source.Resize(new SKImageInfo((int)(source.Width * factor), (int)(source.Height * factor), SKColorType.Bgra8888, SKAlphaType.Opaque), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear))!;

    private static MemoryStream Jpeg(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        return new MemoryStream(image.Encode(SKEncodedImageFormat.Jpeg, 95).ToArray());
    }

    private static async Task<(int Hit, int Total)> Recall(OnnxOcrEngine engine, string id, SKBitmap bitmap)
    {
        var words = await engine.ExtractTextAsync(Jpeg(bitmap), CancellationToken.None);
        var page = string.Concat(words.Select(w => OcrComparison.Norm(w.Text)));
        using var label = JsonDocument.Parse(File.ReadAllText(OcrTestAssets.LabelPath(id)));
        int total = 0, hit = 0;
        foreach (var w in label.RootElement.GetProperty("words").EnumerateArray())
        {
            var g = OcrComparison.Norm(w.GetProperty("text").GetString() ?? string.Empty);
            if (g.Length >= 3)
            {
                total++;
                hit += page.Contains(g, StringComparison.Ordinal) ? 1 : 0;
            }
        }

        return (hit, total);
    }

    [RequiresOcrParityDataFact]
    public async Task Recall_of_each_preparation_on_clean_scans_and_on_synthetic_screen_photos()
    {
        using var engine = new OnnxOcrEngine(OcrTestAssets.Options());
        var rows = new (string Name, Func<SKBitmap, SKBitmap> Prepare)[]
        {
            ("none", b => b.Copy()),
            ("binarize", b => ReceiptEnhancer.ForOcr(b, OcrEnhancement.Binarize)),
            ("flatten", b => ReceiptEnhancer.ForOcr(b, OcrEnhancement.Flatten)),
            ("blur1.5+binarize", b => { using var t = Blur(b, 1.5f); return ReceiptEnhancer.ForOcr(t, OcrEnhancement.Binarize); }),
            ("blur1.2+flatten", b => { using var t = Blur(b, 1.2f); return ReceiptEnhancer.ForOcr(t, OcrEnhancement.Flatten); }),
            ("downscale0.6", b => Downscale(b, 0.6)),
            ("downscale0.6+flatten", b => { using var t = Downscale(b, 0.6); return ReceiptEnhancer.ForOcr(t, OcrEnhancement.Flatten); }),
        };

        foreach (var source in new[] { "clean", "screen" })
        {
            var tallies = rows.ToDictionary(r => r.Name, _ => (Hit: 0, Total: 0));
            foreach (var id in OcrTestAssets.ParityIds)
            {
                using var original = Load(id);
                using var basis = source == "clean" ? original.Copy() : ScreenPhoto(original, id.GetHashCode());
                foreach (var (name, prepare) in rows)
                {
                    using var prepared = prepare(basis);
                    var (hit, total) = await Recall(engine, id, prepared);
                    var t = tallies[name];
                    tallies[name] = (t.Hit + hit, t.Total + total);
                }
            }

            foreach (var (name, t) in tallies)
            {
                output.WriteLine($"{source,-6} {name,-22} recall {(double)t.Hit / t.Total:P1} ({t.Hit}/{t.Total})");
            }
        }

        true.Should().BeTrue();
    }
}
