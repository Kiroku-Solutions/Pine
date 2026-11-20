using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Pine.Core.Ai;
using SkiaSharp;

namespace Pine.Infrastructure.Ai.Vision;

/// <summary>
/// The detect/crop stage for a head with no OpenCV (Android, docs/65 phases C and B2): applies the EXIF orientation,
/// shrinks a camera-sized photo, then looks for the receipt's four corners and perspective-corrects the sheet. When
/// no receipt-like sheet is found it falls back to the previous behaviour: EXIF + downscale only.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IReceiptDetector"/> is the seam: <c>DefaultLocalAiProvider</c> only knows the interface; OpenCV's
/// <c>ReceiptDetector</c> is one implementation and this is the other.
/// </para>
/// <para>
/// HOW IT FINDS THE RECEIPT. <see cref="ReceiptQuadFinder"/> (Otsu threshold, largest region, convex hull, maximum-area
/// inscribed quadrilateral), then a homography warp drawn by Skia. It is NOT the YOLO model (see the finder's remarks),
/// so the answer is "a sheet-like quadrilateral covering 12-95 % of the frame", never "this is certainly a receipt".
/// </para>
/// <para>
/// WHAT IT RETURNS. With a quad: <c>PassedFilters = true</c> and the upright, cropped colour picture as a JPEG in both
/// <see cref="ReceiptAnalysisResult.CroppedImageStream"/> and <see cref="ReceiptAnalysisResult.OcrImageStream"/> (no
/// binarisation: OCR reads colour, docs/65 section 10). Without one, and when the image is already upright and small,
/// <c>PassedFilters = false</c>, which the provider reads as "use the original bytes"; otherwise the upright, downscaled
/// picture is returned as the OCR image. OCR, the heuristics and LayoutLM then all work in that one coordinate space.
/// </para>
/// <para>
/// TUNED-CONSTANTS BOUNDARY (TD-118). <see cref="MaxSide"/>, the detection working size and the JPEG quality are literals
/// by design, like the rest of the vision stack; see <c>docs/13-AI-Architecture.md</c> section 7.1.
/// </para>
/// </remarks>
public sealed class SkiaReceiptDetector : IReceiptDetector
{
    private readonly YoloReceiptSegmenter? _yolo;

    /// <summary>Creates the detector; <paramref name="yolo"/> is the optional ONNX segmenter tried before the classical finder.</summary>
    public SkiaReceiptDetector(YoloReceiptSegmenter? yolo = null)
    {
        _yolo = yolo;
    }

    /// <summary>Longest side an image is shrunk to before OCR.</summary>
    /// <remarks>Receipt photos are long and thin; 2048 keeps text well above the recogniser's 48 px line height.</remarks>
    public const int MaxSide = 2048;

    private const int JpegQuality = 95;

    /// <summary>Longest side of the greyscale picture the finder works on.</summary>
    private const int DetectionSide = 320;

    /// <inheritdoc />
    public async Task<bool> DetectReceiptAsync(Stream imageStream, CancellationToken cancellationToken = default)
        => await DetectReceiptConfidenceAsync(imageStream, cancellationToken).ConfigureAwait(false) > 0f;

    /// <inheritdoc />
    /// <remarks>1 when a sheet-like quadrilateral was found, otherwise 0. Not a probability.</remarks>
    public Task<float> DetectReceiptConfidenceAsync(Stream imageStream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(imageStream);
        using var memory = new MemoryStream();
        imageStream.CopyTo(memory);
        var bytes = memory.ToArray();
        return Task.Run(
            () =>
            {
                using var upright = DecodeUpright(bytes, out _);
                return FindQuadWithModel(upright) is null ? 0f : 1f;
            },
            cancellationToken);
    }

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

    internal ReceiptAnalysisResult Normalise(byte[] encoded)
    {
        using var upright = DecodeUpright(encoded, out var unchanged);

        // A failure in the optional crop must never lose the scan: any problem means "no receipt found".
        try
        {
            var quad = FindQuadWithModel(upright);
            if (quad is not null)
            {
                using var warped = Warp(upright, quad);
                if (warped is not null)
                {
                    var jpeg = Encode(warped);
                    return new ReceiptAnalysisResult(1f, true, null, new MemoryStream(jpeg), new MemoryStream(jpeg));
                }
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or InvalidDataException)
        {
        }

        if (unchanged)
        {
            return new ReceiptAnalysisResult(1f, false, null, null);
        }

        return new ReceiptAnalysisResult(1f, true, null, null, new MemoryStream(Encode(upright)));
    }

    /// <summary>Decodes, shrinks to <see cref="MaxSide"/> and applies the EXIF orientation. <paramref name="unchanged"/> is true when neither was needed.</summary>
    private static SKBitmap DecodeUpright(byte[] encoded, out bool unchanged)
    {
        using var data = SKData.CreateCopy(encoded);
        using var codec = SKCodec.Create(data) ?? throw new InvalidDataException("The image could not be decoded.");

        var width = codec.Info.Width;
        var height = codec.Info.Height;
        var origin = codec.EncodedOrigin;
        var longSide = Math.Max(width, height);
        unchanged = origin == SKEncodedOrigin.TopLeft && longSide <= MaxSide;

        // Ask the codec for a cheap power-of-two reduction first (JPEG decodes at 1/2, 1/4, 1/8 directly).
        var scale = longSide > MaxSide ? (float)MaxSide / longSide : 1f;
        var decodeSize = scale < 1f ? codec.GetScaledDimensions(scale) : codec.Info.Size;
        var info = new SKImageInfo(decodeSize.Width, decodeSize.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        using var decoded = SKBitmap.Decode(codec, info) ?? throw new InvalidDataException("The image could not be decoded.");

        using var sized = ShrinkTo(decoded, MaxSide);
        return Orient(sized, origin);
    }

    private static byte[] Encode(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var encodedData = image.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);
        return encodedData.ToArray();
    }

    /// <summary>YOLO first (the Windows semantics), then the classical finder, then nothing. Timings go to <see cref="ScanDiagnostics"/>.</summary>
    private (double X, double Y)[]? FindQuadWithModel(SKBitmap upright)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        (double X, double Y)[]? quad = null;
        var source = "none";
        long modelMs = 0;
        if (_yolo is not null)
        {
            try
            {
                quad = _yolo.FindQuad(upright, out _, out modelMs);
                source = quad is null ? "yolo-miss" : "yolo";
            }
            catch (InvalidOperationException)
            {
                source = "yolo-error";
            }
        }

        var yoloTotal = clock.ElapsedMilliseconds;
        if (quad is null)
        {
            quad = FindQuad(upright);
            if (quad is not null)
            {
                source += "+classic";
            }
        }

        ScanDiagnostics.Report($"detect source={source} yolo_infer_ms={modelMs} yolo_total_ms={yoloTotal} total_ms={clock.ElapsedMilliseconds}");
        return quad;
    }

