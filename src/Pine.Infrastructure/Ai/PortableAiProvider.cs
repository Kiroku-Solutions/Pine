using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Pine.Core.Ai;
using Pine.Infrastructure.Ai.Categorisation;

namespace Pine.Infrastructure.Ai;

/// <summary>
/// The <see cref="IAIProvider"/> for a head that has the categoriser but not the camera: it
/// categorises transactions, and refuses the four vision operations with a sentence (TD-90).
/// </summary>
/// <remarks>
/// <para>
/// WHAT SPLITS THE INTERFACE IN TWO. <see cref="IAIProvider"/> bundles two subsystems with very
/// different portability. Categorisation is ML.NET over text the vault already holds. The other
/// four methods are the receipt pipeline -- YOLO detection, OpenCV geometry, PaddleOCR -- and every
/// one of those depends on a native binary that arrives through <c>OpenCvSharp4.runtime.win</c> and
/// <c>Sdcb.PaddleInference.runtime.win64.mkl</c>. Those two packages have <em>win-x64 assets and no
/// others</em> IN THIS REPOSITORY'S PIN SET: the macOS, Android and iOS heads reference
/// <c>Pine.Infrastructure</c> and therefore compile against the managed wrappers, and would fail at
/// the first P/Invoke. For macOS that is a pin this project chose not to add -- the osx runtime
/// packages exist at the pinned versions -- and the Phase 3 platform review left it that way for
/// want of a machine to test on. For Android and iOS no such package exists at all.
/// </para>
/// <para>
/// WHY REFUSING IS BETTER THAN THE TWO ALTERNATIVES. Returning an empty
/// <see cref="ReceiptExtractionResult"/> would let the scan screen show a blank receipt as though
/// OCR had run and found nothing, which is the worst of the three outcomes -- it produces a
/// financial record from a document nobody read. Letting the native call happen produces a process
/// abort with no managed exception to catch. Throwing <see cref="AiCapabilityUnavailableException"/>
/// keeps the failure inside the managed world, inside the TD-24 taxonomy, and inside one sentence.
/// </para>
/// <para>
/// THE CATEGORISER IS INJECTED, NOT CONSTRUCTED -- the same rule
/// <see cref="DefaultLocalAiProvider"/> follows and for the same reason: it holds per-vault state
/// and there must be exactly one, shared with <c>ICategorizationTrainer</c>.
/// </para>
/// </remarks>
public sealed class PortableAiProvider : IAIProvider
{
    private readonly IMlNetCategorizer _categorizer;

    /// <summary>Builds the provider over the container's single categoriser.</summary>
    /// <param name="categorizer">The shared, vault-scoped categoriser.</param>
    public PortableAiProvider(IMlNetCategorizer categorizer)
    {
        ArgumentNullException.ThrowIfNull(categorizer);
        _categorizer = categorizer;
    }

    /// <inheritdoc />
    public Task<bool> DetectReceiptAsync(Stream imageStream, CancellationToken cancellationToken = default)
        => throw new AiCapabilityUnavailableException("receipt detection");

    /// <inheritdoc />
    public Task<float> DetectReceiptConfidenceAsync(Stream imageStream, CancellationToken cancellationToken = default)
        => throw new AiCapabilityUnavailableException("receipt detection");

    /// <inheritdoc />
    public Task<ReceiptAnalysisResult> AnalyzeAndCropReceiptAsync(Stream imageStream, CancellationToken cancellationToken = default)
        => throw new AiCapabilityUnavailableException("receipt cropping");

    /// <inheritdoc />
    public Task<ReceiptExtractionResult> ExtractReceiptDataAsync(Stream imageStream, CancellationToken cancellationToken = default)
        => throw new AiCapabilityUnavailableException("receipt OCR");

    /// <inheritdoc />
    /// <remarks>
    /// THE ONE METHOD THAT REALLY WORKS HERE, and the reason this class is not simply a stub that
    /// throws five times. Import and the categorise query both reach the AI subsystem only through
    /// this method; wiring them to a total refusal would break importing a CSV on four heads to
    /// express a limitation that only applies to the camera.
    /// </remarks>
    public Task<CategorisationResult> CategorizeTransactionAsync(string transactionText, decimal amount, CancellationToken cancellationToken = default)
        => _categorizer.PredictCategoryAsync(transactionText, amount, cancellationToken);
}
