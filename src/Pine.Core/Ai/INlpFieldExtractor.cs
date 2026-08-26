using System.Collections.Generic;

namespace Pine.Core.Ai;

public interface INlpFieldExtractor : System.IDisposable
{
    ReceiptExtractionResult ExtractFields(IReadOnlyList<OcrWord> ocrWords);
    void LoadModel();
}
