namespace Pine.Core.Ai;

/// <summary>Which engine reads the text of a scanned receipt (docs/65-Onnx-Ocr-Design.md).</summary>
public enum OcrBackend
{
    /// <summary>
    /// Sdcb.PaddleOCR on Paddle Inference + OpenCV. Windows only (its native runtimes carry win-x64
    /// assets). The default until the ONNX engine has been proven on the same fixtures.
    /// </summary>
    Paddle = 0,

    /// <summary>
    /// The managed engine: the same PP-OCRv5 weights as ONNX models run by ONNX Runtime, with SkiaSharp and
    /// plain C# for the pre/post-processing. Runs on every head.
    /// </summary>
    Onnx = 1,
}
