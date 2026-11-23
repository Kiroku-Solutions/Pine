using System;
using System.IO;

namespace Pine.Infrastructure.Ai.Vision;

/// <summary>Where the ONNX OCR backend finds its three model files (docs/65 section 7).</summary>
/// <remarks>
/// <para>
/// The files are named here, as literals, because <c>ShippedAssetsAreNamedByProductionCode</c> requires the
/// production code that loads a shipped asset to name it. The default directory is anchored to
/// <see cref="AppContext.BaseDirectory"/> (TD-46), never to the working directory.
/// </para>
/// <para>
/// TUNED-CONSTANTS BOUNDARY (TD-118): only paths are options. The detector and recogniser constants
/// (limit side, thresholds, unclip ratio, widths) are literals in <c>SkiaImagePreprocessor</c> and
/// <c>DbPostProcessor</c>.
/// </para>
/// </remarks>
public sealed class OnnxOcrOptions
{
    /// <summary>File name of the text-detection network (PP-OCRv5 mobile, DB head).</summary>
    public const string DetectionFileName = "det.onnx";

    /// <summary>File name of the text-recognition network (latin PP-OCRv5 mobile, CTC head).</summary>
    public const string RecognitionFileName = "rec.onnx";

    /// <summary>File name of the recogniser's character dictionary.</summary>
    public const string DictionaryFileName = "dict_latin_v5.txt";

    /// <summary>The detection model.</summary>
    public string DetectionModelPath { get; set; } = string.Empty;

    /// <summary>The recognition model.</summary>
    public string RecognitionModelPath { get; set; } = string.Empty;

    /// <summary>The dictionary.</summary>
    public string DictionaryPath { get; set; } = string.Empty;

    /// <summary>The default folder: <c>Assets/Ai/ocr</c> beside the application.</summary>
    public static string DefaultDirectory { get; } = Path.Combine(AppContext.BaseDirectory, "Assets", "Ai", "ocr");

    /// <summary>Options for a directory holding the three files; <see langword="null"/> or empty means <see cref="DefaultDirectory"/>.</summary>
    /// <param name="directory">The directory, or <see langword="null"/> for the shipped one.</param>
    /// <returns>The options.</returns>
    public static OnnxOcrOptions ForDirectory(string? directory)
    {
        var dir = string.IsNullOrWhiteSpace(directory) ? DefaultDirectory : directory;
        return new OnnxOcrOptions
        {
            DetectionModelPath = Path.Combine(dir, DetectionFileName),
            RecognitionModelPath = Path.Combine(dir, RecognitionFileName),
            DictionaryPath = Path.Combine(dir, DictionaryFileName),
        };
    }

    /// <summary>
    /// True when all three files exist and the models are real files rather than Git LFS pointer stubs
    /// (a checkout without LFS leaves a ~130 byte text file where a model should be).
    /// </summary>
    public bool FilesArePresent =>
        IsRealFile(DetectionModelPath, 100_000) && IsRealFile(RecognitionModelPath, 100_000) && IsRealFile(DictionaryPath, 100);

    private static bool IsRealFile(string path, long minimumBytes) => File.Exists(path) && new FileInfo(path).Length >= minimumBytes;
}
