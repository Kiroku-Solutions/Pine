using OpenCvSharp;

namespace Pine.Infrastructure.Ai.Extraction.LayoutLm;

/// <summary>An encoded image reduced to what the model needs: its original size and 3x224x224 pixel values.</summary>
/// <param name="Width">Width in pixels of the decoded image (the coordinate space of the OCR boxes).</param>
/// <param name="Height">Height in pixels of the decoded image.</param>
/// <param name="PixelValues">CHW float32, RGB, scaled to [-1, 1].</param>
public sealed record LayoutLmImage(int Width, int Height, float[] PixelValues);

/// <summary>
/// <c>LayoutLMv3ImageProcessor</c> with <c>apply_ocr=false</c>: bilinear resize to 224x224, rescale
/// 1/255, normalise with mean 0.5 and std 0.5.
/// </summary>
/// <remarks>
/// <para>
/// DECODING USES OPENCV, BECAUSE THE OCR ENGINE DOES. <c>PaddleOcrEngine</c> decodes with
/// <c>Cv2.ImDecode(..., Color)</c>, so the OCR boxes live in that decoder's coordinate space (EXIF
/// rotation included). Decoding the same bytes the same way keeps boxes and pixels aligned.
/// </para>
/// <para>
/// RESIZE IS PILLOW'S BILINEAR, REIMPLEMENTED. OpenCV's <c>INTER_LINEAR</c> does not widen its
/// kernel when shrinking, so it aliases on a photo of a receipt, while PIL (what the model was trained
/// with) low-pass filters; the two differ by tens of grey levels. This follows Pillow's
/// <c>ImagingResample</c>: triangle filter, support scaled by the reduction, horizontal pass then
/// vertical pass with an 8-bit rounding between them. Agreement with PIL on the test fixtures is
/// within 1 grey level.
/// </para>
/// </remarks>
public static class LayoutLmImagePreprocessor
{
    /// <summary>Side of the square image the model takes.</summary>
    public const int Size = 224;

    /// <summary>Decodes an encoded image (PNG, JPEG, ...) and preprocesses it.</summary>
    public static LayoutLmImage Process(ReadOnlySpan<byte> encoded)
    {
        using var mat = Cv2.ImDecode(encoded.ToArray(), ImreadModes.Color);
        if (mat.Empty())
        {
            throw new InvalidOperationException("The image could not be decoded.");
        }

        var width = mat.Width;
        var height = mat.Height;
        var rgb = new byte[width * height * 3];
        mat.GetArray(out Vec3b[] bgr);
        for (var i = 0; i < bgr.Length; i++)
        {
            rgb[i * 3] = bgr[i].Item2;
            rgb[i * 3 + 1] = bgr[i].Item1;
            rgb[i * 3 + 2] = bgr[i].Item0;
        }

        var resized = ResizeBilinear(rgb, width, height, Size, Size);
        return new LayoutLmImage(width, height, ToPixelValues(resized));
    }

    /// <summary>Interleaved HWC uint8 RGB to normalised CHW float32.</summary>
    public static float[] ToPixelValues(byte[] hwc)
    {
        var plane = Size * Size;
        var values = new float[3 * plane];
        for (var p = 0; p < plane; p++)
        {
            for (var c = 0; c < 3; c++)
            {
                values[c * plane + p] = ((hwc[p * 3 + c] / 255f) - 0.5f) / 0.5f;
            }
        }

        return values;
    }

    /// <summary>Pillow-compatible bilinear resize of an interleaved 3-channel 8-bit image.</summary>
    public static byte[] ResizeBilinear(byte[] source, int sourceWidth, int sourceHeight, int targetWidth, int targetHeight)
    {
        var horizontal = ResampleAxis(source, sourceWidth, sourceHeight, targetWidth, horizontal: true);
        return ResampleAxis(horizontal, targetWidth, sourceHeight, targetHeight, horizontal: false);
    }

    private static byte[] ResampleAxis(byte[] source, int width, int height, int targetSize, bool horizontal)
    {
        var inSize = horizontal ? width : height;
        if (inSize == targetSize)
        {
            return source;
        }

        var (starts, weights) = Coefficients(inSize, targetSize);
        var outWidth = horizontal ? targetSize : width;
        var outHeight = horizontal ? height : targetSize;
        var result = new byte[outWidth * outHeight * 3];

        for (var y = 0; y < outHeight; y++)
        {
            for (var x = 0; x < outWidth; x++)
            {
                var index = horizontal ? x : y;
                var start = starts[index];
                var kernel = weights[index];
                for (var c = 0; c < 3; c++)
                {
                    double sum = 0;
                    for (var k = 0; k < kernel.Length; k++)
                    {
                        var sx = horizontal ? start + k : x;
                        var sy = horizontal ? y : start + k;
                        sum += kernel[k] * source[(sy * width + sx) * 3 + c];
                    }

                    result[(y * outWidth + x) * 3 + c] = (byte)Math.Clamp((int)Math.Floor(sum + 0.5), 0, 255);
                }
            }
        }

        return result;
    }

    /// <summary>Pillow's <c>precompute_coeffs</c> for the bilinear (triangle) filter.</summary>
    private static (int[] Starts, double[][] Weights) Coefficients(int inSize, int outSize)
    {
        var scale = (double)inSize / outSize;
        var filterScale = Math.Max(scale, 1.0);
        var support = 1.0 * filterScale;
        var starts = new int[outSize];
        var weights = new double[outSize][];

        for (var xx = 0; xx < outSize; xx++)
        {
            var center = (xx + 0.5) * scale;
            var xmin = Math.Max((int)(center - support + 0.5), 0);
            var xmax = Math.Min((int)(center + support + 0.5), inSize) - xmin;
            var kernel = new double[xmax];
            double total = 0;
            for (var x = 0; x < xmax; x++)
            {
                var distance = Math.Abs((x + xmin - center + 0.5) / filterScale);
                var w = distance < 1.0 ? 1.0 - distance : 0.0;
                kernel[x] = w;
                total += w;
            }

            if (total != 0)
            {
                for (var x = 0; x < xmax; x++)
                {
                    kernel[x] /= total;
                }
            }

            starts[xx] = xmin;
            weights[xx] = kernel;
        }

        return (starts, weights);
    }
}
