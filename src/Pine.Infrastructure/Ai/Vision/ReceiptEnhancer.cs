using System;
using SkiaSharp;

namespace Pine.Infrastructure.Ai.Vision;

/// <summary>How the picture handed to OCR is prepared after the crop.</summary>
public enum OcrEnhancement
{
    /// <summary>The cropped colour picture as it is.</summary>
    None,

    /// <summary>Grey, 5 x 5 blur, Gaussian adaptive threshold (block 21, C 10): what the Windows detector produces.</summary>
    Binarize,

    /// <summary>Grey with the background divided out (shadows and uneven light removed), no hard threshold.</summary>
    Flatten,
}

/// <summary>
/// Managed, Skia-free (arrays and <see cref="SKBitmap"/> pixels only) port of the image enhancement the Windows
/// <c>ReceiptDetector</c> does after the warp: a 3 x 3 sharpen for the screen, and a grey + blur + adaptive-threshold copy for
/// OCR. Plus a gentler "flatten" mode for pictures where a hard threshold destroys detail (screens, moire).
/// </summary>
/// <remarks>
/// TUNED-CONSTANTS BOUNDARY (TD-118). Kernel, block size 21, C 10, blur 5 x 5 and the flatten radius are the same literals as
/// the OpenCV pipeline (<c>ReceiptDetector</c>: kernel [0 -1 0; -1 5 -1; 0 -1 0], GaussianBlur 5, AdaptiveThreshold GaussianC
/// Binary 21 / 10) or tuned with them on the research corpus (docs/65 section 14). Literals on purpose; see
/// <c>docs/13-AI-Architecture.md</c> section 7.1.
/// </remarks>
public static class ReceiptEnhancer
{
    private const int ThresholdBlock = 21;
    private const double ThresholdC = 10;
    private const int FlattenRadius = 24;

    /// <summary>Grey (BT.601) values of a bitmap, row-major.</summary>
    internal static byte[] ToGrey(SKBitmap bitmap)
    {
        var grey = new byte[bitmap.Width * bitmap.Height];
        var pixels = bitmap.GetPixelSpan();
        var stride = bitmap.RowBytes;
        var bpp = bitmap.BytesPerPixel;
        for (var y = 0; y < bitmap.Height; y++)
        {
            var row = y * stride;
            for (var x = 0; x < bitmap.Width; x++)
            {
                var i = row + (x * bpp);
                grey[(y * bitmap.Width) + x] = bpp >= 3
                    ? (byte)(((pixels[i + 2] * 299) + (pixels[i + 1] * 587) + (pixels[i] * 114)) / 1000)
                    : pixels[i];
            }
        }

        return grey;
    }

    internal static SKBitmap FromGrey(byte[] grey, int width, int height)
    {
        var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        var pixels = bitmap.GetPixelSpan();
        var stride = bitmap.RowBytes;
        for (var y = 0; y < height; y++)
        {
            var row = y * stride;
            for (var x = 0; x < width; x++)
            {
                var v = grey[(y * width) + x];
                var i = row + (x * 4);
                pixels[i] = v;
                pixels[i + 1] = v;
                pixels[i + 2] = v;
                pixels[i + 3] = 255;
            }
        }

        return bitmap;
    }

    /// <summary>The 3 x 3 sharpen of the Windows pipeline, per colour channel, borders replicated.</summary>
    public static SKBitmap Sharpen(SKBitmap source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var w = source.Width;
        var h = source.Height;
        var result = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque);
        var src = source.GetPixelSpan();
        var dst = result.GetPixelSpan();
        var sStride = source.RowBytes;
        var dStride = result.RowBytes;
        var bpp = source.BytesPerPixel;
        for (var y = 0; y < h; y++)
        {
            var up = Math.Max(y - 1, 0) * sStride;
            var mid = y * sStride;
            var down = Math.Min(y + 1, h - 1) * sStride;
            for (var x = 0; x < w; x++)
            {
                var l = Math.Max(x - 1, 0) * bpp;
                var c = x * bpp;
                var r = Math.Min(x + 1, w - 1) * bpp;
                var o = (y * dStride) + (x * 4);
                for (var ch = 0; ch < 3; ch++)
                {
                    var v = (5 * src[mid + c + ch]) - src[up + c + ch] - src[down + c + ch] - src[mid + l + ch] - src[mid + r + ch];
                    dst[o + ch] = (byte)Math.Clamp(v, 0, 255);
                }

                dst[o + 3] = 255;
            }
        }

