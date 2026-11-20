using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Pine.Core.Ai;
using Pine.Infrastructure.Ai.Extraction;
using Pine.Infrastructure.Ai.Extraction.LayoutLm;

// Usage: PineKieBaseline <ocr_dir> <predictions_dir> [--layoutlm <model.onnx> <tokenizer.json>]
// Reads <ocr_dir>/*.json  ({ "id", "image", "words": [ { "text", "confidence", "box": {"x","y","width","height"} } ] })
// Without --layoutlm (Phase 1 baseline): the heuristic extractor only -> <predictions_dir>/<id>.json
//   { id, merchant, total, date, tax, tax_supported:true }.  The heuristics report TaxAmount (summed tax rows).
// With --layoutlm (Phase 5): runs Pine's REAL LayoutLmFieldExtractor on the same OCR words + the page image and writes
//   <predictions_dir>             = layout model only
//   <predictions_dir>-hybrid      = heuristics overlaid by the model, exactly as DefaultLocalAiProvider does
//   <predictions_dir>-timings.json = per-document model time (ms), to report median/P95 end to end.
if (args.Length < 2) { Console.Error.WriteLine("usage: PineKieBaseline <ocr_dir> <predictions_dir> [--layoutlm <model.onnx> <tokenizer.json>]"); return 2; }
var ocrDir = args[0]; var outDir = args[1];
string? modelPath = null, tokenizerPath = null;
var flag = Array.IndexOf(args, "--layoutlm");
if (flag >= 0)
{
    if (args.Length < flag + 3) { Console.Error.WriteLine("--layoutlm needs <model.onnx> <tokenizer.json>"); return 2; }
    modelPath = args[flag + 1]; tokenizerPath = args[flag + 2];
}
Directory.CreateDirectory(outDir);
var hybridDir = outDir.TrimEnd('/', '\\') + "-hybrid";
if (modelPath is not null) { Directory.CreateDirectory(hybridDir); }
var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
var heuristic = new HeuristicFieldExtractor();
using var layout = modelPath is null ? null : new LayoutLmFieldExtractor(modelPath, tokenizerPath!);
var timings = new List<double>();
int n = 0, failed = 0;

static object Shape(string id, ReceiptExtractionResult r, bool taxSupported)
{
    // "Unknown"/0 are the extractors' own "nothing found" sentinels -> treat as missing.
    string? merchant = string.IsNullOrWhiteSpace(r.Merchant) || r.Merchant == "Unknown" ? null : r.Merchant;
    string? total = r.TotalAmount is > 0m ? r.TotalAmount.Value.ToString("0.00", CultureInfo.InvariantCulture) : null;
    string? date = r.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    string? tax = taxSupported && r.TaxAmount is > 0m ? r.TaxAmount.Value.ToString("0.00", CultureInfo.InvariantCulture) : null;
    return new { id, merchant, total, date, tax, tax_supported = taxSupported };
}

foreach (var file in Directory.EnumerateFiles(ocrDir, "*.json").OrderBy(f => f, StringComparer.Ordinal))
{
    var doc = JsonSerializer.Deserialize<OcrDoc>(File.ReadAllText(file), opts)!;
    var words = (doc.Words ?? []).Select(w => new OcrWord
    {
        Text = w.Text ?? "",
        Confidence = w.Confidence,
        BoundingBox = new BoundingBox { X = w.Box.X, Y = w.Box.Y, Width = w.Box.Width, Height = w.Box.Height },
    }).ToList();
    var name = Path.GetFileName(file);
    object pred, hybrid = new { };
    try
    {
        var h = heuristic.ExtractFields(words);
        if (layout is null)
        {
            pred = Shape(doc.Id, h, true); // heuristics report TaxAmount since the tax-model change
        }
        else
        {
            var sw = Stopwatch.StartNew();
            var l = layout.ExtractFields(words, File.ReadAllBytes(doc.Image!));
            sw.Stop();
            timings.Add(sw.Elapsed.TotalMilliseconds);
            pred = Shape(doc.Id, l, true);
            hybrid = Shape(doc.Id, LayoutLmResultMerger.Overlay(h, l), true);
        }
    }
    catch (Exception ex)
    {
        failed++;
        pred = new { id = doc.Id, merchant = (string?)null, total = (string?)null, date = (string?)null, tax = (string?)null, tax_supported = layout is not null, error = ex.GetType().Name + ": " + ex.Message };
        hybrid = pred;
    }
    File.WriteAllText(Path.Combine(outDir, name), JsonSerializer.Serialize(pred));
    if (layout is not null) { File.WriteAllText(Path.Combine(hybridDir, name), JsonSerializer.Serialize(hybrid)); }
    n++;
}
if (layout is not null)
{
    timings.Sort();
    var p95 = timings.Count == 0 ? 0 : timings[Math.Min(timings.Count - 1, (int)Math.Round(0.95 * (timings.Count - 1)))];
    var median = timings.Count == 0 ? 0 : timings[timings.Count / 2];
    File.WriteAllText(outDir.TrimEnd('/', '\\') + "-timings.json", JsonSerializer.Serialize(new
    {
        docs = timings.Count, median_ms = Math.Round(median, 1), p95_ms = Math.Round(p95, 1), failed,
        inferences = layout.InferenceCount, per_doc_ms = timings.Select(t => Math.Round(t, 1)),
    }));
    Console.WriteLine($"layoutlm docs={timings.Count} median={median:F0} ms p95={p95:F0} ms inferences={layout.InferenceCount}");
}
Console.WriteLine($"processed={n} failed={failed}");
return 0;

sealed record OcrBox(float X, float Y, float Width, float Height);
sealed record OcrW(string? Text, float Confidence, OcrBox Box);
sealed record OcrDoc(string Id, string? Image, List<OcrW>? Words);
