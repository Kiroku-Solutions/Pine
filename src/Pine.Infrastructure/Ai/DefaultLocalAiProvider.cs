using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Pine.Core.Ai;
using Pine.Infrastructure.Ai.Vision;
using Pine.Infrastructure.Ai.Extraction;
using Pine.Infrastructure.Ai.Categorisation;
using Pine.Infrastructure.Ai.Extraction.LayoutLm;

namespace Pine.Infrastructure.Ai;

/// <summary>
/// On-device AI provider that orchestrates the full receipt scanning pipeline:
/// YOLO Detection → Image Preprocessing → PaddleOCR → Heuristic Field Extraction.
/// </summary>
public sealed class DefaultLocalAiProvider : IAIProvider, IDisposable
{
    private readonly IReceiptDetector _detector;
    private readonly bool _ownsDetector;
    private readonly IOcrEngine _ocr;
    private readonly bool _ownsOcr;
    private readonly HeuristicFieldExtractor _extractor;
    private readonly IMlNetCategorizer _categorizer;
    private readonly VisualExtractionGuard _visualGuard;

    /// <summary>Builds the on-device pipeline.</summary>
    /// <param name="categorizer">
    /// The transaction categoriser, INJECTED rather than constructed (TD-16). See the remarks.
    /// </param>
    /// <param name="loggerFactory">
    /// The factory the hand-constructed pipeline stages take their loggers from (TD-14). It is
    /// OPTIONAL so that this type keeps a constructor the container can call with nothing but what
    /// it already has: every head registers logging via <c>AddPineLogging</c>, so the container
    /// picks this constructor and supplies a real factory.
    /// </param>
    /// <remarks>
    /// <para>
    /// THE FACTORY, NOT AN <c>ILogger</c>. The stages below are separate categories -- an OCR
    /// failure and a model-load failure are different events with different owners -- and threading
    /// one logger through would collapse them into a single category named after this class.
    /// </para>
    /// <para>
    /// WHY THE CATEGORISER IS THE ONE STAGE THAT IS INJECTED. This constructor used to run
    /// <c>_categorizer = new MlNetCategorizer()</c>, which produced a SECOND categoriser alongside
    /// the one the container registered as <c>ICategorizationTrainer</c>. Training rebuilt the
    /// prediction engine on the container's instance; every prediction ran through this private
    /// one. The result was that training had no effect on categorisation until the process
    /// restarted. The other three stages stay hand-constructed because they are stateless loaders
    /// over shipped ONNX assets with nothing to share; the categoriser holds per-vault state, so it
    /// must be exactly one object and the container must own it.
    /// </para>
    /// </remarks>
    /// <param name="visualExtractor">
    /// The optional layout model (LayoutLMv3). When it is registered, its fields overlay the heuristic ones;
    /// when it is absent, cannot load or throws, extraction is exactly the heuristic one and a warning is logged.
    /// </param>
    /// <param name="detector">
    /// The detect/crop stage. The container supplies the OpenCV one or, on an OCR-only head, the Skia
    /// pass-through one, and owns it. When <see langword="null"/> the provider builds the OpenCV detector
    /// itself, as before, and disposes it.
    /// </param>
    /// <param name="ocrEngine">
    /// The OCR backend (docs/65). The container supplies the one chosen by <c>Pine:Ai:OcrBackend</c> and
    /// OWNS it. When it is <see langword="null"/> (hand-constructed callers, older tests) the provider falls
    /// back to the Paddle engine it always used, and then disposes it itself.
    /// </param>
    public DefaultLocalAiProvider(
        IMlNetCategorizer categorizer,
        ILoggerFactory? loggerFactory = null,
        IVisualFieldExtractor? visualExtractor = null,
        IOcrEngine? ocrEngine = null,
        IReceiptDetector? detector = null)
    {
        ArgumentNullException.ThrowIfNull(categorizer);

        _ownsDetector = detector is null;
        _detector = detector ?? CreateOpenCvDetector();
        _ownsOcr = ocrEngine is null;
        _ocr = ocrEngine ?? new PaddleOcrEngine(loggerFactory?.CreateLogger<PaddleOcrEngine>());
        // No logger and no LoadModel call any more (TD-36): the extractor's only log record was a
        // failure to load an ONNX model that was never used for inference, and whose file has never
        // existed in this repository. Extraction was always heuristic.
        _extractor = new HeuristicFieldExtractor();
        _categorizer = categorizer;
        _visualGuard = new VisualExtractionGuard(visualExtractor, loggerFactory?.CreateLogger<DefaultLocalAiProvider>());
    }