        return result;
    }

    /// <summary>Applies <paramref name="mode"/> to a cropped picture; <see cref="OcrEnhancement.None"/> returns a copy.</summary>
    public static SKBitmap ForOcr(SKBitmap cropped, OcrEnhancement mode)
    {
        ArgumentNullException.ThrowIfNull(cropped);
        if (mode == OcrEnhancement.None)
        {
            return cropped.Copy();
        }

        var grey = ToGrey(cropped);
        var output = mode == OcrEnhancement.Binarize
            ? AdaptiveThreshold(GaussianBlur(grey, cropped.Width, cropped.Height, GaussianKernel(5, 1.1f)), cropped.Width, cropped.Height)
            : Flatten(grey, cropped.Width, cropped.Height);
        return FromGrey(output, cropped.Width, cropped.Height);
    }

    internal static float[] GaussianKernel(int size, float sigma)
    {
        var kernel = new float[size];
        var half = size / 2;
        float sum = 0;
        for (var i = 0; i < size; i++)
        {
            var d = i - half;
            kernel[i] = MathF.Exp(-(d * d) / (2 * sigma * sigma));
            sum += kernel[i];
        }

        for (var i = 0; i < size; i++)
        {
            kernel[i] /= sum;
        }

        return kernel;
    }

    /// <summary>Separable Gaussian blur with replicated borders.</summary>
    internal static byte[] GaussianBlur(byte[] grey, int w, int h, float[] kernel)
    {
        var half = kernel.Length / 2;
        var tmp = new float[grey.Length];
        for (var y = 0; y < h; y++)
        {
            var row = y * w;
            for (var x = 0; x < w; x++)
            {
                float acc = 0;
                for (var k = 0; k < kernel.Length; k++)
                {
                    acc += kernel[k] * grey[row + Math.Clamp(x + k - half, 0, w - 1)];
                }

                tmp[row + x] = acc;
            }
        }

        var result = new byte[grey.Length];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                float acc = 0;
                for (var k = 0; k < kernel.Length; k++)
                {
                    acc += kernel[k] * tmp[(Math.Clamp(y + k - half, 0, h - 1) * w) + x];
                }

                result[(y * w) + x] = (byte)Math.Clamp((int)MathF.Round(acc), 0, 255);
            }
        }

        return result;
    }

    /// <summary>OpenCV <c>AdaptiveThreshold(GaussianC, Binary, 21, 10)</c>: white where the pixel exceeds its Gaussian-weighted neighbourhood mean minus C.</summary>
    internal static byte[] AdaptiveThreshold(byte[] grey, int w, int h)
    {
        // OpenCV derives the window sigma from the block size: 0.3 * ((block - 1) * 0.5 - 1) + 0.8.
        var sigma = (float)((0.3 * (((ThresholdBlock - 1) * 0.5) - 1)) + 0.8);
        var mean = GaussianBlur(grey, w, h, GaussianKernel(ThresholdBlock, sigma));
        var result = new byte[grey.Length];
        for (var i = 0; i < grey.Length; i++)
        {
            result[i] = grey[i] > mean[i] - ThresholdC ? (byte)255 : (byte)0;
        }

        return result;
    }

    /// <summary>Divides out a box-blurred background so shadows and gradients vanish, keeping every grey level of the print.</summary>
    internal static byte[] Flatten(byte[] grey, int w, int h)
    {
        var background = BoxBlur(grey, w, h, FlattenRadius);
        var result = new byte[grey.Length];
        for (var i = 0; i < grey.Length; i++)
        {
            result[i] = (byte)Math.Clamp(grey[i] * 235 / Math.Max(background[i], (byte)1), 0, 255);
        }

        return result;
    }

    internal static byte[] BoxBlur(byte[] grey, int w, int h, int radius)
    {
        var integral = new long[(w + 1) * (h + 1)];
        for (var y = 0; y < h; y++)
        {
            long row = 0;
            for (var x = 0; x < w; x++)
            {
                row += grey[(y * w) + x];
                integral[((y + 1) * (w + 1)) + x + 1] = integral[(y * (w + 1)) + x + 1] + row;
            }
        }

        var result = new byte[grey.Length];
        for (var y = 0; y < h; y++)
        {
            var y0 = Math.Max(y - radius, 0);
            var y1 = Math.Min(y + radius + 1, h);
            for (var x = 0; x < w; x++)
            {
                var x0 = Math.Max(x - radius, 0);
                var x1 = Math.Min(x + radius + 1, w);
                var sum = integral[(y1 * (w + 1)) + x1] - integral[(y0 * (w + 1)) + x1] - integral[(y1 * (w + 1)) + x0] + integral[(y0 * (w + 1)) + x0];
                result[(y * w) + x] = (byte)(sum / ((long)(x1 - x0) * (y1 - y0)));
            }
        }

        return result;
    }

    /// <summary>
    /// Variance of the Laplacian of the grey picture (a cheap sharpness score: high is sharp). Pass a region of interest
    /// already cropped; the score is only comparable between pictures of the same size, so callers use a fixed working size.
    /// </summary>
    public static double LaplacianVariance(SKBitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        var w = bitmap.Width;
        var h = bitmap.Height;
        if (w < 3 || h < 3)
        {
            return 0;
        }

        var g = ToGrey(bitmap);
        double sum = 0;
        double sumSquares = 0;
        var n = 0;
        for (var y = 1; y < h - 1; y++)
        {
            for (var x = 1; x < w - 1; x++)
            {
                var i = (y * w) + x;
                double lap = (4 * g[i]) - g[i - 1] - g[i + 1] - g[i - w] - g[i + w];
                sum += lap;
                sumSquares += lap * lap;
                n++;
            }
        }

        var mean = sum / n;
        return (sumSquares / n) - (mean * mean);
    }
}
