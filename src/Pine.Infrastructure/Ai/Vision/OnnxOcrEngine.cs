using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Pine.Core.Ai;

namespace Pine.Infrastructure.Ai.Vision;

/// <summary>
/// Reads a receipt with the PP-OCRv5 detection and recognition networks as ONNX models. Managed pre- and
/// post-processing (<see cref="SkiaImagePreprocessor"/>, <see cref="DbPostProcessor"/>, <see cref="CtcDecoder"/>),
/// so it needs neither OpenCV nor Paddle Inference and can run on every head (docs/65).
/// </summary>
/// <remarks>
/// <para>
/// TUNED-CONSTANTS BOUNDARY (TD-118). The session options and the batch size below, like the thresholds in
/// <c>DbPostProcessor</c> and the sizes in <c>SkiaImagePreprocessor</c>, belong to the shipped model version.
/// They are literals, not <c>PineOptions</c>; change one only together with a parity re-run
/// (docs/65 section 5). See <c>docs/13-AI-Architecture.md</c> section 7.1.
/// </para>
/// <para>
/// The two <see cref="InferenceSession"/>s are created lazily on the first scan under a semaphore, like
/// <c>ReceiptDetector</c>, and live until <see cref="Dispose"/>. Inference runs on the thread pool, never on
/// the caller's thread. Nothing is downloaded: the models are files on disk. Logging carries counts and the
/// mean confidence, never recognised text (30-Observability section 4.3).
/// </para>
/// </remarks>
public sealed partial class OnnxOcrEngine : IOcrEngine, IDisposable
{
    // Lines with the same padded width share one recogniser call. The cap bounds peak activations on a phone.
    private const int RecBatchSize = 6;
    private const int MaxLines = 200;

    private readonly OnnxOcrOptions _options;
    private readonly ILogger<OnnxOcrEngine>? _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private InferenceSession? _det;
    private InferenceSession? _rec;
    private IReadOnlyList<string>? _charset;
    private bool _disposed;

    /// <summary>Creates the engine. Nothing is loaded until the first scan.</summary>
    /// <param name="options">Where the model files are.</param>
    /// <param name="logger">Optional; records failures and per-scan counts.</param>
    public OnnxOcrEngine(OnnxOcrOptions options, ILogger<OnnxOcrEngine>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OcrWord>> ExtractTextAsync(Stream imageStream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(imageStream);
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var memory = new MemoryStream();
        await imageStream.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
        var bytes = memory.ToArray();

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // Off the caller's thread: this method is awaited from view models, and a blocked UI thread
            // cannot even animate the scan screen's busy indicator.
            var words = await Task.Run(() => Recognise(bytes, cancellationToken), cancellationToken).ConfigureAwait(false);
            LogExtractionSummary(words);
            return words;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogExtractFailed(_logger, ex);
            throw;
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_rec is not null)
        {
            return;
        }

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_rec is not null)
            {
                return;
            }

            if (!_options.FilesArePresent)
            {
                // The sentence is for the UI (IUserFacingFailure); which file is missing goes to the log.
                LogModelsMissing(_logger);
                throw new AiCapabilityUnavailableException("receipt text recognition");
            }

            _charset = CtcDecoder.LoadCharset(_options.DictionaryPath);

