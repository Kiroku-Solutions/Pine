using FluentAssertions;
using OpenCvSharp;
using Pine.Infrastructure.Ai.Vision;
using SkiaSharp;
using Xunit.Abstractions;

namespace Pine.Tests.Unit.Ai.Ocr;

/// <summary>The managed enhancement against OpenCV on real research images, the sharpness score, and the screen detector.</summary>
public sealed class ReceiptEnhancerTests(ITestOutputHelper output)
{
    private static SKBitmap Load(string id) => SKBitmap.Decode(OcrTestAssets.ImagePath(id));

    private static Mat ToMat(SKBitmap bitmap)
    {
        var mat = new Mat(bitmap.Height, bitmap.Width, MatType.CV_8UC4);
        System.Runtime.InteropServices.Marshal.Copy(bitmap.Bytes, 0, mat.Data, bitmap.Bytes.Length);
        return mat;
    }

    [RequiresOcrParityDataFact]
    public void Sharpen_and_the_adaptive_threshold_match_opencv_away_from_the_border()
    {
        const int margin = 12;
        foreach (var id in OcrTestAssets.ParityIds.Take(5))
        {
            using var bitmap = Load(id);
            using var bgra = new SKBitmap(bitmap.Width, bitmap.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
            using (var canvas = new SKCanvas(bgra))
            {
                canvas.DrawBitmap(bitmap, 0, 0);
            }

            using var bgr = new Mat();
            using var src = ToMat(bgra);
            Cv2.CvtColor(src, bgr, ColorConversionCodes.BGRA2BGR);

            using var kernel = new Mat(3, 3, MatType.CV_32FC1);
            kernel.SetArray(new float[] { 0, -1, 0, -1, 5, -1, 0, -1, 0 });
            using var cvSharp = new Mat();
            Cv2.Filter2D(bgr, cvSharp, -1, kernel);
            using var mySharp = ReceiptEnhancer.Sharpen(bgra);
            double diff = 0;
            long n = 0;
            for (var y = margin; y < bgra.Height - margin; y += 3)
            {
                for (var x = margin; x < bgra.Width - margin; x += 3)
                {
                    var c = mySharp.GetPixel(x, y);
                    var v = cvSharp.At<Vec3b>(y, x);
                    diff += Math.Abs(c.Blue - v.Item0) + Math.Abs(c.Green - v.Item1) + Math.Abs(c.Red - v.Item2);
                    n += 3;
                }
            }

            (diff / n).Should().BeLessThan(0.5, $"{id}: mean absolute channel difference of the sharpen");

            using var grey = new Mat();
            Cv2.CvtColor(bgr, grey, ColorConversionCodes.BGR2GRAY);
            Cv2.GaussianBlur(grey, grey, new OpenCvSharp.Size(5, 5), 0);
            using var cvBin = new Mat();
            Cv2.AdaptiveThreshold(grey, cvBin, 255, AdaptiveThresholdTypes.GaussianC, ThresholdTypes.Binary, 21, 10);
            using var myBin = ReceiptEnhancer.ForOcr(bgra, OcrEnhancement.Binarize);
            long differing = 0;
            long total = 0;
            for (var y = margin; y < bgra.Height - margin; y += 3)
            {
                for (var x = margin; x < bgra.Width - margin; x += 3)
                {
                    total++;
                    if ((myBin.GetPixel(x, y).Red > 127) != (cvBin.At<byte>(y, x) > 127))
                    {
                        differing++;
                    }
                }
            }

            var share = (double)differing / total;
            output.WriteLine($"{id}: sharpen mean abs diff {diff / n:F3}, binarize pixels differing {share:P2}");
            share.Should().BeLessThan(0.03, $"{id}: the managed threshold differs from OpenCV's only by blur rounding");
        }
    }

    [Fact]
    public void A_blurred_picture_scores_lower_sharpness_than_the_sharp_one()
    {
        using var sharp = new SKBitmap(200, 200, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using (var canvas = new SKCanvas(sharp))
        {
            canvas.Clear(SKColors.White);
            using var ink = new SKPaint { Color = SKColors.Black, StrokeWidth = 2 };
            for (var y = 10; y < 200; y += 9)
            {
                canvas.DrawLine(10, y, 190, y, ink);
            }
        }

        using var blurred = new SKBitmap(200, 200, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using (var canvas = new SKCanvas(blurred))
        using (var paint = new SKPaint { ImageFilter = SKImageFilter.CreateBlur(2.5f, 2.5f) })
        {
            canvas.DrawBitmap(sharp, 0, 0, paint);
        }

        ReceiptEnhancer.LaplacianVariance(sharp).Should().BeGreaterThan(ReceiptEnhancer.LaplacianVariance(blurred) * 3);
    }

    [RequiresOcrParityDataFact]
    public void The_screen_detector_separates_clean_scans_from_synthetic_screen_photos()
    {
        var clean = new List<double>();
        var screen = new List<double>();
        foreach (var id in OcrTestAssets.ParityIds)
        {
            using var original = Load(id);
            using var scaled = original.Resize(new SKImageInfo(1000, (int)(1000.0 * original.Height / original.Width), SKColorType.Bgra8888, SKAlphaType.Opaque), new SKSamplingOptions(SKFilterMode.Linear))!;
            clean.Add(ScreenPatternDetector.Score(scaled));
            using var photo = ReceiptEnhancementExperimentTests.ScreenPhoto(original, id.GetHashCode());
            screen.Add(ScreenPatternDetector.Score(photo));
        }

        output.WriteLine($"clean  scores: min {clean.Min():F1} median {clean.OrderBy(v => v).ElementAt(clean.Count / 2):F1} max {clean.Max():F1}");
        output.WriteLine($"screen scores: min {screen.Min():F1} median {screen.OrderBy(v => v).ElementAt(screen.Count / 2):F1} max {screen.Max():F1}");
        clean.Max().Should().BeLessThan(ScreenPatternDetector.Threshold);
        screen.Count(v => v > ScreenPatternDetector.Threshold).Should().BeGreaterThanOrEqualTo((int)(screen.Count * 0.9));
    }
}
