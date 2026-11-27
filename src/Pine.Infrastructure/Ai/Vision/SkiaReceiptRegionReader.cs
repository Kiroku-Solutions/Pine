using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Pine.Core.Ai;
using SkiaSharp;

namespace Pine.Infrastructure.Ai.Vision;

/// <summary>
/// Crops a user-drawn bounding box from the OCR-aligned receipt image (with gentle padding and bounded linear
/// upscaling when small) and runs the configured <see cref="IOcrEngine"/> on just that region so faint or tight lines
/// missed by full-page detection get the detector's full resolution budget.
/// </summary>
public sealed class SkiaReceiptRegionReader : IReceiptRegionReader
{
    private const int MaxTargetWidth = 960;
    private readonly IOcrEngine? _ocrEngine;

    public SkiaReceiptRegionReader(IOcrEngine? ocrEngine = null)
    {
        _ocrEngine = ocrEngine;
    }

    public async Task<string> ReadRegionAsync(
        byte[] alignedImageBytes,
        NormalizedRect region,
        CancellationToken cancellationToken = default)
    {
        if (_ocrEngine is null
            || alignedImageBytes is null
            || alignedImageBytes.Length == 0
            || region is null
            || !IsFiniteRegion(region)
            || region.Width <= 0.005
            || region.Height <= 0.005)
        {
            return string.Empty;
        }

        byte[]? croppedPng = await Task.Run(() => CropRegion(alignedImageBytes, region), cancellationToken).ConfigureAwait(false);
        if (croppedPng is null || croppedPng.Length == 0)
        {
            return string.Empty;
        }

        using var ms = new MemoryStream(croppedPng, writable: false);
        var words = await _ocrEngine.ExtractTextAsync(ms, cancellationToken).ConfigureAwait(false);
        if (words is null || words.Count == 0)
        {
            return string.Empty;
        }

        var ordered = OrderReadingOrder(words.Where(w => !string.IsNullOrWhiteSpace(w.Text)).ToList());
        return string.Join(" ", ordered.Select(w => w.Text.Trim())).Trim();
    }

    private static bool IsFiniteRegion(NormalizedRect r) =>
        double.IsFinite(r.X) && double.IsFinite(r.Y) && double.IsFinite(r.Width) && double.IsFinite(r.Height);

    private static List<OcrWord> OrderReadingOrder(List<OcrWord> words)
    {
        if (words.Count <= 1)
        {
            return words;
        }

        var heights = words.Select(w => (double)w.BoundingBox.Height).OrderBy(h => h).ToList();
        var rowTol = Math.Max(heights[heights.Count / 2] * 0.6, 6.0);

        var sortedByY = words.OrderBy(w => w.BoundingBox.Y).ThenBy(w => w.BoundingBox.X).ToList();
        var rows = new List<List<OcrWord>>();
        var current = new List<OcrWord> { sortedByY[0] };
        var currentY = CenterY(sortedByY[0]);

        for (var i = 1; i < sortedByY.Count; i++)
        {
            if (Math.Abs(CenterY(sortedByY[i]) - currentY) <= rowTol)
            {
                current.Add(sortedByY[i]);
            }
            else
            {
                rows.Add(current.OrderBy(w => w.BoundingBox.X).ToList());
                current = [sortedByY[i]];
                currentY = CenterY(sortedByY[i]);
            }
        }

        rows.Add(current.OrderBy(w => w.BoundingBox.X).ToList());
        return rows.SelectMany(r => r).ToList();
    }

    private static double CenterY(OcrWord word) => word.BoundingBox.Y + (word.BoundingBox.Height * 0.5);

    private static byte[]? CropRegion(byte[] imageBytes, NormalizedRect region)
    {
        try
        {
            using var bitmap = SKBitmap.Decode(imageBytes);
            if (bitmap is null || bitmap.Width <= 0 || bitmap.Height <= 0)
            {
                return null;
            }

            // Add 12% context margin around the drawn box so DB detector sees clean white border around glyphs.
            var padX = Math.Max(region.Width * 0.12, 0.01);
            var padY = Math.Max(region.Height * 0.15, 0.008);
            var x0 = Math.Clamp((int)Math.Floor((region.X - padX) * bitmap.Width), 0, bitmap.Width - 1);
            var y0 = Math.Clamp((int)Math.Floor((region.Y - padY) * bitmap.Height), 0, bitmap.Height - 1);
            var x1 = Math.Clamp((int)Math.Ceiling((region.Right + padX) * bitmap.Width), x0 + 1, bitmap.Width);
            var y1 = Math.Clamp((int)Math.Ceiling((region.Bottom + padY) * bitmap.Height), y0 + 1, bitmap.Height);

            var cropW = x1 - x0;
            var cropH = y1 - y0;
            if (cropW < 4 || cropH < 4)
            {
                return null;
            }

            // Upscale tiny crops so small thermal print reaches ~160px height while capping width at MaxTargetWidth.
            var desiredScale = cropH < 120 ? Math.Min(3.0, 160.0 / cropH) : 1.0;
            var maxScaleByWidth = cropW > 0 ? Math.Max(1.0, (double)MaxTargetWidth / cropW) : 1.0;
            var scale = Math.Min(desiredScale, maxScaleByWidth);

            var targetW = Math.Max(8, (int)Math.Round(cropW * scale));
            var targetH = Math.Max(8, (int)Math.Round(cropH * scale));
            const int border = 24;

            using var output = new SKBitmap(targetW + (border * 2), targetH + (border * 2), SKColorType.Bgra8888, SKAlphaType.Opaque);
            using (var srcImage = SKImage.FromBitmap(bitmap))
            using (var canvas = new SKCanvas(output))
            {
                canvas.Clear(SKColors.White);
                canvas.DrawImage(
                    srcImage,
                    new SKRect(x0, y0, x1, y1),
                    new SKRect(border, border, border + targetW, border + targetH),
                    new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
            }

            using var image = SKImage.FromBitmap(output);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            return data?.ToArray();
        }
        catch
        {
            return null;
        }
    }
}
