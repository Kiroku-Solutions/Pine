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
/// Levels a tilted receipt: the dominant angle of the detector's text boxes decides, and when it is between 3 and 45 degrees the
/// picture is turned that much the other way and read again. Everything after OCR (row grouping, LayoutLM's picture) then works
/// in the levelled picture.
/// </summary>
/// <remarks>
/// TUNED-CONSTANTS BOUNDARY (TD-118). The 3 degree trigger, the 45 degree limit, the five-line minimum and the 80 % acceptance
/// of the second read are literals tuned on the tilted East Repair invoice (docs/65 section 14); see
/// <c>docs/13-AI-Architecture.md</c> section 7.1.
/// </remarks>
public static class ReceiptDeskew
{
    private const double TriggerDegrees = 3;
    private const double LimitDegrees = 45;
    private const int MinimumLines = 5;
    private const double AcceptShare = 0.8;

    /// <summary>The picture, the words read from it, and the angle that was removed (0 when it was level).</summary>
    public sealed record Result(byte[] Image, IReadOnlyList<OcrWord> Words, double RemovedDegrees);

    /// <summary>The median text angle in degrees (positive: the text runs clockwise down the page), or null when level, unknown or not a tilt.</summary>
    public static double? DominantAngle(IReadOnlyList<OcrWord> words)
    {
        ArgumentNullException.ThrowIfNull(words);
        var angles = words
            .Where(w => w.BoundingBox.Width >= 2 * Math.Max(1f, w.BoundingBox.Height) && Math.Abs(w.AngleDegrees) < LimitDegrees)
            .Select(w => (double)w.AngleDegrees)
            .OrderBy(a => a)
            .ToArray();
        if (angles.Length < MinimumLines)
        {
            return null;
        }

        var median = angles[angles.Length / 2];
        return Math.Abs(median) > TriggerDegrees ? median : null;
    }

    /// <summary>Reads once; if the text is tilted, turns the picture level and reads again, keeping the second read when it did not lose lines.</summary>
    public static async Task<Result> ReadLevelAsync(IOcrEngine ocr, byte[] image, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ocr);
        ArgumentNullException.ThrowIfNull(image);
        var first = await ocr.ExtractTextAsync(new MemoryStream(image, writable: false), cancellationToken).ConfigureAwait(false);
        if (DominantAngle(first) is not { } angle)
        {
            return new Result(image, first, 0);
        }

        var leveled = RotateBy(image, -angle);
        var second = await ocr.ExtractTextAsync(new MemoryStream(leveled, writable: false), cancellationToken).ConfigureAwait(false);
        ScanDiagnostics.Report($"deskew angle={angle:F1} lines_before={first.Count} lines_after={second.Count}");
        return second.Count >= first.Count * AcceptShare ? new Result(leveled, second, angle) : new Result(image, first, 0);
    }

    /// <summary>Turns a picture by <paramref name="degrees"/> clockwise about its centre on an enlarged white canvas (nothing is cut off).</summary>
    public static byte[] RotateBy(byte[] encoded, double degrees)
    {
        ArgumentNullException.ThrowIfNull(encoded);
        using var source = SKBitmap.Decode(encoded) ?? throw new InvalidDataException("The image could not be decoded.");
        var radians = Math.Abs(degrees) * Math.PI / 180;
        var width = (int)Math.Ceiling((source.Width * Math.Cos(radians)) + (source.Height * Math.Sin(radians)));
        var height = (int)Math.Ceiling((source.Width * Math.Sin(radians)) + (source.Height * Math.Cos(radians)));
        using var result = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using (var canvas = new SKCanvas(result))
        {
            canvas.Clear(SKColors.White);
            canvas.Translate(width / 2f, height / 2f);
            canvas.RotateDegrees((float)degrees);
            canvas.Translate(-source.Width / 2f, -source.Height / 2f);
            using var image = SKImage.FromBitmap(source);
            canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), null);
        }

        using var encodedImage = SKImage.FromBitmap(result);
        return encodedImage.Encode(SKEncodedImageFormat.Jpeg, 95).ToArray();
    }
}
