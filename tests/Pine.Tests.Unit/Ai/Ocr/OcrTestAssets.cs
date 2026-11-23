using System.Text.Json;
using Pine.Infrastructure.Ai.Vision;
using Pine.Tests.Unit.Ai.LayoutLm;

namespace Pine.Tests.Unit.Ai.Ocr;

/// <summary>Locates the ONNX OCR models and the research data for tests; nothing here is an absolute path.</summary>
internal static class OcrTestAssets
{
    public const string ModelDirectoryEnvironmentVariable = "PINE_OCR_MODELS";
    public const string ParityReferenceEnvironmentVariable = "PINE_OCR_PARITY_REF";
    public const string ParityPaddleEnvironmentVariable = "PINE_OCR_PARITY_PADDLE";

    /// <summary>The 20 test-split images of docs/65 section 5 (the same ids the Python run used).</summary>
    public static readonly string[] ParityIds =
    [
        "cord_test_00001", "cord_test_00095", "cord_train_00140", "cord_train_00200", "cord_train_00268",
        "cord_train_00341", "cord_train_00410", "cord_train_00514", "cord_train_00562", "cord_train_00658",
        "cord_train_00732", "cord_validation_00090", "sroie_train_00011", "sroie_train_00103", "sroie_train_00196",
        "sroie_train_00291", "sroie_train_00328", "sroie_train_00415", "sroie_train_00528", "sroie_train_00606",
    ];

    public static string FixtureDirectory() => LayoutLmTestAssets.FixtureDirectory();

    /// <summary>The model options: the environment variable, else the folder the build copies the shipped models to.</summary>
    public static OnnxOcrOptions Options() =>
        OnnxOcrOptions.ForDirectory(Environment.GetEnvironmentVariable(ModelDirectoryEnvironmentVariable) is { Length: > 0 } dir ? dir : null);

    public static bool ModelsPresent() => Options().FilesArePresent;

    public static string ParityDirectory() => Path.Combine(LayoutLmTestAssets.RepositoryRoot(), "research", "data", "ocr_onnx_ref");

    public static string ParityReferencePath() =>
        Environment.GetEnvironmentVariable(ParityReferenceEnvironmentVariable) is { Length: > 0 } p ? p : Path.Combine(ParityDirectory(), "onnx_reference.json");

    public static string ParityPaddlePath() =>
        Environment.GetEnvironmentVariable(ParityPaddleEnvironmentVariable) is { Length: > 0 } p ? p : Path.Combine(ParityDirectory(), "pine960_paddle.json");

    public static string ImagePath(string id) =>
        Path.Combine(LayoutLmTestAssets.ProcessedDataDirectory(), id.Split('_')[0], "images", id + ".png");

    public static string LabelPath(string id) =>
        Path.Combine(LayoutLmTestAssets.ProcessedDataDirectory(), id.Split('_')[0], "labels", id + ".json");

    public static bool ParityDataPresent() =>
        ModelsPresent()
        && File.Exists(ParityReferencePath())
        && ParityIds.All(id => File.Exists(ImagePath(id)) && File.Exists(LabelPath(id)));

    /// <summary>One line of a reference run: text and an axis-aligned box.</summary>
    internal readonly record struct Line(string Text, double X, double Y, double Width, double Height);

    /// <summary>Reads a run written by onnx_ocr_reference.py or the Paddle baseline: id to lines (box from "box" or "poly").</summary>
    public static Dictionary<string, List<Line>> LoadRun(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var result = new Dictionary<string, List<Line>>();
        foreach (var page in doc.RootElement.EnumerateObject())
        {
            var lines = new List<Line>();
            foreach (var w in page.Value.GetProperty("words").EnumerateArray())
            {
                double x, y, width, height;
                if (w.TryGetProperty("box", out var b))
                {
                    (x, y, width, height) = (b.GetProperty("x").GetDouble(), b.GetProperty("y").GetDouble(), b.GetProperty("width").GetDouble(), b.GetProperty("height").GetDouble());
                }
                else
                {
                    var xs = w.GetProperty("poly").EnumerateArray().Select(p => p[0].GetDouble()).ToArray();
                    var ys = w.GetProperty("poly").EnumerateArray().Select(p => p[1].GetDouble()).ToArray();
                    (x, y, width, height) = (xs.Min(), ys.Min(), xs.Max() - xs.Min(), ys.Max() - ys.Min());
                }

                lines.Add(new Line(w.GetProperty("text").GetString() ?? string.Empty, x, y, width, height));
            }

            result[page.Name] = lines;
        }

        return result;
    }
}

