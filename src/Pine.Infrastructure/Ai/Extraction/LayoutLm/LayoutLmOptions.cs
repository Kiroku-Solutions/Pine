namespace Pine.Infrastructure.Ai.Extraction.LayoutLm;

/// <summary>
/// Where the optional LayoutLMv3 field-extraction model lives. The model is interchangeable: any
/// ONNX export with the same inputs and outputs (see <see cref="LayoutLmFieldExtractor"/>) works.
/// </summary>
/// <remarks>
/// <para>
/// THE MODEL IS NOT SHIPPED WITH THE APPLICATION. It is about 128 MB (int8, embeddings quantised)
/// and its licence is unresolved, so it is loaded from a configurable path and is OFF until a
/// head or a user supplies one. With no model the scan pipeline is exactly the heuristic one.
/// </para>
/// <para>
/// TUNED-CONSTANTS BOUNDARY (TD-118). Only paths and the on/off switch are options. The window
/// length, stride, label set and image size describe the exported graph and its training run, not a
/// preference, so they are literals in the classes that use them.
/// </para>
/// </remarks>
public sealed class LayoutLmOptions
{
    /// <summary>The ONNX model file. <c>null</c> or empty disables the extractor.</summary>
    public string? ModelPath { get; set; }

    /// <summary>
    /// The Hugging Face <c>tokenizer.json</c> the model was trained with. When <c>null</c> it is looked up
    /// beside the model file.
    /// </summary>
    public string? TokenizerPath { get; set; }

    /// <summary>Switch to turn the extractor off without forgetting the paths. Default on.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>True when the extractor should be registered and tried.</summary>
    public bool IsConfigured => Enabled && !string.IsNullOrWhiteSpace(ModelPath);

    /// <summary>The tokenizer path to use: the explicit one, or <c>tokenizer.json</c> beside the model.</summary>
    public string? ResolveTokenizerPath()
    {
        if (!string.IsNullOrWhiteSpace(TokenizerPath))
        {
            return TokenizerPath;
        }

        var directory = string.IsNullOrWhiteSpace(ModelPath) ? null : System.IO.Path.GetDirectoryName(ModelPath);
        return directory is null ? null : System.IO.Path.Combine(directory, "tokenizer.json");
    }
}
