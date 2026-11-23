using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Pine.Core.Ai;

/// <summary>
/// Reads the text lines of a receipt image. One implementation per OCR backend (docs/65 section 6):
/// the Paddle Inference engine on Windows and the managed ONNX engine on every head.
/// </summary>
/// <remarks>
/// <para>
/// ONE METHOD ON PURPOSE. <c>DefaultLocalAiProvider</c> used to construct <c>PaddleOcrEngine</c> with
/// <c>new</c>, which made the OCR backend a property of the provider's source code. With a seam, the
/// backend is a composition decision (<c>Pine:Ai:OcrBackend</c>) and a test can supply a fake.
/// </para>
/// <para>
/// Boxes are axis-aligned pixel rectangles in the coordinates of the decoded image, in reading order.
/// Implementations must not run their heavy work on the caller's thread.
/// </para>
/// </remarks>
public interface IOcrEngine
{
    /// <summary>Recognises the text lines in an encoded image (PNG, JPEG, ...).</summary>
    /// <param name="imageStream">The encoded image, positioned at its start.</param>
    /// <param name="cancellationToken">Cancels the scan.</param>
    /// <returns>One <see cref="OcrWord"/> per detected text line, top to bottom.</returns>
    Task<IReadOnlyList<OcrWord>> ExtractTextAsync(Stream imageStream, CancellationToken cancellationToken);
}