    /// <summary>The receipt's corners in <paramref name="upright"/>'s pixel coordinates, clockwise from top-left, or null.</summary>
    private static (double X, double Y)[]? FindQuad(SKBitmap upright)
    {
        var ratio = Math.Min(1.0, (double)DetectionSide / Math.Max(upright.Width, upright.Height));
        var w = Math.Max(1, (int)Math.Round(upright.Width * ratio));
        var h = Math.Max(1, (int)Math.Round(upright.Height * ratio));
        using var small = upright.Resize(
            new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Unpremul),
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        if (small is null)
        {
            return null;
        }

        var pixels = small.GetPixelSpan();
        var grey = new byte[w * h];
        for (var y = 0; y < h; y++)
        {
            var row = y * small.RowBytes;
            for (var x = 0; x < w; x++)
            {
                var i = row + (x * 4);
                grey[(y * w) + x] = (byte)(((pixels[i + 2] * 299) + (pixels[i + 1] * 587) + (pixels[i] * 114)) / 1000);
            }
        }

        var quad = ReceiptQuadFinder.Find(grey, w, h);
        if (quad is null)
        {
            return null;
        }

        var sx = (double)upright.Width / w;
        var sy = (double)upright.Height / h;
        return quad.Select(p => (p.X * sx, p.Y * sy)).ToArray();
    }

    /// <summary>Straightens the quadrilateral into an upright rectangle with a homography Skia applies while drawing.</summary>
    internal static SKBitmap? Warp(SKBitmap source, (double X, double Y)[] quad)
    {
        static double Distance((double X, double Y) a, (double X, double Y) b)
            => Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)));

        var width = Math.Max(Distance(quad[0], quad[1]), Distance(quad[3], quad[2]));
        var height = Math.Max(Distance(quad[0], quad[3]), Distance(quad[1], quad[2]));
        var shrink = Math.Min(1.0, MaxSide / Math.Max(width, height));
        var outW = (int)Math.Round(width * shrink);
        var outH = (int)Math.Round(height * shrink);
        if (outW < 8 || outH < 8)
        {
            return null;
        }

        var destination = new (double X, double Y)[]
        {
            (0, 0), (outW, 0), (outW, outH), (0, outH),
        };
        var h = Homography(quad, destination);
        if (h is null)
        {
            return null;
        }

        var result = new SKBitmap(outW, outH, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(result);
        canvas.Clear(SKColors.White);
        canvas.SetMatrix(new SKMatrix(
            (float)h[0], (float)h[1], (float)h[2],
            (float)h[3], (float)h[4], (float)h[5],
            (float)h[6], (float)h[7], 1f));
        using var image = SKImage.FromBitmap(source);
        canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), null);
        return result;
    }

    /// <summary>The homography (row-major, 8 values, h22 = 1) taking each <paramref name="from"/> point to its <paramref name="to"/>, or null when degenerate.</summary>
    internal static double[]? Homography((double X, double Y)[] from, (double X, double Y)[] to)
    {
        var a = new double[8, 9];
        for (var i = 0; i < 4; i++)
        {
            var (x, y) = from[i];
            var (u, v) = to[i];
            var r = i * 2;
            a[r, 0] = x; a[r, 1] = y; a[r, 2] = 1; a[r, 6] = -u * x; a[r, 7] = -u * y; a[r, 8] = u;
            a[r + 1, 3] = x; a[r + 1, 4] = y; a[r + 1, 5] = 1; a[r + 1, 6] = -v * x; a[r + 1, 7] = -v * y; a[r + 1, 8] = v;
        }

        for (var col = 0; col < 8; col++)
        {
            var pivot = col;
            for (var r = col + 1; r < 8; r++)
            {
                if (Math.Abs(a[r, col]) > Math.Abs(a[pivot, col]))
                {
                    pivot = r;
                }
            }

            if (Math.Abs(a[pivot, col]) < 1e-9)
            {
                return null;
            }

            if (pivot != col)
            {
                for (var c = 0; c < 9; c++)
                {
                    (a[col, c], a[pivot, c]) = (a[pivot, c], a[col, c]);
                }
            }

            for (var r = 0; r < 8; r++)
            {
                if (r == col)
                {
                    continue;
                }

                var f = a[r, col] / a[col, col];
                for (var c = col; c < 9; c++)
                {
                    a[r, c] -= f * a[col, c];
                }
            }
        }

        var h = new double[8];
        for (var i = 0; i < 8; i++)
        {
            h[i] = a[i, 8] / a[i, i];
        }

        return h;
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
