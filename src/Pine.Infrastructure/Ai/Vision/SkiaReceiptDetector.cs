using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Pine.Core.Ai;
using SkiaSharp;

namespace Pine.Infrastructure.Ai.Vision;

/// <summary>
/// The detect/crop stage for a head with no OpenCV (Android, docs/65 phase C): it finds NO document and crops
/// nothing. It only makes the image a sane size for the OCR engine -- applies the EXIF orientation and shrinks
/// a camera-sized photo so a 12 MP picture is not decoded into ~100 MB of working buffers.
/// </summary>
/// <remarks>
/// <para>
/// THIS IS THE SEAM B2 SLOTS INTO. <c>DefaultLocalAiProvider</c> only knows <see cref="IReceiptDetector"/>; the
/// OpenCV <c>ReceiptDetector</c> is one implementation and this is the other. A Skia port of the YOLO crop
/// and perspective warp replaces <see cref="AnalyzeAndCropReceiptAsync"/>'s body (and the Detect methods)
/// without touching the provider, the container shape or the scan view model.
/// </para>
/// <para>
/// WHAT IT RETURNS. When the image is already upright and small enough it reports
/// <c>PassedFilters = false</c>, which the provider reads as "use the original bytes". Otherwise it returns the
/// upright, downscaled picture as a JPEG in <see cref="ReceiptAnalysisResult.OcrImageStream"/>. OCR, the
/// heuristics and LayoutLM then all work in that one coordinate space.
/// </para>
/// <para>
/// DETECTION IS REFUSED, NOT FAKED. The YOLO model is portable ONNX but its pre- and post-processing (letterbox,
/// mask, contours) is OpenCV today, so "is this a receipt?" has no honest answer here yet; claiming one would
/// let the live scanner accept anything. The scan-from-file path never asks.
/// </para>
/// </remarks>
public sealed class SkiaReceiptDetector : IReceiptDetector
{
    /// <summary>Longest side an image is shrunk to before OCR.</summary>
    /// <remarks>Receipt photos are long and thin; 2048 keeps text well above the recogniser's 48 px line height.</remarks>
    public const int MaxSide = 2048;

    private const int JpegQuality = 95;

    /// <inheritdoc />
    public Task<bool> DetectReceiptAsync(Stream imageStream, CancellationToken cancellationToken = default)
        => throw new AiCapabilityUnavailableException("receipt detection");

    /// <inheritdoc />
    public Task<float> DetectReceiptConfidenceAsync(Stream imageStream, CancellationToken cancellationToken = default)
        => throw new AiCapabilityUnavailableException("receipt detection");

    /// <inheritdoc />
    public Task<ReceiptAnalysisResult> AnalyzeAndCropReceiptAsync(Stream imageStream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(imageStream);

        using var memory = new MemoryStream();
        imageStream.CopyTo(memory);
        var bytes = memory.ToArray();
        return Task.Run(() => Normalise(bytes), cancellationToken);
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }

    internal static ReceiptAnalysisResult Normalise(byte[] encoded)
    {
        using var data = SKData.CreateCopy(encoded);
        using var codec = SKCodec.Create(data) ?? throw new InvalidDataException("The image could not be decoded.");

        var width = codec.Info.Width;
        var height = codec.Info.Height;
        var origin = codec.EncodedOrigin;
        var longSide = Math.Max(width, height);

        if (origin == SKEncodedOrigin.TopLeft && longSide <= MaxSide)
        {
            return new ReceiptAnalysisResult(1f, false, null, null);
        }

        // Ask the codec for a cheap power-of-two reduction first (JPEG decodes at 1/2, 1/4, 1/8 directly).
        var scale = longSide > MaxSide ? (float)MaxSide / longSide : 1f;
        var decodeSize = scale < 1f ? codec.GetScaledDimensions(scale) : codec.Info.Size;
        var info = new SKImageInfo(decodeSize.Width, decodeSize.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        using var decoded = SKBitmap.Decode(codec, info) ?? throw new InvalidDataException("The image could not be decoded.");

        using var sized = ShrinkTo(decoded, MaxSide);
        using var upright = Orient(sized, origin);
        using var image = SKImage.FromBitmap(upright);
        using var encodedData = image.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);

        var output = new MemoryStream(encodedData.ToArray());
        return new ReceiptAnalysisResult(1f, true, null, null, output);
    }

    private static SKBitmap ShrinkTo(SKBitmap source, int maxSide)
    {
        var longSide = Math.Max(source.Width, source.Height);
        if (longSide <= maxSide)
        {
            return source.Copy();
        }

        var ratio = (double)maxSide / longSide;
        var target = new SKImageInfo(
            Math.Max(1, (int)Math.Round(source.Width * ratio)),
            Math.Max(1, (int)Math.Round(source.Height * ratio)),
            SKColorType.Bgra8888,
            SKAlphaType.Unpremul);
        return source.Resize(target, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear))
            ?? throw new InvalidDataException("The image could not be resized.");
    }

    /// <summary>Draws the bitmap upright; a flattened white background replaces any alpha (the output is a JPEG).</summary>
    private static SKBitmap Orient(SKBitmap source, SKEncodedOrigin origin)
    {
        var w = source.Width;
        var h = source.Height;
        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var result = new SKBitmap(swap ? h : w, swap ? w : h, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(result);
        canvas.Clear(SKColors.White);

        // Each row maps source (x, y) to the upright image: x' = sx*x + kx*y + tx, y' = ky*x + sy*y + ty.
        var matrix = origin switch
        {
            SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),
            SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightTop => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1),
            SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1),
            _ => SKMatrix.Identity,
        };
        canvas.SetMatrix(matrix);
        canvas.DrawBitmap(source, 0, 0);
        return result;
    }
}
