using System.Text;
using System.Text.Json;
using Pine.Core.Ai;
using Xunit;

namespace Pine.Tests.Unit.Ai.LayoutLm;

/// <summary>
/// Locates the optional LayoutLM files for tests. Nothing here is an absolute path: the environment
/// variables win, otherwise the paths are relative to the repository root (the <c>research/</c> folder).
/// </summary>
internal static class LayoutLmTestAssets
{
    public const string ModelEnvironmentVariable = "PINE_LAYOUTLM_MODEL";
    public const string TokenizerEnvironmentVariable = "PINE_LAYOUTLM_TOKENIZER";

    public static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Pine.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Pine.slnx not found above the test output.");
    }

    public static string ModelPath() =>
        Environment.GetEnvironmentVariable(ModelEnvironmentVariable) is { Length: > 0 } fromEnv
            ? fromEnv
            : Path.Combine(RepositoryRoot(), "research", "colab", "onnx", "model_int8_emb.onnx");

    public static string TokenizerPath() =>
        Environment.GetEnvironmentVariable(TokenizerEnvironmentVariable) is { Length: > 0 } fromEnv
            ? fromEnv
            : Path.Combine(RepositoryRoot(), "research", "models", "layoutlmv3_run1", "final", "tokenizer.json");

    public static string FixtureDirectory() =>
        Path.Combine(RepositoryRoot(), "tests", "Pine.Tests.Unit", "Ai", "Fixtures");

    public static string ProcessedDataDirectory() => Path.Combine(RepositoryRoot(), "research", "data", "processed");

    /// <summary>A minimal byte-level tokenizer.json (256 byte symbols, no merges): enough to test chunking offline.</summary>
    public static string WriteSyntheticTokenizer(string directory)
    {
        var printable = new List<int>();
        printable.AddRange(Enumerable.Range('!', '~' - '!' + 1));
        printable.AddRange(Enumerable.Range(0xA1, 0xAC - 0xA1 + 1));
        printable.AddRange(Enumerable.Range(0xAE, 0xFF - 0xAE + 1));
        var symbols = new List<string>();
        var n = 0;
        for (var b = 0; b < 256; b++)
        {
            symbols.Add(printable.Contains(b) ? ((char)b).ToString() : ((char)(256 + n++)).ToString());
        }

        var vocab = new Dictionary<string, int> { ["<s>"] = 0, ["<pad>"] = 1, ["</s>"] = 2, ["<unk>"] = 3 };
        foreach (var s in symbols)
        {
            vocab[s] = vocab.Count;
        }

        var json = JsonSerializer.Serialize(new
        {
            added_tokens = new[]
            {
                new { id = 0, content = "<s>" }, new { id = 1, content = "<pad>" },
                new { id = 2, content = "</s>" }, new { id = 3, content = "<unk>" },
            },
            model = new { type = "BPE", vocab, merges = Array.Empty<string>() },
        });
        var path = Path.Combine(directory, "tokenizer.json");
        File.WriteAllText(path, json, Encoding.UTF8);
        return path;
    }

    public static OcrWord Word(string text, float x, float y, float width, float height, float confidence = 0.9f) =>
        new() { Text = text, Confidence = confidence, BoundingBox = new BoundingBox { X = x, Y = y, Width = width, Height = height } };
}

/// <summary>A fact that is reported as skipped (not passed) when the optional model files are absent.</summary>
internal sealed class RequiresLayoutLmModelFactAttribute : FactAttribute
{
    public RequiresLayoutLmModelFactAttribute()
    {
        if (!File.Exists(LayoutLmTestAssets.ModelPath()) || !File.Exists(LayoutLmTestAssets.TokenizerPath()))
        {
            Skip = $"LayoutLM model/tokenizer not found. Set {LayoutLmTestAssets.ModelEnvironmentVariable} and "
                 + $"{LayoutLmTestAssets.TokenizerEnvironmentVariable}, or place them under research/.";
        }
    }
}

/// <summary>A fact skipped when only the tokenizer.json (not the model) is absent.</summary>
internal sealed class RequiresLayoutLmTokenizerFactAttribute : FactAttribute
{
    public RequiresLayoutLmTokenizerFactAttribute()
    {
        if (!File.Exists(LayoutLmTestAssets.TokenizerPath()))
        {
            Skip = $"tokenizer.json not found. Set {LayoutLmTestAssets.TokenizerEnvironmentVariable} or place it under research/.";
        }
    }
}
