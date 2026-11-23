using System;
using System.IO;
using SkiaSharp;

namespace Pine.Infrastructure.Ai.Vision;

/// <summary>An image as interleaved 8-bit BGR, the channel order PaddleOCR feeds both of its models.</summary>
internal sealed class BgrImage
{
    internal BgrImage(int width, int height, byte[] data)
    {
        Width = width;
        Height = height;
        Data = data;
    }

    internal int Width { get; }

    internal int Height { get; }

    /// <summary>Row-major, 3 bytes (B, G, R) per pixel.</summary>
    internal byte[] Data { get; }
}

/// <summary>A float32 NCHW tensor body plus the size the image was resized to.</summary>
internal sealed record DetInput(float[] Tensor, int Width, int Height);

/// <summary>A cropped, deskewed text line (float BGR, row-major) ready for <see cref="SkiaImagePreprocessor.RecTensor"/>.</summary>
internal sealed record LineCrop(float[] Pixels, int Width, int Height);

/// <summary>A normalised recognition input, CHW, padded to <see cref="Width"/>.</summary>
internal sealed record RecInput(float[] Tensor, int Width);

/// <summary>
/// Everything between the encoded image and the two networks' input tensors: decode (SkiaSharp, with EXIF
/// orientation), the detector's resize and normalisation, the perspective crop of a text line, and the
/// recogniser's resize and normalisation. Ports of the matching functions in
/// <c>research/scripts/onnx_ocr_reference.py</c> (docs/65 sections 4 and 6).
/// </summary>
/// <remarks>
/// <para>
/// SKIA DECODES; MANAGED CODE RESAMPLES. SkiaSharp is the one image codec that ships on every head, so it
/// does the decoding. The resize and the warp are deliberately NOT <c>SKBitmap.Resize</c>/<c>SKCanvas</c>:
/// PaddleOCR's models were trained and are served with <c>cv2.INTER_LINEAR</c> (half-pixel bilinear, no
/// anti-aliasing) and Skia's sampling differs in the last pixel, which moves the detector's box edges. The
/// loops below reproduce OpenCV's sampling exactly and have no native dependency of their own.
/// </para>
/// <para>
/// No ONNX runtime is named here (ArchitectureTests): this class produces arrays and the engine runs them.
/// </para>
/// <para>
/// TUNED-CONSTANTS BOUNDARY (TD-118): the detector's long-side limit 960 and multiple of 32, the mean and
/// standard deviation, and the recogniser's height 48, minimum width 320 and maximum width 3200 describe the
/// exported PP-OCRv5 networks and their training, not preferences. They are literals.
/// </para>
/// </remarks>
internal static class SkiaImagePreprocessor
{
    internal const int DetLimitSide = 960;
    internal const int RecHeight = 48;
    private const int RecMinWidth = 320;
    private const int RecMaxWidth = 3200;
    private static readonly double[] Mean = [0.485, 0.456, 0.406];
    private static readonly double[] Std = [0.229, 0.224, 0.225];

    /// <summary>Decodes an encoded image (PNG, JPEG, WebP, ...), applies its EXIF orientation and drops alpha.</summary>
    /// <param name="encoded">The encoded bytes.</param>
    /// <returns>The upright image as BGR.</returns>
    /// <exception cref="InvalidDataException">The bytes are not an image Skia can decode.</exception>
    internal static BgrImage Decode(byte[] encoded)
    {
        ArgumentNullException.ThrowIfNull(encoded);

        using var data = SKData.CreateCopy(encoded);
        using var codec = SKCodec.Create(data) ?? throw new InvalidDataException("The image could not be decoded.");
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        using var bitmap = SKBitmap.Decode(codec, info) ?? throw new InvalidDataException("The image could not be decoded.");

        return Reorient(bitmap.GetPixelSpan(), bitmap.Width, bitmap.Height, bitmap.RowBytes, codec.EncodedOrigin);
    }