    // Separate method: only a caller that really falls back to OpenCV resolves ReceiptDetector (Android never does).
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static ReceiptDetector CreateOpenCvDetector() => new();

    public async Task<bool> DetectReceiptAsync(Stream imageStream, CancellationToken cancellationToken = default)
    {
        return await _detector.DetectReceiptAsync(imageStream, cancellationToken);
    }

    public async Task<float> DetectReceiptConfidenceAsync(Stream imageStream, CancellationToken cancellationToken = default)
    {
        return await _detector.DetectReceiptConfidenceAsync(imageStream, cancellationToken);
    }

    public async Task<ReceiptAnalysisResult> AnalyzeAndCropReceiptAsync(Stream imageStream, CancellationToken cancellationToken = default)
    {
        return await _detector.AnalyzeAndCropReceiptAsync(imageStream, cancellationToken);
    }

    public async Task<ReceiptExtractionResult> ExtractReceiptDataAsync(Stream imageStream, CancellationToken cancellationToken = default)
    {
        // P0 FIX: Preprocess image before OCR using the detector's stage when possible (OpenCV crop and
        // binarise on Windows; EXIF + downscale only on an OCR-only head).
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Stream ocrStream = imageStream;
        try
        {
            imageStream.Position = 0;
            var analysis = await _detector.AnalyzeAndCropReceiptAsync(imageStream, cancellationToken);
            if (analysis.PassedFilters && analysis.OcrImageStream != null)
            {
                // Use the preprocessed (upright, downscaled or cropped + binarised) image for OCR
                ocrStream = analysis.OcrImageStream;
            }
            else
            {
                // Fallback: use the original image
                imageStream.Position = 0;
                ocrStream = imageStream;
            }
        }
        catch
        {
            // If preprocessing fails, use original
            imageStream.Position = 0;
            ocrStream = imageStream;
        }

        var prepMs = clock.ElapsedMilliseconds;
        clock.Restart();

        // Execute OCR on the (potentially preprocessed) image
        ocrStream.Position = 0;
        byte[] ocrBytes;
        using (var copy = new MemoryStream())
        {
            await ocrStream.CopyToAsync(copy, cancellationToken);
            ocrBytes = copy.ToArray();
        }

        // Read; if the text runs at more than 3 degrees, level the picture and read again, so the row grouping and LayoutLM both work
        // on a level page (docs/65 section 14).
        var read = await ReceiptDeskew.ReadLevelAsync(_ocr, ocrBytes, cancellationToken);
        var textBlocks = read.Words;
        if (read.RemovedDegrees != 0)
        {
            ocrStream = new MemoryStream(read.Image, writable: false);
        }

        ocrStream.Position = 0;
        var ocrMs = clock.ElapsedMilliseconds;
        clock.Restart();

        // Extract fields using layout-aware heuristics
        var heuristic = _extractor.ExtractFields(textBlocks);
        var heuristicMs = clock.ElapsedMilliseconds;
        clock.Restart();
        var result = await _visualGuard.OverlayAsync(heuristic, textBlocks, ocrStream, cancellationToken);

        // Counts and timings only -- never a recognised word (30-Observability section 4.3).
        ScanDiagnostics.Report(
            $"scan prep_ms={prepMs} ocr_ms={ocrMs} lines={textBlocks.Count} heuristics_ms={heuristicMs} layoutlm_ms={clock.ElapsedMilliseconds}");
        return result;
    }

    public async Task<CategorisationResult> CategorizeTransactionAsync(string transactionText, decimal amount, CancellationToken cancellationToken = default)
    {
        return await _categorizer.PredictCategoryAsync(transactionText, amount, cancellationToken);
    }

    /// <summary>Disposes the stages this provider constructed.</summary>
    /// <remarks>
    /// THE CATEGORISER IS NOT DISPOSED HERE, AND THAT IS THE POINT OF INJECTING IT. It is a
    /// container-owned singleton shared with <c>ICategorizationTrainer</c>; disposing it from here
    /// would tear down the trainer's prediction engine as a side effect of this provider going
    /// away. Whoever constructs an object disposes it -- this type constructs the other three.
    /// </remarks>
    /// <remarks>
    /// The extractor is no longer among them (TD-36). It stopped being <c>IDisposable</c> when its
    /// unused <c>InferenceSession</c> was deleted; it now holds nothing that needs releasing.
    /// </remarks>
    public void Dispose()
    {
        if (_ownsDetector)
        {
            _detector.Dispose();
        }

        if (_ownsOcr && _ocr is IDisposable ocr)
        {
            ocr.Dispose();
        }
    }
}
