using System;

namespace Pine.Infrastructure.Ai.Vision;

/// <summary>
/// Array-in, array-out half of the portable YOLO receipt detector: letterbox geometry, anchor choice, mask decoding and the
/// acceptance filters. It mirrors the OpenCV <c>ReceiptDetector</c> step for step (docs/13 section 7.1) so both heads
/// answer the same question the same way, and it names no inference runtime, which keeps it testable without the model.
/// </summary>
/// <remarks>
/// TUNED-CONSTANTS BOUNDARY (TD-118). Input 640, 8400 anchors, the 64 px minimum box, the 0.85 confidence cutoff, the 0.5 mask
/// threshold, the 15 px border margin, the 1.15 aspect floor and the 10 % area floor are the same literals
/// <c>ReceiptDetector</c> uses, tuned with <c>yolo-receipt-nano.onnx</c>. They are not in <c>PineOptions</c>; change one only
/// with a model or fixture change that re-evaluates both detectors. See <c>docs/13-AI-Architecture.md</c> section 7.1.
/// </remarks>
internal static class YoloPostProcessor
{
    internal const int InputSize = 640;
    internal const int AnchorCount = 8400;
    internal const int MaskSize = 160;
    internal const int MaskChannels = 32;
    internal const float MinConfidence = 0.85f;
    private const float MinBoxSide = 64f;
    private const float MaskThreshold = 0.5f;
    private const double BorderMargin = 15;
    private const double MinAspect = 1.15;
    private const double MinAreaShare = 0.10;

    /// <summary>Where the resized picture sits inside the 640 x 640 square.</summary>
    internal readonly record struct Letterbox(float Ratio, int Width, int Height, int Left, int Top);

    internal static Letterbox ComputeLetterbox(int imageWidth, int imageHeight)
    {
        var ratio = Math.Min((float)InputSize / imageWidth, (float)InputSize / imageHeight);
        var width = (int)Math.Round(imageWidth * ratio);
        var height = (int)Math.Round(imageHeight * ratio);
        var top = (InputSize - height) / 2;
        var left = (InputSize - width) / 2;
        return new Letterbox(ratio, width, height, left, top);
    }

    /// <summary>
    /// The anchor with the highest class score among boxes of at least 64 x 64 (like <c>ReceiptDetector</c>: no NMS, the single
    /// best anchor is the receipt). <paramref name="output0"/> is [1, channels, 8400] flattened row-major.
    /// </summary>
    internal static bool TryPickAnchor(float[] output0, int channels, out int anchor, out float confidence)
    {
        anchor = -1;
        confidence = 0f;
        var classes = Math.Max(1, channels - 4 - MaskChannels);
        for (var i = 0; i < AnchorCount; i++)
        {
            if (output0[(2 * AnchorCount) + i] < MinBoxSide || output0[(3 * AnchorCount) + i] < MinBoxSide)
            {
                continue;
            }

            for (var c = 0; c < classes; c++)
            {
                var score = output0[((4 + c) * AnchorCount) + i];
                if (score > confidence)
                {
                    confidence = score;
                    anchor = i;
                }
            }
        }

        return anchor >= 0;
    }

    /// <summary>
    /// The receipt's corners (clockwise from top-left, in image pixels) from the chosen anchor's mask, or null when the mask has
    /// no usable sheet. <paramref name="protos"/> is [1, 32, 160, 160] flattened.
    /// </summary>
    internal static (double X, double Y)[]? QuadFromAnchor(
        float[] output0, int channels, int anchor, float[] protos, Letterbox box)
    {
        var classes = Math.Max(1, channels - 4 - MaskChannels);
        var coefficients = new float[MaskChannels];
        for (var c = 0; c < MaskChannels; c++)
        {
            coefficients[c] = output0[((4 + classes + c) * AnchorCount) + anchor];
        }

        // Probability map at 160 x 160: sigmoid of the coefficient-weighted sum of the prototype masks.
        var cells = MaskSize * MaskSize;
        var probability = new float[cells];
        for (var c = 0; c < MaskChannels; c++)
        {
            var weight = coefficients[c];
            var offset = c * cells;
            for (var i = 0; i < cells; i++)
            {
                probability[i] += weight * protos[offset + i];
            }
        }

        for (var i = 0; i < cells; i++)
        {
            probability[i] = 1f / (1f + MathF.Exp(-probability[i]));
        }

        // Bilinear up to the picture's own size inside the letterbox (what resize-to-640 then crop does), thresholded.
        var mask = new bool[box.Width * box.Height];
        var scale = (float)MaskSize / InputSize;
        for (var y = 0; y < box.Height; y++)
        {
            var sy = Math.Clamp(((y + box.Top + 0.5f) * scale) - 0.5f, 0f, MaskSize - 1);
            var y0 = (int)sy;
            var y1 = Math.Min(y0 + 1, MaskSize - 1);
            var fy = sy - y0;
            for (var x = 0; x < box.Width; x++)
            {
                var sx = Math.Clamp(((x + box.Left + 0.5f) * scale) - 0.5f, 0f, MaskSize - 1);
                var x0 = (int)sx;
                var x1 = Math.Min(x0 + 1, MaskSize - 1);
                var fx = sx - x0;
                var top = (probability[(y0 * MaskSize) + x0] * (1 - fx)) + (probability[(y0 * MaskSize) + x1] * fx);
                var bottom = (probability[(y1 * MaskSize) + x0] * (1 - fx)) + (probability[(y1 * MaskSize) + x1] * fx);
                mask[(y * box.Width) + x] = (top * (1 - fy)) + (bottom * fy) > MaskThreshold;
            }
        }

        var quad = ReceiptQuadFinder.QuadFromMask(mask, box.Width, box.Height);
        if (quad is null)
        {
            return null;
        }

        var inverse = 1.0 / box.Ratio;
        var result = new (double X, double Y)[4];
        for (var i = 0; i < 4; i++)
        {
            result[i] = (quad[i].X * inverse, quad[i].Y * inverse);
        }

        return result;
    }

    /// <summary>The same three refusals as <c>ReceiptDetector</c>: touching the frame edge, not elongated, too small.</summary>
    internal static bool Accept((double X, double Y)[] quad, int imageWidth, int imageHeight)
        => Refusal(quad, imageWidth, imageHeight) is null;

    /// <summary>Why <see cref="Accept"/> says no: <c>border</c>, <c>aspect</c> or <c>area</c>; null when accepted.</summary>
    internal static string? Refusal((double X, double Y)[] quad, int imageWidth, int imageHeight, bool strict = true)
    {
        // The strict set is the Windows live scanner's guidance ("move back", "not rectangular"). A still photo that fills the frame
        // or a squarish sheet is still worth cropping, so the relaxed set only refuses what is too small to be the receipt.
        foreach (var (x, y) in quad)
        {
            if (strict && (x < BorderMargin || x > imageWidth - BorderMargin || y < BorderMargin || y > imageHeight - BorderMargin))
            {
                return "border";
            }
        }

        static double Distance((double X, double Y) a, (double X, double Y) b)
            => Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)));

        var width = Math.Max(Distance(quad[0], quad[1]), Distance(quad[3], quad[2]));
        var height = Math.Max(Distance(quad[0], quad[3]), Distance(quad[1], quad[2]));
        var aspect = Math.Max(width, height) / Math.Max(1, Math.Min(width, height));
        if (strict && aspect < MinAspect)
        {
            return "aspect";
        }

        return width * height >= imageWidth * (double)imageHeight * MinAreaShare ? null : "area";
    }
}
