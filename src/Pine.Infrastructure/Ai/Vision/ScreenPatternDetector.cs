using System;
using SkiaSharp;

namespace Pine.Infrastructure.Ai.Vision;

/// <summary>
/// Tells a photo of a screen from a photo of paper by the periodic pattern a screen leaves (sub-pixel stripes and the moire
/// beat against the camera's own pixel grid): a sharp peak in the row or column power spectrum. Printed text has a broad
/// spectrum and no such peak.
/// </summary>
/// <remarks>
/// TUNED-CONSTANTS BOUNDARY (TD-118). The window (256), the sampled lines (32), the frequency band and the peak-to-median
/// threshold were calibrated on synthetic screen photos of the research corpus (docs/65 section 14), NOT on real screens; treat
/// the threshold as provisional. Literals on purpose; see <c>docs/13-AI-Architecture.md</c> section 7.1.
/// </remarks>
public static class ScreenPatternDetector
{
    /// <summary>Peak-to-median spectral ratio above which a picture is treated as a screen photo.</summary>
    public const double Threshold = 1500;

    private const int Window = 256;
    private const int Lines = 32;
    private const int FirstBin = 40; // period 6.4 px: text pitch and the receipt layout live below that
    private const int LastBin = 127; // period 2 px: Nyquist

    private static readonly double[] Hann = BuildHann();
    private static readonly double[] Cos = BuildTable(true);
    private static readonly double[] Sin = BuildTable(false);

    /// <summary>The larger of the row-wise and column-wise peak-to-median ratios; 5 to 340 for the clean scans, 6000 to 34000 for synthetic screen photos.</summary>
    public static double Score(SKBitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        if (bitmap.Width < Window || bitmap.Height < Window)
        {
            return 0;
        }

        // Only the middle window is read: converting a whole 12 MP picture to grey to look at 256 x 256 of it cost seconds on a tablet.
        var grey = ReceiptEnhancer.ToGreyRegion(bitmap, (bitmap.Width - Window) / 2, (bitmap.Height - Window) / 2, Window, Window);
        const int x0 = 0;
        const int y0 = 0;
        var horizontal = new double[LastBin + 1];
        var vertical = new double[LastBin + 1];
        var samples = new double[Window];
        for (var line = 0; line < Lines; line++)
        {
            var offset = (int)((long)line * (Window - 1) / (Lines - 1));
            for (var i = 0; i < Window; i++)
            {
                samples[i] = grey[((y0 + offset) * Window) + x0 + i];
            }

            Accumulate(samples, horizontal);
            for (var i = 0; i < Window; i++)
            {
                samples[i] = grey[((y0 + i) * Window) + x0 + offset];
            }

            Accumulate(samples, vertical);
        }

        return Math.Max(PeakToMedian(horizontal), PeakToMedian(vertical));
    }

    private static void Accumulate(double[] samples, double[] power)
    {
        double mean = 0;
        for (var i = 0; i < Window; i++)
        {
            mean += samples[i];
        }

        mean /= Window;
        for (var k = FirstBin; k <= LastBin; k++)
        {
            double re = 0;
            double im = 0;
            var table = k * Window;
            for (var i = 0; i < Window; i++)
            {
                var v = (samples[i] - mean) * Hann[i];
                re += v * Cos[table + i];
                im += v * Sin[table + i];
            }

            power[k] += (re * re) + (im * im);
        }
    }

    private static double PeakToMedian(double[] power)
    {
        // Bins at multiples of 32 are period 8, 4 and 2.67: the 8 x 8 blocks of JPEG, which every phone and scan has.
        var values = new System.Collections.Generic.List<double>();
        var peak = 0.0;
        for (var k = FirstBin; k <= LastBin; k++)
        {
            if ((k + 1) % 32 <= 2)
            {
                continue;
            }

            values.Add(power[k]);
            peak = Math.Max(peak, power[k]);
        }

        var band = values.ToArray();

        Array.Sort(band);
        var median = band[band.Length / 2];
        return peak / Math.Max(median, 1e-9);
    }

    private static double[] BuildHann()
    {
        var w = new double[Window];
        for (var i = 0; i < Window; i++)
        {
            w[i] = 0.5 - (0.5 * Math.Cos(2 * Math.PI * i / (Window - 1)));
        }

        return w;
    }

    private static double[] BuildTable(bool cos)
    {
        var t = new double[(LastBin + 1) * Window];
        for (var k = 0; k <= LastBin; k++)
        {
            for (var i = 0; i < Window; i++)
            {
                var angle = 2 * Math.PI * k * i / Window;
                t[(k * Window) + i] = cos ? Math.Cos(angle) : Math.Sin(angle);
            }
        }

        return t;
    }
}
