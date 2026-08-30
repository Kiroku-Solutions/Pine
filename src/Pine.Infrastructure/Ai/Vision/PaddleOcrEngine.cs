using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Pine.Core.Ai;
using Sdcb.PaddleOCR;
using Sdcb.PaddleOCR.Models.Local;
using Sdcb.PaddleInference;
using OpenCvSharp;

// OpenCvSharp also defines a LogLevel, and this file names Microsoft's in every logging attribute.
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Pine.Infrastructure.Ai.Vision;

/// <summary>
/// Uses Sdcb.PaddleOCR to process receipt images.
/// Uses the LatinV5 models for robust extraction in Spanish and other latin languages.
/// </summary>
public sealed partial class PaddleOcrEngine : IDisposable
{
    private readonly ILogger<PaddleOcrEngine>? _logger;

    private PaddleOcrAll? _engine;
    private bool _initialized;

    /// <summary>Creates the engine and initialises the PaddleOCR models.</summary>
    /// <param name="logger">
    /// Where initialisation and extraction failures are recorded (TD-14/TD-25). OPTIONAL, and
    /// nullable rather than a null-object logger, because this type is hand-constructed by
    /// <c>DefaultLocalAiProvider</c> rather than resolved from a container; making it required would
    /// have broken that call site and every test that news one up. The container supplies a real one
    /// through <c>ILoggerFactory</c>.
    /// </param>
    public PaddleOcrEngine(ILogger<PaddleOcrEngine>? logger = null)
    {
        _logger = logger;

        try
        {
            Initialize();
        }
        catch (Exception ex)
        {
            // WAS: File.AppendAllText into "ocr_init_error.log" next to the executable, with the
            // whole exception dumped verbatim -- unredacted, in a directory that is read-only on a
            // packaged install, in a format nothing reads (TD-25).
            LogInitCrashed(_logger, ex);
            _engine = null;
        }
    }

    private void Initialize()
    {
        try
        {
            // Initialize PaddleOcrAll with the built-in PP-OCRv5 Latin models (perfect for Spanish)
            _engine = new PaddleOcrAll(LocalFullModels.LatinV5, PaddleDevice.Mkldnn())
            {
                AllowRotateDetection = false, 
                Enable180Classification = false
            };
            _initialized = true;
        }
        catch (Exception ex)
        {
            // WAS: a second File.AppendAllText into the same ocr_init_error.log. See the constructor.
            LogInitFailed(_logger, ex);
            _engine = null;
        }
    }

    /// <summary>
    /// Extracts text from a stream and formats it as OcrWord list.
    /// </summary>
    public async Task<List<OcrWord>> ExtractTextAsync(Stream imageStream, CancellationToken cancellationToken)
    {
        if (!_initialized || _engine == null)
        {
            return new List<OcrWord>
            {
                new() { Text = "ERROR_MODELS_NOT_FOUND", Confidence = 1.0f, BoundingBox = new BoundingBox { X = 50, Y = 100, Width = 200, Height = 40 } }
            };
        }

        var blocks = new List<OcrWord>();

        try
        {
            // Copy stream to memory
            using var ms = new MemoryStream();
            await imageStream.CopyToAsync(ms, cancellationToken);
            byte[] imageBytes = ms.ToArray();

            // OFF THE CALLER'S THREAD. Detection and recognition are two more neural networks, and
            // on a full-page receipt they are the slowest stage of the whole scan -- comfortably
            // longer than the YOLO pass before them. Running them inline is what made the window
            // stop painting: this method is awaited from a view model, so its continuation was the
            // UI thread. The scan screen's busy indicator existed all along and could not animate,
            // because a blocked UI thread does not run animations either.
            var engine = _engine;
            PaddleOcrResult ocrResult = await Task.Run(
                () =>
                {
                    // Sdcb uses OpenCvSharp Mat natively
                    using Mat mat = Cv2.ImDecode(imageBytes, ImreadModes.Color);
                    return engine.Run(mat);
                },
                cancellationToken).ConfigureAwait(false);

            if (ocrResult?.Regions != null)
            {
                foreach (var region in ocrResult.Regions)
                {
                    // Calculate bounding box from the 4 corners
                    var points = region.Rect.Points();
                    float minX = Math.Min(Math.Min(points[0].X, points[1].X), Math.Min(points[2].X, points[3].X));
                    float maxX = Math.Max(Math.Max(points[0].X, points[1].X), Math.Max(points[2].X, points[3].X));
                    float minY = Math.Min(Math.Min(points[0].Y, points[1].Y), Math.Min(points[2].Y, points[3].Y));
                    float maxY = Math.Max(Math.Max(points[0].Y, points[1].Y), Math.Max(points[2].Y, points[3].Y));
                    
                    blocks.Add(new OcrWord
                    {
                        Text = region.Text,
                        Confidence = region.Score,
                        BoundingBox = new BoundingBox
                        {
                            X = minX,
                            Y = minY,
                            Width = maxX - minX,
                            Height = maxY - minY
                        }
                    });
                }

                // TD-25. THE LINE THAT USED TO BE HERE LOGGED region.Text -- the full recognised
                // text of every region of every receipt the user scanned -- to the console. That is
                // the merchant, the line items and the total, which 30-Observability §4.3 forbids a
                // log from carrying in any form, aggregated or not. It is deleted, not redacted:
                // there is no redaction of a receipt's contents that leaves anything worth keeping.
                // What a diagnostic legitimately needs is how MUCH was read and how confidently,
                // and both are numbers.
                LogExtractionSummary(blocks);
            }
        }
        catch (Exception ex)
        {
            LogExtractFailed(_logger, ex);

            return new List<OcrWord>
            {
                new() { Text = "OCR_ERROR_CRASH", Confidence = 1.0f, BoundingBox = new BoundingBox { X = 50, Y = 100, Width = 200, Height = 40 } },
                new() { Text = ex.Message, Confidence = 1.0f, BoundingBox = new BoundingBox { X = 50, Y = 150, Width = 200, Height = 40 } }
            };
        }

        return blocks;
    }

