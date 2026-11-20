using Microsoft.Extensions.Logging;
using Pine.Core.Ai;

namespace Pine.Infrastructure.Ai.Extraction.LayoutLm;

/// <summary>
/// Runs the optional visual extractor on top of the heuristic result and makes sure it can never break a
/// scan: no extractor, a load failure or an inference failure all return the heuristic result unchanged
/// and log a warning that carries no receipt content.
/// </summary>
internal sealed class VisualExtractionGuard
{
    // After this many consecutive failures the optional model is left alone for the rest of the process:
    // a corrupt 128 MB file must not be re-parsed on every scan.
    internal const int MaxConsecutiveFailures = 3;

    private static readonly Action<ILogger, int, string, Exception?> ModelFailed = LoggerMessage.Define<int, string>(
        LogLevel.Warning,
        new EventId(4101, nameof(ModelFailed)),
        "The layout model could not read this receipt ({Failures} consecutive failure(s), {ExceptionType}); using the heuristic extraction.");

    private readonly IVisualFieldExtractor? _extractor;
    private readonly ILogger? _logger;
    private int _failures;

    public VisualExtractionGuard(IVisualFieldExtractor? extractor, ILogger? logger)
    {
        _extractor = extractor;
        _logger = logger;
    }

    /// <summary>Consecutive failures so far (reset by a success).</summary>
    public int ConsecutiveFailures => Volatile.Read(ref _failures);

    /// <summary>
    /// What the last <see cref="OverlayAsync"/> did: <c>ok</c> (the model's fields were merged), <c>off</c> (no model registered),
    /// <c>retired</c> (it failed <see cref="MaxConsecutiveFailures"/> times in a row and is left alone) or <c>failed</c> (this
    /// scan fell back to the heuristics). It exists so the scan diagnostics line says whether the model really contributed,
    /// which a bare <c>layoutlm_ms</c> cannot.
    /// </summary>
    public string LastOutcome { get; private set; } = "off";

    /// <summary>
    /// Overlays the model's fields onto <paramref name="heuristic"/>. The model gets the SAME image the OCR ran
    /// on, because the OCR boxes are in that image's coordinates.
    /// </summary>
    public async Task<ReceiptExtractionResult> OverlayAsync(
        ReceiptExtractionResult heuristic,
        IReadOnlyList<OcrWord> words,
        Stream image,
        CancellationToken cancellationToken)
    {
        if (_extractor is null || ConsecutiveFailures >= MaxConsecutiveFailures)
        {
            LastOutcome = _extractor is null ? "off" : "retired";
            return heuristic;
        }

        try
        {
            image.Position = 0;
            using var buffer = new MemoryStream();
            await image.CopyToAsync(buffer, cancellationToken);
            var visual = _extractor.ExtractFields(words, buffer.GetBuffer().AsMemory(0, (int)buffer.Length));
            Volatile.Write(ref _failures, 0);
            LastOutcome = "ok";
            return LayoutLmResultMerger.Overlay(heuristic, visual);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var failures = Interlocked.Increment(ref _failures);
            LastOutcome = "failed";
            if (_logger is not null)
            {
                // The exception object is not passed: a missing-file exception carries the user's full model path.
                ModelFailed(_logger, failures, ex.GetType().Name, null);
            }

            return heuristic;
        }
    }
}
