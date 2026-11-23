using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;

namespace Pine.Infrastructure.Ai.Vision;

/// <summary>
/// Runs <c>yolo-receipt-nano.onnx</c> (the model the Windows <c>ReceiptDetector</c> uses) with SkiaSharp pre-processing and the
/// managed <see cref="YoloPostProcessor"/>, so a head with no OpenCV can find the receipt the same way (docs/65 B2).
/// </summary>
/// <remarks>
/// TUNED-CONSTANTS BOUNDARY (TD-118). The 640 letterbox, the 114 grey padding and the thread budget are tuned with the model and
/// the device; the thresholds live in <see cref="YoloPostProcessor"/>. Literals on purpose; see
/// <c>docs/13-AI-Architecture.md</c> section 7.1.
/// </remarks>
public sealed class YoloReceiptSegmenter : IDisposable
{
    /// <summary>File name of the model inside the models directory (and next to the app on desktop).</summary>
    public const string ModelFileName = "yolo-receipt-nano.onnx";

    private const long MinimumRealModelBytes = 1_000_000; // a Git LFS pointer stub is ~130 bytes

    private readonly object _gate = new();

    /// <summary>
    /// True applies the OpenCV detector's refusals unchanged (edge contact, aspect below 1.15) -- used by the parity test. The default,
    /// false, is for still photos: only a quad under 10 % of the frame is refused, and corners are clamped into the picture.
    /// </summary>
    internal bool StrictFilters { get; set; }
    private readonly InferenceSession _session;
    private readonly string _inputName;

    private YoloReceiptSegmenter(InferenceSession session)
    {
        _session = session;
        _inputName = session.InputMetadata.Keys.First();
    }

    /// <summary>The segmenter, or null when the model file is absent, an LFS stub, or will not load.</summary>
    public static YoloReceiptSegmenter? TryCreate(string? modelDirectory)
    {
        var path = Path.Combine(modelDirectory ?? Path.Combine(AppContext.BaseDirectory, "Assets", "Ai"), ModelFileName);
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length < MinimumRealModelBytes)
            {
                return null;
            }

            // Leave two cores for the UI and the camera; a phone that is all-in on inference drops frames in the viewfinder.
            using var options = new SessionOptions
            {
                IntraOpNumThreads = Math.Max(1, Math.Min(3, Environment.ProcessorCount - 2)),
                InterOpNumThreads = 1,
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            };
            return new YoloReceiptSegmenter(new InferenceSession(path, options));
        }
        catch (Exception ex) when (ex is OnnxRuntimeException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Finds the receipt in an upright picture: its corners (image pixels, clockwise from top-left) and the model's confidence.</summary>
    internal (double X, double Y)[]? FindQuad(SKBitmap upright, out float confidence, out long inferenceMilliseconds)
        => FindQuad(upright, out confidence, out inferenceMilliseconds, out _, out _);

    /// <summary>As above, also returning why a pass failed (<paramref name="reason"/>: conf, nomask, border, aspect, area, nomodel) and the unfiltered quad.</summary>
    internal (double X, double Y)[]? FindQuad(
        SKBitmap upright, out float confidence, out long inferenceMilliseconds, out string reason, out (double X, double Y)[]? unfiltered)
    {
        try
        {
            return Run(upright, out confidence, out inferenceMilliseconds, out reason, out unfiltered);
        }
        catch (OnnxRuntimeException ex)
        {
            throw new InvalidOperationException("The receipt model failed to run.", ex);
        }
    }

    private (double X, double Y)[]? Run(
        SKBitmap upright, out float confidence, out long inferenceMilliseconds, out string reason, out (double X, double Y)[]? unfiltered)
    {
        confidence = 0f;
        inferenceMilliseconds = 0;
        reason = "ok";
        unfiltered = null;
        var box = YoloPostProcessor.ComputeLetterbox(upright.Width, upright.Height);
        var input = Preprocess(upright, box);

        float[] output0;
        int channels;
        float[] protos;
        var clock = Stopwatch.StartNew();
        lock (_gate)
        {
            using var results = _session.Run([NamedOnnxValue.CreateFromTensor(_inputName, input)]);
            inferenceMilliseconds = clock.ElapsedMilliseconds;
            if (results.Count < 2)
            {
                reason = "nomodel";
                return null; // a detection-only export has no mask: not the model this pipeline is tuned for
            }

            var first = results[0].AsTensor<float>();
            channels = first.Dimensions[1];
            if (channels < 4 + 1 + YoloPostProcessor.MaskChannels)
            {
                reason = "nomodel";
                return null;
            }

            output0 = first.ToArray();
            protos = results[1].AsTensor<float>().ToArray();
        }

        if (!YoloPostProcessor.TryPickAnchor(output0, channels, out var anchor, out confidence)
            || confidence < YoloPostProcessor.MinConfidence)
        {
            reason = "conf";
            return null;
        }

        var quad = YoloPostProcessor.QuadFromAnchor(output0, channels, anchor, protos, box);
        unfiltered = quad;
        if (quad is null)
        {
            reason = "nomask:" + ReceiptQuadFinder.LastWhy;
            return null;
        }

        reason = YoloPostProcessor.Refusal(quad, upright.Width, upright.Height, StrictFilters) ?? "ok";
        if (reason != "ok")
        {
            return null;
        }

        if (!StrictFilters)
        {
            for (var i = 0; i < 4; i++)
            {
                quad[i] = (Math.Clamp(quad[i].X, 0, upright.Width - 1), Math.Clamp(quad[i].Y, 0, upright.Height - 1));
            }
        }

        return quad;
    }

    private static DenseTensor<float> Preprocess(SKBitmap source, YoloPostProcessor.Letterbox box)
    {
        const int size = YoloPostProcessor.InputSize;
        using var resized = source.Resize(
            new SKImageInfo(box.Width, box.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul),
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear))
            ?? throw new InvalidOperationException("The image could not be resized.");

        var tensor = new DenseTensor<float>([1, 3, size, size]);
        var data = tensor.Buffer.Span;
        data.Fill(114f / 255f);
        var plane = size * size;
        var pixels = resized.GetPixelSpan();
        for (var y = 0; y < box.Height; y++)
        {
            var row = y * resized.RowBytes;
            var target = ((y + box.Top) * size) + box.Left;
            for (var x = 0; x < box.Width; x++)
            {
                var i = row + (x * 4);
                data[target + x] = pixels[i + 2] / 255f;
                data[plane + target + x] = pixels[i + 1] / 255f;
                data[(2 * plane) + target + x] = pixels[i] / 255f;
            }
        }

        return tensor;
    }

    public void Dispose() => _session.Dispose();
}