    /// <summary>
    /// Records how much text was recognised and how confidently -- never WHAT was recognised.
    /// </summary>
    /// <remarks>
    /// Both values are written as numbers, so they pass through <c>PineLogRecordFormatter</c>'s
    /// numeric path rather than its redacting string path; a count of 42 regions is a diagnostic,
    /// not an amount. Debug level, because this fires once per scan and 30-Observability §4.2
    /// reserves <c>debug</c> for exactly this: diagnostic detail for maintainers.
    /// </remarks>
    private void LogExtractionSummary(List<OcrWord> blocks)
    {
        if (_logger is null || !_logger.IsEnabled(LogLevel.Debug))
        {
            return;
        }

        var meanConfidence = 0d;

        if (blocks.Count > 0)
        {
            var total = 0d;

            foreach (var block in blocks)
            {
                total += block.Confidence;
            }

            meanConfidence = total / blocks.Count;
        }

        LogExtracted(_logger, blocks.Count, meanConfidence);
    }

    // The source-generated LoggerMessage delegates. CA1848 is an ERROR in this repository, so every
    // record has to go through one of these rather than through an ILogger extension method; the
    // generator is also what stamps the stable, dotted, snake_case EventName that
    // 30-Observability §4.1 requires in the "event" field.
    //
    // Each takes a NULLABLE logger and returns early: this type is hand-constructed by
    // DefaultLocalAiProvider, so there may genuinely be no logger, and pushing the null check in
    // here keeps every call site a single unconditional line.

    [LoggerMessage(
        EventId = 1,
        EventName = "ocr.init_crashed",
        Level = LogLevel.Error,
        Message = "PaddleOCR initialisation crashed.")]
    private static partial void LogInitCrashedCore(ILogger logger, Exception exception);

    private static void LogInitCrashed(ILogger? logger, Exception exception)
    {
        if (logger is not null)
        {
            LogInitCrashedCore(logger, exception);
        }
    }

    [LoggerMessage(
        EventId = 2,
        EventName = "ocr.init_failed",
        Level = LogLevel.Error,
        Message = "PaddleOCR model initialisation failed; OCR is unavailable.")]
    private static partial void LogInitFailedCore(ILogger logger, Exception exception);

    private static void LogInitFailed(ILogger? logger, Exception exception)
    {
        if (logger is not null)
        {
            LogInitFailedCore(logger, exception);
        }
    }

    [LoggerMessage(
        EventId = 3,
        EventName = "ocr.extracted",
        Level = LogLevel.Debug,
        Message = "OCR recognised {RegionCount} regions with mean confidence {MeanConfidence}.")]
    private static partial void LogExtractedCore(ILogger logger, int regionCount, double meanConfidence);

    private static void LogExtracted(ILogger? logger, int regionCount, double meanConfidence)
    {
        if (logger is not null)
        {
            LogExtractedCore(logger, regionCount, meanConfidence);
        }
    }

    [LoggerMessage(
        EventId = 4,
        EventName = "ocr.extract_failed",
        Level = LogLevel.Error,
        Message = "OCR extraction failed.")]
    private static partial void LogExtractFailedCore(ILogger logger, Exception exception);

    private static void LogExtractFailed(ILogger? logger, Exception exception)
    {
        if (logger is not null)
        {
            LogExtractFailedCore(logger, exception);
        }
    }

    public void Dispose()
    {
        _engine?.Dispose();
    }
}
