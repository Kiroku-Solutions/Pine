using System;
using System.Collections.Generic;

namespace Pine.Infrastructure.Ai.Vision;

/// <summary>
/// Finds the quadrilateral of a receipt (a roughly uniform, usually lighter sheet on a different background) in a
/// small greyscale picture. Pure array code: no Skia, no OpenCV, no ONNX runtime, so it runs on every head and is
/// tested with synthetic pictures.
/// </summary>
/// <remarks>
/// <para>
/// TUNED-CONSTANTS BOUNDARY (TD-118). The blur radius, area limits, fill ratio and polygon tolerance below are the
/// algorithm, tuned together with <see cref="SkiaReceiptDetector"/>'s working size. They are literals on purpose and
/// not in <c>PineOptions</c>; see <c>docs/13-AI-Architecture.md</c> section 7.1 and <c>docs/65-Onnx-Ocr-Design.md</c>.
/// </para>
/// <para>
/// WHY NOT THE YOLO MODEL. The shipped <c>yolo-receipt-nano.onnx</c> is a segmentation model whose mask head, contour
/// and polygon fit are OpenCV code today; porting them is possible but cannot be validated without receipt photos on a
/// device, and the model is ~5 MB of APK nobody would read until then. This finder is the deterministic, testable
/// substitute: Otsu threshold, largest connected region that does not swallow the frame, convex hull, then the
/// maximum-area inscribed quadrilateral (handles any rotation and perspective, unlike the min/max of x+y). It returns
/// <see langword="null"/> whenever it is not confident, and the caller falls back to the uncropped picture.
/// </para>
/// </remarks>
internal static class ReceiptQuadFinder
{
    /// <summary>Why the last <see cref="QuadFromMask"/> refused (diagnostics only).</summary>
    [ThreadStatic]
    internal static string? LastWhy;

    private const int BlurRadius = 2;
    private const double MinAreaFraction = 0.12;
    private const double MaxAreaFraction = 0.95;
    private const double MinFillRatio = 0.80;
    private const double MinQuadSide = 0.12;

    /// <summary>A model mask may fill the whole picture (a close photo) and is looser than a threshold blob, so the checks written for a blob are relaxed.</summary>
    private const double ModelMinFillRatio = 0.65;

    /// <summary>A receipt photographed from a distance can be a few percent of the frame (a mask under 12 % was refused as no mask, so the crop was lost; seen on the tablet).</summary>
    private const double ModelMinAreaFraction = 0.02;

    /// <summary>The four corners in image coordinates, clockwise, or null when no receipt-like sheet was found.</summary>
    /// <param name="grey">Row-major 8-bit luma, <paramref name="width"/> x <paramref name="height"/>.</param>
    public static (double X, double Y)[]? Find(byte[] grey, int width, int height)
    {
        if (width < 16 || height < 16 || grey.Length < width * height)
        {
            return null;
        }

        var blurred = BoxBlur(grey, width, height, BlurRadius);
        var threshold = Otsu(blurred);

        // The receipt is usually the lighter region, but a white receipt can also sit on a lighter table; try both polarities
        // and keep the better candidate.
        (double X, double Y)[]? best = null;
        var bestScore = 0.0;
        foreach (var bright in new[] { true, false })
        {
            var mask = new bool[width * height];
            for (var i = 0; i < mask.Length; i++)
            {
                mask[i] = bright ? blurred[i] > threshold : blurred[i] <= threshold;
            }

            if (TryQuad(mask, width, height, out var quad, out var score) && score > bestScore)
            {
                best = quad;
                bestScore = score;
            }
        }

        return best;
    }

    /// <summary>The corners of the largest region of a ready-made mask (the YOLO segmentation mask), or null.</summary>
    internal static (double X, double Y)[]? QuadFromMask(bool[] mask, int width, int height)
        => TryQuad(mask, width, height, out var quad, out _, model: true) ? quad : null;