/// <summary>The comparison of compare() in onnx_ocr_reference.py, ported so the numbers are comparable with docs/65 section 5.</summary>
internal static class OcrComparison
{
    internal sealed record Result(int Lines, int Matched, int Exact, int NormalisedExact, double Cer, double MeanIou, int Extra)
    {
        public double MatchRate => Lines == 0 ? 1 : (double)Matched / Lines;

        public double ExactRate => Matched == 0 ? 0 : (double)Exact / Matched;

        public override string ToString() =>
            $"lines={Lines} matched(IoU>=.5)={Matched} ({MatchRate:P1}) extra={Extra} exact={ExactRate:P1} "
            + $"exactNormalised={(Matched == 0 ? 0 : (double)NormalisedExact / Matched):P1} CER={Cer:P2} meanIoU={MeanIou:F3}";
    }

    /// <summary>Greedy: each reference line takes the best unused candidate line with IoU at least 0.5.</summary>
    public static Result Compare(
        IReadOnlyDictionary<string, List<OcrTestAssets.Line>> reference,
        IReadOnlyDictionary<string, List<OcrTestAssets.Line>> candidate,
        IEnumerable<string> ids)
    {
        int total = 0, matched = 0, exact = 0, nexact = 0, ed = 0, ch = 0, extra = 0;
        var ious = new List<double>();
        foreach (var id in ids)
        {
            var wa = reference[id];
            var wb = candidate[id];
            var used = new HashSet<int>();
            foreach (var a in wa)
            {
                total++;
                var best = 0.0;
                var bj = -1;
                for (var j = 0; j < wb.Count; j++)
                {
                    if (used.Contains(j))
                    {
                        continue;
                    }

                    var s = Iou(a, wb[j]);
                    if (s > best)
                    {
                        best = s;
                        bj = j;
                    }
                }

                if (best >= 0.5)
                {
                    used.Add(bj);
                    matched++;
                    ious.Add(best);
                    exact += a.Text == wb[bj].Text ? 1 : 0;
                    var na = Norm(a.Text);
                    var nb = Norm(wb[bj].Text);
                    nexact += na == nb ? 1 : 0;
                    ed += Levenshtein(na, nb);
                    ch += Math.Max(na.Length, 1);
                }
            }

            extra += wb.Count - used.Count;
        }

        return new Result(total, matched, exact, nexact, ch == 0 ? 0 : (double)ed / ch, ious.Count == 0 ? 0 : ious.Average(), extra);
    }

    public static string Norm(string text) => string.Concat(text.Where(c => !char.IsWhiteSpace(c))).ToLowerInvariant();

    public static double Iou(OcrTestAssets.Line a, OcrTestAssets.Line b)
    {
        var iw = Math.Max(0, Math.Min(a.X + a.Width, b.X + b.Width) - Math.Max(a.X, b.X));
        var ih = Math.Max(0, Math.Min(a.Y + a.Height, b.Y + b.Height) - Math.Max(a.Y, b.Y));
        var inter = iw * ih;
        var union = (a.Width * a.Height) + (b.Width * b.Height) - inter;
        return union > 0 ? inter / union : 0;
    }

    public static int Levenshtein(string a, string b)
    {
        var prev = Enumerable.Range(0, b.Length + 1).ToArray();
        for (var i = 1; i <= a.Length; i++)
        {
            var cur = new int[b.Length + 1];
            cur[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                cur[j] = Math.Min(Math.Min(prev[j] + 1, cur[j - 1] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            }

            prev = cur;
        }

        return prev[b.Length];
    }
}

/// <summary>A fact skipped (reported as skipped, not passed) when the ONNX OCR models are absent or only Git LFS pointers.</summary>
internal sealed class RequiresOcrModelsFactAttribute : FactAttribute
{
    public RequiresOcrModelsFactAttribute()
    {
        if (!OcrTestAssets.ModelsPresent())
        {
            Skip = $"ONNX OCR models not found (or Git LFS pointers). Run git lfs pull, or set {OcrTestAssets.ModelDirectoryEnvironmentVariable}.";
        }
    }
}

/// <summary>A fact skipped when the research data or the Python reference output of the parity run is absent (for example in CI).</summary>
internal sealed class RequiresOcrParityDataFactAttribute : FactAttribute
{
    public RequiresOcrParityDataFactAttribute()
    {
        if (!OcrTestAssets.ParityDataPresent())
        {
            Skip = "OCR parity data not found: needs the models, research/data/processed images and labels, and the Python reference run "
                 + $"({OcrTestAssets.ParityReferencePath()}, or set {OcrTestAssets.ParityReferenceEnvironmentVariable}).";
        }
    }
}
