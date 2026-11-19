using System;
using System.Collections.Generic;

namespace Pine.Core.Ai;

/// <summary>
/// Extracts receipt fields from the OCR words AND the page image, for models that read both
/// (layout-aware transformers such as LayoutLMv3).
/// </summary>
/// <remarks>
/// <para>
/// WHY A SECOND PORT RATHER THAN A NEW PARAMETER ON <see cref="INlpFieldExtractor"/>. That interface
/// is satisfied by the heuristic extractor, takes no image, and has callers and tests that never have
/// one. Adding the image to it would force every caller to invent a picture or the heuristic class to
/// ignore an argument. A visual model needs pixels, so it gets its own contract, and the scan pipeline
/// (<c>DefaultLocalAiProvider</c>) is the single place that holds both.
/// </para>
/// <para>
/// IMPLEMENTATIONS THROW WHEN THE MODEL CANNOT BE LOADED OR RUN. The caller falls back to the heuristic
/// extractor and logs a warning; scanning is never allowed to fail because an optional model did.
/// Implementations must not use the network.
/// </para>
/// </remarks>
public interface IVisualFieldExtractor
{
    /// <summary>Extracts the fields the model can read; fields it did not find are <c>null</c>.</summary>
    /// <param name="ocrWords">The recognised lines, with pixel boxes in the coordinate space of <paramref name="encodedImage"/>.</param>
    /// <param name="encodedImage">The encoded image (PNG, JPEG, ...) the OCR engine ran on.</param>
    ReceiptExtractionResult ExtractFields(IReadOnlyList<OcrWord> ocrWords, ReadOnlyMemory<byte> encodedImage);
}