    private static bool TryQuad(bool[] mask, int width, int height, out (double X, double Y)[] quad, out double score, bool model = false)
    {
        quad = [];
        score = 0;
        var labels = new int[mask.Length];
        var stack = new Stack<int>();
        var next = 0;
        List<int>? bestRegion = null;
        var bestArea = 0;

        for (var start = 0; start < mask.Length; start++)
        {
            if (!mask[start] || labels[start] != 0)
            {
                continue;
            }

            next++;
            var region = new List<int>();
            labels[start] = next;
            stack.Push(start);
            while (stack.Count > 0)
            {
                var p = stack.Pop();
                region.Add(p);
                var x = p % width;
                var y = p / width;
                if (x > 0 && mask[p - 1] && labels[p - 1] == 0) { labels[p - 1] = next; stack.Push(p - 1); }
                if (x < width - 1 && mask[p + 1] && labels[p + 1] == 0) { labels[p + 1] = next; stack.Push(p + 1); }
                if (y > 0 && mask[p - width] && labels[p - width] == 0) { labels[p - width] = next; stack.Push(p - width); }
                if (y < height - 1 && mask[p + width] && labels[p + width] == 0) { labels[p + width] = next; stack.Push(p + width); }
            }

            if (region.Count > bestArea)
            {
                bestArea = region.Count;
                bestRegion = region;
            }
        }

        var total = (double)width * height;
        if (bestRegion is null || bestArea < total * (model ? ModelMinAreaFraction : MinAreaFraction) || bestArea > total * (model ? 1.0 : MaxAreaFraction))
        {
            LastWhy = bestRegion is null ? "noregion" : $"area={bestArea / total:F3}";
            return false;
        }

        // One point per row end is enough for the hull and keeps it cheap: leftmost and rightmost pixel of every row.
        var left = new int[height];
        var right = new int[height];
        Array.Fill(left, int.MaxValue);
        Array.Fill(right, -1);
        foreach (var p in bestRegion)
        {
            var x = p % width;
            var y = p / width;
            if (x < left[y]) left[y] = x;
            if (x > right[y]) right[y] = x;
        }

        var points = new List<(double X, double Y)>();
        for (var y = 0; y < height; y++)
        {
            if (right[y] >= 0)
            {
                points.Add((left[y], y));
                points.Add((right[y] + 1, y));
            }
        }

        var hull = ConvexHull(points);
        if (hull.Count < 4)
        {
            LastWhy = "hull";
            return false;
        }

        var candidate = MaxAreaQuad(hull);
        var quadArea = PolygonArea(candidate);
        if (quadArea <= 0 || bestArea / quadArea < (model ? ModelMinFillRatio : MinFillRatio) || quadArea > total * (model ? 1.0 : MaxAreaFraction))
        {
            LastWhy = $"fill={bestArea / Math.Max(quadArea, 1):F2} quad={quadArea / total:F2}";
            return false;
        }

        var minSide = MinQuadSide * Math.Min(width, height);
        for (var i = 0; i < 4; i++)
        {
            var a = candidate[i];
            var b = candidate[(i + 1) % 4];
            if (Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y))) < minSide)
            {
                LastWhy = "side";
                return false;
            }
        }

        quad = candidate;
        score = Math.Min(1.0, bestArea / quadArea) * Math.Sqrt(quadArea / total);
        return true;
    }

    private static List<(double X, double Y)> ConvexHull(List<(double X, double Y)> points)
    {
        points.Sort((a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y));
        var hull = new List<(double X, double Y)>();
        for (var pass = 0; pass < 2; pass++)
        {
            var start = hull.Count;
            for (var i = pass == 0 ? 0 : points.Count - 1; pass == 0 ? i < points.Count : i >= 0; i += pass == 0 ? 1 : -1)
            {
                var p = points[i];
                while (hull.Count >= start + 2 && Cross(hull[^2], hull[^1], p) <= 0)
                {
                    hull.RemoveAt(hull.Count - 1);
                }

                hull.Add(p);
            }

            hull.RemoveAt(hull.Count - 1);
        }

        return hull;
    }

    /// <summary>The four hull vertices enclosing the greatest area. Hull is counter-clockwise in a y-up sense; area is taken absolute.</summary>
    private static (double X, double Y)[] MaxAreaQuad(List<(double X, double Y)> hull)
    {
        // Thin the hull first: row-end points make it long, and the cubic search below is quadratic in what survives.
        var h = Simplify(hull, 1.5);
        var n = h.Count;
        if (n <= 4)
        {
            return Clockwise(h);
        }

        var bestArea = -1.0;
        var bi = 0;
        var bj = 0;
        var bk = 0;
        var bl = 0;
        for (var i = 0; i < n; i++)
        {
            for (var k = i + 2; k < n; k++)
            {
                var bestJ = -1;
                var bestJArea = -1.0;
                for (var j = i + 1; j < k; j++)
                {
                    var a = TriangleArea(h[i], h[j], h[k]);
                    if (a > bestJArea) { bestJArea = a; bestJ = j; }
                }

                var bestL = -1;
                var bestLArea = -1.0;
                for (var l = k + 1; l < n + i; l++)
                {
                    var a = TriangleArea(h[i], h[l % n], h[k]);
                    if (a > bestLArea) { bestLArea = a; bestL = l % n; }
                }

                if (bestJ >= 0 && bestL >= 0 && bestJArea + bestLArea > bestArea)
                {
                    bestArea = bestJArea + bestLArea;
                    bi = i; bj = bestJ; bk = k; bl = bestL;
                }
            }
        }

        return Clockwise([h[bi], h[bj], h[bk], h[bl]]);
    }

    /// <summary>Ramer-Douglas-Peucker over a closed convex polygon (split at the two most distant vertices).</summary>
    private static List<(double X, double Y)> Simplify(List<(double X, double Y)> hull, double epsilon)
    {
        if (hull.Count <= 8)
        {
            return hull;
        }

        var keep = new bool[hull.Count];
        var split = hull.Count / 2;
        keep[0] = true;
        keep[split] = true;
        Reduce(hull, 0, split, epsilon, keep);
        Reduce(hull, split, hull.Count, epsilon, keep);
        var result = new List<(double X, double Y)>();
        for (var i = 0; i < hull.Count; i++)
        {
            if (keep[i])
            {
                result.Add(hull[i]);
            }
        }

        return result;
    }

    private static void Reduce(List<(double X, double Y)> pts, int from, int to, double epsilon, bool[] keep)
    {
        // Segment runs from pts[from] to pts[to % Count]; the farthest interior vertex is kept if beyond epsilon.
        var a = pts[from];
        var b = pts[to % pts.Count];
        var farthest = -1;
        var farthestDistance = epsilon;
        for (var i = from + 1; i < to; i++)
        {
            var d = DistanceToSegment(pts[i], a, b);
            if (d > farthestDistance)
            {
                farthestDistance = d;
                farthest = i;
            }
        }

        if (farthest >= 0)
        {
            keep[farthest] = true;
            Reduce(pts, from, farthest, epsilon, keep);
            Reduce(pts, farthest, to, epsilon, keep);
        }
    }

    private static double DistanceToSegment((double X, double Y) p, (double X, double Y) a, (double X, double Y) b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var len2 = (dx * dx) + (dy * dy);
        var t = len2 == 0 ? 0 : Math.Clamp((((p.X - a.X) * dx) + ((p.Y - a.Y) * dy)) / len2, 0, 1);
        var px = a.X + (t * dx) - p.X;
        var py = a.Y + (t * dy) - p.Y;
        return Math.Sqrt((px * px) + (py * py));
    }

    private static double TriangleArea((double X, double Y) a, (double X, double Y) b, (double X, double Y) c)
        => Math.Abs(Cross(a, b, c)) / 2;

    private static double Cross((double X, double Y) o, (double X, double Y) a, (double X, double Y) b)
        => ((a.X - o.X) * (b.Y - o.Y)) - ((a.Y - o.Y) * (b.X - o.X));

    private static double PolygonArea((double X, double Y)[] p)
    {
        var s = 0.0;
        for (var i = 0; i < p.Length; i++)
        {
            var a = p[i];
            var b = p[(i + 1) % p.Length];
            s += (a.X * b.Y) - (b.X * a.Y);
        }

        return Math.Abs(s) / 2;
    }

    /// <summary>Orders four points clockwise on screen (y down), starting from the one nearest the top-left.</summary>
    private static (double X, double Y)[] Clockwise(List<(double X, double Y)> points)
    {
        var cx = 0.0;
        var cy = 0.0;
        foreach (var p in points)
        {
            cx += p.X;
            cy += p.Y;
        }

        cx /= points.Count;
        cy /= points.Count;
        var sorted = new List<(double X, double Y)>(points);

        // With y pointing down, increasing atan2(dy, dx) runs clockwise on screen.
        sorted.Sort((a, b) => Math.Atan2(a.Y - cy, a.X - cx).CompareTo(Math.Atan2(b.Y - cy, b.X - cx)));
        var first = 0;
        for (var i = 1; i < sorted.Count; i++)
        {
            if (sorted[i].X + sorted[i].Y < sorted[first].X + sorted[first].Y)
            {
                first = i;
            }
        }

        var result = new (double X, double Y)[sorted.Count];
        for (var i = 0; i < sorted.Count; i++)
        {
            result[i] = sorted[(first + i) % sorted.Count];
        }

        return result;
    }

    private static byte[] BoxBlur(byte[] src, int width, int height, int radius)
    {
        var tmp = new int[src.Length];
        var dst = new byte[src.Length];
        var window = (2 * radius) + 1;
        for (var y = 0; y < height; y++)
        {
            var row = y * width;
            var sum = 0;
            for (var x = -radius; x <= radius; x++)
            {
                sum += src[row + Math.Clamp(x, 0, width - 1)];
            }

            for (var x = 0; x < width; x++)
            {
                tmp[row + x] = sum;
                sum += src[row + Math.Min(x + radius + 1, width - 1)] - src[row + Math.Max(x - radius, 0)];
            }
        }

        for (var x = 0; x < width; x++)
        {
            var sum = 0;
            for (var y = -radius; y <= radius; y++)
            {
                sum += tmp[(Math.Clamp(y, 0, height - 1) * width) + x];
            }

            for (var y = 0; y < height; y++)
            {
                dst[(y * width) + x] = (byte)(sum / (window * window));
                sum += tmp[(Math.Min(y + radius + 1, height - 1) * width) + x] - tmp[(Math.Max(y - radius, 0) * width) + x];
            }
        }

        return dst;
    }

    private static int Otsu(byte[] data)
    {
        var histogram = new long[256];
        foreach (var v in data)
        {
            histogram[v]++;
        }

        double total = data.Length;
        double sumAll = 0;
        for (var i = 0; i < 256; i++)
        {
            sumAll += i * (double)histogram[i];
        }

        double sumB = 0;
        double weightB = 0;
        double maxVariance = -1;
        var threshold = 127;
        for (var t = 0; t < 256; t++)
        {
            weightB += histogram[t];
            if (weightB == 0)
            {
                continue;
            }

            var weightF = total - weightB;
            if (weightF == 0)
            {
                break;
            }

            sumB += t * (double)histogram[t];
            var meanB = sumB / weightB;
            var meanF = (sumAll - sumB) / weightF;
            var variance = weightB * weightF * (meanB - meanF) * (meanB - meanF);
            if (variance > maxVariance)
            {
                maxVariance = variance;
                threshold = t;
            }
        }

        return threshold;
    }
}