    /// <summary>
    /// <see cref="Decode"/> for a consumer that needs few pixels: asks the codec for the largest power-of-two reduction (1/2, 1/4, 1/8)
    /// that keeps the shorter side at or above <paramref name="minShortSide"/>. Returns the reduced upright image and the full upright
    /// size, which is the coordinate space of anything computed on the full picture.
    /// </summary>
    internal static (BgrImage Image, int FullWidth, int FullHeight) DecodeReduced(byte[] encoded, int minShortSide)
    {
        ArgumentNullException.ThrowIfNull(encoded);
        using var data = SKData.CreateCopy(encoded);
        using var codec = SKCodec.Create(data) ?? throw new InvalidDataException("The image could not be decoded.");
        var swap = codec.EncodedOrigin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var fullWidth = swap ? codec.Info.Height : codec.Info.Width;
        var fullHeight = swap ? codec.Info.Width : codec.Info.Height;
        var scale = 1f;
        var shortSide = Math.Min(codec.Info.Width, codec.Info.Height);
        foreach (var candidate in new[] { 0.125f, 0.25f, 0.5f })
        {
            if (shortSide * candidate >= minShortSide)
            {
                scale = candidate;
                break;
            }
        }

        var size = scale < 1f ? codec.GetScaledDimensions(scale) : codec.Info.Size;
        var info = new SKImageInfo(size.Width, size.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        using var bitmap = SKBitmap.Decode(codec, info) ?? throw new InvalidDataException("The image could not be decoded.");
        return (Reorient(bitmap.GetPixelSpan(), bitmap.Width, bitmap.Height, bitmap.RowBytes, codec.EncodedOrigin), fullWidth, fullHeight);
    }

    /// <summary>
    /// Copies BGRA pixels into a BGR image and applies the EXIF orientation. OpenCV's <c>imdecode</c>, which the
    /// Paddle engine uses, does the same, so both backends see the same upright pixels.
    /// </summary>
    internal static BgrImage Reorient(ReadOnlySpan<byte> bgra, int width, int height, int rowBytes, SKEncodedOrigin origin)
    {
        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var outW = swap ? height : width;
        var outH = swap ? width : height;
        var output = new byte[outW * outH * 3];

        if (origin is not (SKEncodedOrigin.TopRight or SKEncodedOrigin.BottomRight or SKEncodedOrigin.BottomLeft
            or SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom))
        {
            // Upright (the usual case): a straight row copy dropping the alpha byte, no per-pixel orientation switch.
            for (var y = 0; y < height; y++)
            {
                var s = y * rowBytes;
                var d = y * width * 3;
                for (var x = 0; x < width; x++)
                {
                    output[d] = bgra[s];
                    output[d + 1] = bgra[s + 1];
                    output[d + 2] = bgra[s + 2];
                    d += 3;
                    s += 4;
                }
            }

            return new BgrImage(outW, outH, output);
        }

        for (var oy = 0; oy < outH; oy++)
        {
            for (var ox = 0; ox < outW; ox++)
            {
                int sx, sy;
                switch (origin)
                {
                    case SKEncodedOrigin.TopRight:
                        (sx, sy) = (width - 1 - ox, oy);
                        break;
                    case SKEncodedOrigin.BottomRight:
                        (sx, sy) = (width - 1 - ox, height - 1 - oy);
                        break;
                    case SKEncodedOrigin.BottomLeft:
                        (sx, sy) = (ox, height - 1 - oy);
                        break;
                    case SKEncodedOrigin.LeftTop:
                        (sx, sy) = (oy, ox);
                        break;
                    case SKEncodedOrigin.RightTop: // rotate 90 degrees clockwise
                        (sx, sy) = (oy, height - 1 - ox);
                        break;
                    case SKEncodedOrigin.RightBottom:
                        (sx, sy) = (width - 1 - oy, height - 1 - ox);
                        break;
                    case SKEncodedOrigin.LeftBottom: // rotate 90 degrees counter-clockwise
                        (sx, sy) = (width - 1 - oy, ox);
                        break;
                    default:
                        (sx, sy) = (ox, oy);
                        break;
                }

                var s = (sy * rowBytes) + (sx * 4);
                var d = ((oy * outW) + ox) * 3;
                output[d] = bgra[s];
                output[d + 1] = bgra[s + 1];
                output[d + 2] = bgra[s + 2];
            }
        }

        return new BgrImage(outW, outH, output);
    }

    /// <summary>
    /// The detector's input: long side at most 960, each side rounded to a multiple of 32, bilinear resize, BGR,
    /// <c>(x / 255 - mean) / std</c> applied to the BGR channels as they are (a PaddleOCR quirk).
    /// </summary>
    /// <param name="image">The upright image.</param>
    /// <param name="limit">Longest allowed side before resizing.</param>
    /// <returns>The NCHW tensor body and its size.</returns>
    internal static DetInput DetPreprocess(BgrImage image, int limit = DetLimitSide)
    {
        ArgumentNullException.ThrowIfNull(image);

        var h = image.Height;
        var w = image.Width;
        var ratio = Math.Max(h, w) > limit ? (double)limit / Math.Max(h, w) : 1.0;
        var rh = Math.Max((int)(Math.Round((int)(h * ratio) / 32.0) * 32), 32);
        var rw = Math.Max((int)(Math.Round((int)(w * ratio) / 32.0) * 32), 32);

        var tensor = new float[3 * rh * rw];
        var plane = rh * rw;
        var (x0, x1, fx) = Axis(rw, w);
        var (y0, y1, fy) = Axis(rh, h);
        var src = image.Data;

        for (var y = 0; y < rh; y++)
        {
            var row0 = y0[y] * w;
            var row1 = y1[y] * w;
            var wy = fy[y];
            for (var x = 0; x < rw; x++)
            {
                var a = (row0 + x0[x]) * 3;
                var b = (row0 + x1[x]) * 3;
                var c = (row1 + x0[x]) * 3;
                var d = (row1 + x1[x]) * 3;
                var wx = fx[x];
                for (var ch = 0; ch < 3; ch++)
                {
                    var top = (src[a + ch] * (1 - wx)) + (src[b + ch] * wx);
                    var bot = (src[c + ch] * (1 - wx)) + (src[d + ch] * wx);
                    var v = (top * (1 - wy)) + (bot * wy);
                    tensor[(ch * plane) + (y * rw) + x] = (float)(((v / 255.0) - Mean[ch]) / Std[ch]);
                }
            }
        }

        return new DetInput(tensor, rw, rh);
    }

    /// <summary>
    /// Cuts the quadrilateral (top-left, top-right, bottom-right, bottom-left) out of the image with a
    /// homography warp (bilinear, edge clamp); a tall line is rotated 90 degrees counter-clockwise.
    /// </summary>
    /// <param name="image">The upright image (full resolution).</param>
    /// <param name="quad">The four corners in image pixels.</param>
    /// <returns>The deskewed line as float BGR.</returns>
    internal static LineCrop CropLine(BgrImage image, Point2[] quad)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(quad);

        var w = Math.Max((int)Math.Max(Dist(quad[0], quad[1]), Dist(quad[2], quad[3])), 1);
        var h = Math.Max((int)Math.Max(Dist(quad[0], quad[3]), Dist(quad[1], quad[2])), 1);

        var dst = new[] { new Point2(0, 0), new Point2(w, 0), new Point2(w, h), new Point2(0, h) };
        var m = Homography(dst, quad); // output -> source

        var sw = image.Width;
        var sh = image.Height;
        var src = image.Data;
        var warped = new float[w * h * 3];

        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                double px = x + 0.5, py = y + 0.5;
                var X = (m[0] * px) + (m[1] * py) + m[2];
                var Y = (m[3] * px) + (m[4] * py) + m[5];
                var Z = (m[6] * px) + (m[7] * py) + m[8];
                var sx = (X / Z) - 0.5;
                var sy = (Y / Z) - 0.5;
                var xa = Math.Clamp((int)Math.Floor(sx), 0, sw - 1);
                var ya = Math.Clamp((int)Math.Floor(sy), 0, sh - 1);
                var xb = Math.Clamp(xa + 1, 0, sw - 1);
                var yb = Math.Clamp(ya + 1, 0, sh - 1);
                var fx = Math.Clamp(sx - xa, 0, 1);
                var fy = Math.Clamp(sy - ya, 0, 1);
                var o = ((y * w) + x) * 3;
                for (var ch = 0; ch < 3; ch++)
                {
                    var top = (src[(((ya * sw) + xa) * 3) + ch] * (1 - fx)) + (src[(((ya * sw) + xb) * 3) + ch] * fx);
                    var bot = (src[(((yb * sw) + xa) * 3) + ch] * (1 - fx)) + (src[(((yb * sw) + xb) * 3) + ch] * fx);
                    warped[o + ch] = (float)((top * (1 - fy)) + (bot * fy));
                }
            }
        }

        if ((double)h / w < 1.5)
        {
            return new LineCrop(warped, w, h);
        }

        // np.rot90: result[i, j] = m[j, W - 1 - i], result is W rows by H columns.
        var rotated = new float[w * h * 3];
        for (var i = 0; i < w; i++)
        {
            for (var j = 0; j < h; j++)
            {
                var s = ((j * w) + (w - 1 - i)) * 3;
                var d = ((i * h) + j) * 3;
                rotated[d] = warped[s];
                rotated[d + 1] = warped[s + 1];
                rotated[d + 2] = warped[s + 2];
            }
        }

        return new LineCrop(rotated, h, w);
    }

    /// <summary>
    /// The recogniser's input: height 48, width <c>ceil(48 * w / h)</c> padded with zeros to at least 320 (at most
    /// 3200), <c>(x / 255 - 0.5) / 0.5</c>, BGR, CHW.
    /// </summary>
    /// <param name="crop">The deskewed line.</param>
    /// <returns>The tensor body and the padded width (lines of equal width can share a batch).</returns>
    internal static RecInput RecTensor(LineCrop crop)
    {
        ArgumentNullException.ThrowIfNull(crop);

        var ratio = crop.Width / (double)crop.Height;
        var tw = Math.Min(Math.Max((int)(RecHeight * Math.Max(RecMinWidth / (double)RecHeight, ratio)), 1), RecMaxWidth);
        var rw = Math.Ceiling(RecHeight * ratio) > tw ? tw : (int)Math.Ceiling(RecHeight * ratio);

        var tensor = new float[3 * RecHeight * tw];
        var plane = RecHeight * tw;
        var (x0, x1, fx) = Axis(rw, crop.Width);
        var (y0, y1, fy) = Axis(RecHeight, crop.Height);
        var src = crop.Pixels;

        for (var y = 0; y < RecHeight; y++)
        {
            for (var x = 0; x < rw; x++)
            {
                var a = ((y0[y] * crop.Width) + x0[x]) * 3;
                var b = ((y0[y] * crop.Width) + x1[x]) * 3;
                var c = ((y1[y] * crop.Width) + x0[x]) * 3;
                var d = ((y1[y] * crop.Width) + x1[x]) * 3;
                for (var ch = 0; ch < 3; ch++)
                {
                    var top = (src[a + ch] * (1 - fx[x])) + (src[b + ch] * fx[x]);
                    var bot = (src[c + ch] * (1 - fx[x])) + (src[d + ch] * fx[x]);
                    var v = (top * (1 - fy[y])) + (bot * fy[y]);
                    tensor[(ch * plane) + (y * tw) + x] = (float)(((v / 255.0) - 0.5) / 0.5);
                }
            }
        }

        return new RecInput(tensor, tw);
    }

    /// <summary>Source indices and weight of a half-pixel-centre bilinear resize along one axis (cv2.INTER_LINEAR).</summary>
    private static (int[] I0, int[] I1, double[] Frac) Axis(int newSize, int oldSize)
    {
        var i0 = new int[newSize];
        var i1 = new int[newSize];
        var frac = new double[newSize];
        var scale = oldSize / (double)newSize;
        for (var i = 0; i < newSize; i++)
        {
            var s = ((i + 0.5) * scale) - 0.5;
            var lo = Math.Clamp((int)Math.Floor(s), 0, oldSize - 1);
            i0[i] = lo;
            i1[i] = Math.Clamp(lo + 1, 0, oldSize - 1);
            frac[i] = Math.Clamp(s - lo, 0, 1);
        }

        return (i0, i1, frac);
    }

    private static double Dist(Point2 a, Point2 b) => Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)));

    /// <summary>The 3x3 homography (row-major, last element 1) mapping the four <paramref name="src"/> points onto <paramref name="dst"/>.</summary>
    internal static double[] Homography(Point2[] src, Point2[] dst)
    {
        // Eight unknowns h0..h7 (h8 = 1): for each pair, u = (h0 x + h1 y + h2) / (h6 x + h7 y + 1) and likewise v.
        var a = new double[8, 9];
        for (var i = 0; i < 4; i++)
        {
            double x = src[i].X, y = src[i].Y, u = dst[i].X, v = dst[i].Y;
            a[2 * i, 0] = x;
            a[2 * i, 1] = y;
            a[2 * i, 2] = 1;
            a[2 * i, 6] = -u * x;
            a[2 * i, 7] = -u * y;
            a[2 * i, 8] = u;
            a[(2 * i) + 1, 3] = x;
            a[(2 * i) + 1, 4] = y;
            a[(2 * i) + 1, 5] = 1;
            a[(2 * i) + 1, 6] = -v * x;
            a[(2 * i) + 1, 7] = -v * y;
            a[(2 * i) + 1, 8] = v;
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

            if (Math.Abs(a[pivot, col]) < 1e-12)
            {
                // Degenerate (collinear) quad: the identity keeps the crop finite; the line will read as empty.
                return [1, 0, 0, 0, 1, 0, 0, 0, 1];
            }

            if (pivot != col)
            {
                for (var k = 0; k < 9; k++)
                {
                    (a[col, k], a[pivot, k]) = (a[pivot, k], a[col, k]);
                }
            }

            for (var r = 0; r < 8; r++)
            {
                if (r == col)
                {
                    continue;
                }

                var f = a[r, col] / a[col, col];
                for (var k = col; k < 9; k++)
                {
                    a[r, k] -= f * a[col, k];
                }
            }
        }

        var h = new double[9];
        for (var i = 0; i < 8; i++)
        {
            h[i] = a[i, 8] / a[i, i];
        }

        h[8] = 1;
        return h;
    }
}
