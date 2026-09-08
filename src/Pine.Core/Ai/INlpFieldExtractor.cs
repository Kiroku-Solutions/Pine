using System.Collections.Generic;

namespace Pine.Core.Ai;

/// <summary>
/// Pulls the fields Pine cares about out of a page of OCR words.
/// </summary>
/// <remarks>
/// <para>
/// IT NO LONGER DECLARES <c>LoadModel()</c> OR <c>IDisposable</c>, AND THAT IS THE WHOLE OF TD-36.
/// The one implementation held an <c>InferenceSession</c> over
/// <c>Assets/Ai/ner_minilm_int8.onnx</c> — a file that has never existed in this repository — and
/// referenced it in exactly four places: the declaration, a null check, the assignment, and
/// <c>Dispose</c>. It was never once used to run inference. So <c>LoadModel()</c> loaded a model
/// nothing consulted, and <c>IDisposable</c> existed to dispose it.
/// </para>
/// <para>
/// The extraction itself was always heuristic — spatial line clustering, zone detection and
/// regular expressions — and is unchanged by this. What is gone is an interface that advertised a
/// machine-learning step Pine does not perform, which is the part that misled readers rather than
/// the part that cost anything at runtime.
/// </para>
/// </remarks>
public interface INlpFieldExtractor
{
    /// <summary>Extracts the receipt's fields from the words the OCR engine read.</summary>
    /// <param name="ocrWords">The recognised words, with their positions and confidences.</param>
    /// <returns>The fields found, each with its own confidence band.</returns>
    ReceiptExtractionResult ExtractFields(IReadOnlyList<OcrWord> ocrWords);
}