            // Dynamic input shapes make ONNX Runtime's memory-pattern planner useless (it replans per shape and
            // keeps every plan), so it is off. Four intra-op threads is the most a phone's big cores can use.
            using var sessionOptions = new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                IntraOpNumThreads = Math.Min(4, Environment.ProcessorCount),
                InterOpNumThreads = 1,
                ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
                EnableMemoryPattern = false,
            };

            _det = new InferenceSession(_options.DetectionModelPath, sessionOptions);
            _rec = new InferenceSession(_options.RecognitionModelPath, sessionOptions);
        }
        finally
        {
            _lock.Release();
        }
    }

    private List<OcrWord> Recognise(byte[] encoded, CancellationToken cancellationToken)
    {
        var det = _det!;
        var rec = _rec!;
        var charset = _charset!;

        var image = SkiaImagePreprocessor.Decode(encoded);
        var boxes = Detect(det, image);
        if (boxes.Count > MaxLines)
        {
            boxes = boxes.Take(MaxLines).ToList();
        }

        // Crop every line, then group by padded width so lines can share a recogniser call.
        var inputs = new RecInput[boxes.Count];
        for (var i = 0; i < boxes.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            inputs[i] = SkiaImagePreprocessor.RecTensor(SkiaImagePreprocessor.CropLine(image, boxes[i].Quad));
        }

        var texts = new (string Text, float Confidence)[boxes.Count];
        foreach (var group in Enumerable.Range(0, boxes.Count).GroupBy(i => inputs[i].Width))
        {
            var indices = group.ToArray();
            for (var offset = 0; offset < indices.Length; offset += RecBatchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var batch = indices.Skip(offset).Take(RecBatchSize).ToArray();
                Recognise(rec, charset, batch, inputs, group.Key, texts);
            }
        }

        var words = new List<OcrWord>(boxes.Count);
        for (var i = 0; i < boxes.Count; i++)
        {
            // A line that decoded to nothing is not a word; the Paddle engine's callers never saw one either.
            if (string.IsNullOrWhiteSpace(texts[i].Text))
            {
                continue;
            }

            var quad = boxes[i].Quad;
            var minX = quad.Min(p => p.X);
            var maxX = quad.Max(p => p.X);
            var minY = quad.Min(p => p.Y);
            var maxY = quad.Max(p => p.Y);
            words.Add(new OcrWord
            {
                Text = texts[i].Text,
                Confidence = texts[i].Confidence,
                BoundingBox = new BoundingBox { X = (float)minX, Y = (float)minY, Width = (float)(maxX - minX), Height = (float)(maxY - minY) },
            });
        }

        return words;
    }

    private static IReadOnlyList<DetectedBox> Detect(InferenceSession det, BgrImage image)
    {
        var input = SkiaImagePreprocessor.DetPreprocess(image);
        using var tensor = OrtValue.CreateTensorValueFromMemory(input.Tensor, [1, 3, input.Height, input.Width]);
        var feeds = new Dictionary<string, OrtValue> { [det.InputNames[0]] = tensor };
        using var runOptions = new RunOptions();
        using var outputs = det.Run(runOptions, feeds, det.OutputNames);

        var map = outputs[0].GetTensorDataAsSpan<float>();
        return DbPostProcessor.Process(map, input.Width, input.Height, image.Width, image.Height);
    }

    private static void Recognise(
        InferenceSession rec,
        IReadOnlyList<string> charset,
        int[] batch,
        RecInput[] inputs,
        int width,
        (string Text, float Confidence)[] texts)
    {
        var lineSize = 3 * SkiaImagePreprocessor.RecHeight * width;
        var data = new float[batch.Length * lineSize];
        for (var b = 0; b < batch.Length; b++)
        {
            Array.Copy(inputs[batch[b]].Tensor, 0, data, b * lineSize, lineSize);
        }

        using var tensor = OrtValue.CreateTensorValueFromMemory(data, [batch.Length, 3, SkiaImagePreprocessor.RecHeight, width]);
        var feeds = new Dictionary<string, OrtValue> { [rec.InputNames[0]] = tensor };
        using var runOptions = new RunOptions();
        using var outputs = rec.Run(runOptions, feeds, rec.OutputNames);

        var shape = outputs[0].GetTensorTypeAndShape().Shape; // [N, T, C]
        var steps = (int)shape[1];
        var classes = (int)shape[2];
        var probs = outputs[0].GetTensorDataAsSpan<float>();
        for (var b = 0; b < batch.Length; b++)
        {
            texts[batch[b]] = CtcDecoder.Decode(probs.Slice(b * steps * classes, steps * classes), steps, classes, charset);
        }
    }

    private void LogExtractionSummary(List<OcrWord> words)
    {
        if (_logger is null || !_logger.IsEnabled(LogLevel.Debug))
        {
            return;
        }

        LogExtracted(_logger, words.Count, words.Count == 0 ? 0 : words.Average(w => (double)w.Confidence));
    }

    [LoggerMessage(
        EventId = 11,
        EventName = "ocr.onnx.models_missing",
        Level = LogLevel.Error,
        Message = "The ONNX OCR model files are missing; OCR is unavailable.")]
    private static partial void LogModelsMissingCore(ILogger logger);

    private static void LogModelsMissing(ILogger? logger)
    {
        if (logger is not null)
        {
            LogModelsMissingCore(logger);
        }
    }

    [LoggerMessage(
        EventId = 12,
        EventName = "ocr.onnx.extracted",
        Level = LogLevel.Debug,
        Message = "ONNX OCR recognised {RegionCount} regions with mean confidence {MeanConfidence}.")]
    private static partial void LogExtractedCore(ILogger logger, int regionCount, double meanConfidence);

    private static void LogExtracted(ILogger? logger, int regionCount, double meanConfidence)
    {
        if (logger is not null)
        {
            LogExtractedCore(logger, regionCount, meanConfidence);
        }
    }

    [LoggerMessage(
        EventId = 13,
        EventName = "ocr.onnx.extract_failed",
        Level = LogLevel.Error,
        Message = "ONNX OCR extraction failed.")]
    private static partial void LogExtractFailedCore(ILogger logger, Exception exception);

    private static void LogExtractFailed(ILogger? logger, Exception exception)
    {
        if (logger is not null)
        {
            LogExtractFailedCore(logger, exception);
        }
    }

    /// <summary>Releases both inference sessions.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _det?.Dispose();
        _rec?.Dispose();
        _lock.Dispose();
    }
}
