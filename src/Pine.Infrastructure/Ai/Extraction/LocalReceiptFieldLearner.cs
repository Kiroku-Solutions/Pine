using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Pine.Core.Ai;
using Pine.Core.Security;

namespace Pine.Infrastructure.Ai.Extraction;

/// <summary>
/// Lightweight, encrypted, per-vault on-device receipt layout learner.
/// Trains asynchronously in the background from user-drawn bounding boxes so Spanish, LatAm, and
/// store-specific receipt formats improve progressively with near-zero CPU/RAM overhead.
/// </summary>
public sealed partial class LocalReceiptFieldLearner : IReceiptFieldLearner, IDisposable
{
    public const string ModelDirectoryName = "ai";
    public const string ModelFileName = "receipt-layout.model";
    private const int MaxStoredAnnotations = 200;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private readonly IVaultDataKeyAccessor _vault;
    private readonly IVaultFileCipher _cipher;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<LocalReceiptFieldLearner>? _logger;

    private readonly object _stateGate = new();
    private readonly SemaphoreSlim _trainLock = new(1, 1);
    private readonly CancellationTokenSource _disposeCts = new();

    private string? _loadedVaultPath;
    private PersistedReceiptModel _model = new();
    private long _annotationVersion;
    private int _pendingTrainJobs;
    private bool _disposed;

    public LocalReceiptFieldLearner(
        IVaultDataKeyAccessor vault,
        IVaultFileCipher cipher,
        TimeProvider? timeProvider = null,
        ILogger<LocalReceiptFieldLearner>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(cipher);

        _vault = vault;
        _cipher = cipher;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger;
    }

    /// <inheritdoc />
    public ReceiptTrainingStatus GetStatus()
    {
        lock (_stateGate)
        {
            EnsureLoadedLocked();
            return new ReceiptTrainingStatus(_model.Annotations.Count, _model.LastTrainedAtUtc, _pendingTrainJobs > 0);
        }
    }

    /// <inheritdoc />
    public Task<ReceiptTrainingStatus> RecordAnnotationAndTrainInBackgroundAsync(
        ReceiptLayoutAnnotation annotation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(annotation);

        if (annotation.Boxes.Count == 0 || _disposed)
        {
            return Task.FromResult(GetStatus());
        }

        int count;
        DateTimeOffset? lastTrained;
        var vaultPath = _vault.IsUnlocked ? _vault.DbPath : null;

        lock (_stateGate)
        {
            EnsureLoadedLocked();
            var list = new List<ReceiptLayoutAnnotation>(_model.Annotations);
            list.Add(annotation);
            if (list.Count > MaxStoredAnnotations)
            {
                list.RemoveRange(0, list.Count - MaxStoredAnnotations);
            }

            _model = _model with { Annotations = list };
            _annotationVersion++;
            _pendingTrainJobs++;
            count = list.Count;
            lastTrained = _model.LastTrainedAtUtc;
        }

        // Low-resource background training bound to learner lifetime (never the caller's request token).
        _ = Task.Run(() => TrainAndPersistAsync(vaultPath, _disposeCts.Token), CancellationToken.None);

        return Task.FromResult(new ReceiptTrainingStatus(count, lastTrained, IsTraining: true));
    }

    /// <summary>
    /// Synchronously trains on current annotations and flushes the encrypted model. Exposed internally for tests.
    /// </summary>
    internal async Task TrainNowAsync(CancellationToken cancellationToken = default)
    {
        var vaultPath = _vault.IsUnlocked ? _vault.DbPath : null;
        lock (_stateGate)
        {
            _pendingTrainJobs++;
        }

        await TrainAndPersistAsync(vaultPath, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public ReceiptExtractionResult ApplyLearnedModel(
        ReceiptExtractionResult baseline,
        IReadOnlyList<OcrWord> ocrWords,
        int imageWidth,
        int imageHeight)
    {
        ArgumentNullException.ThrowIfNull(baseline);

        if (ocrWords is null || ocrWords.Count == 0)
        {
            return baseline;
        }

        var snapshot = EnsureLoadedForCurrentVault();
        if (snapshot.GlobalPrototypes.Count == 0 && snapshot.MerchantTemplates.Count == 0)
        {
            return baseline;
        }

        var (w, h) = ReceiptBoxExtractor.ResolveDimensions(ocrWords, imageWidth, imageHeight);
        if (w <= 0 || h <= 0)
        {
            return baseline;
        }

        var lines = BuildNormalizedLines(ocrWords, w, h);
        if (lines.Count == 0)
        {
            return baseline;
        }

        var fullTextFolded = FoldText(string.Join(" ", lines.Select(l => l.Text)));
        var matchedTemplate = MatchMerchantTemplate(snapshot.MerchantTemplates, fullTextFolded, baseline.Merchant);

        var separator = ReceiptNumberParser.Infer(lines.Select(l => l.Text));
        var dayFirst = ReceiptDateParser.InferDayFirst(lines.Select(l => l.Text));

        var merchant = baseline.Merchant;
        var merchantConf = baseline.Confidence.Merchant;
        var total = baseline.TotalAmount;
        var totalConf = baseline.Confidence.Total;
        var date = baseline.Date;
        var dateConf = baseline.Confidence.Date;
        var dateAmbiguous = baseline.DateIsAmbiguous;
        var altDate = baseline.AlternativeDate;
        var tax = baseline.TaxAmount;

        // 1. Merchant
        var merchantProto = ResolvePrototype(ReceiptTagField.Merchant, matchedTemplate, snapshot);
        if (merchantProto is not null)
        {
            var (predMerchant, score) = PredictMerchant(lines, merchantProto);
            if (!string.IsNullOrWhiteSpace(predMerchant)
                && ShouldOverride(score, merchantConf, matchedTemplate is not null || string.IsNullOrWhiteSpace(merchant) || merchant.StartsWith("Unknown", StringComparison.OrdinalIgnoreCase)))
            {
                merchant = predMerchant;
                merchantConf = FieldConfidence.FromWordScores([score]);
            }
        }

        // 2. Total
        var totalProto = ResolvePrototype(ReceiptTagField.Total, matchedTemplate, snapshot);
        if (totalProto is not null)
        {
            var (predTotal, score) = PredictAmount(lines, totalProto, separator);
            if (predTotal is > 0m
                && ShouldOverride(score, totalConf, matchedTemplate is not null || total is null or <= 0m))
            {
                total = predTotal;
                totalConf = FieldConfidence.FromWordScores([score]);
            }
        }

        // 3. Date
        var dateProto = ResolvePrototype(ReceiptTagField.Date, matchedTemplate, snapshot);
        if (dateProto is not null)
        {
            var (predDate, score) = PredictDate(lines, dateProto, dayFirst);
            if (predDate is { } parsedDate
                && ShouldOverride(score, dateConf, matchedTemplate is not null || date is null))
            {
                date = parsedDate.Value;
                dateAmbiguous = parsedDate.IsAmbiguous;
                altDate = parsedDate.Alternative;
                dateConf = FieldConfidence.FromWordScores([score]);
            }
        }

        // 4. Tax
        var taxProto = ResolvePrototype(ReceiptTagField.Tax, matchedTemplate, snapshot);
        if (taxProto is not null)
        {
            var (predTax, score) = PredictAmount(lines, taxProto, separator);
            if (predTax is >= 0m
                && (total is null || total <= 0m || predTax.Value < total.Value)
                && (tax is null || (matchedTemplate is not null && score >= 0.72f)))
            {
                tax = predTax;
            }
        }

        return baseline with
        {
            Merchant = merchant,
            TotalAmount = total,
            Date = date,
            DateIsAmbiguous = dateAmbiguous,
            AlternativeDate = altDate,
            TaxAmount = tax,
            Confidence = baseline.Confidence with
            {
                Merchant = merchantConf,
                Total = totalConf,
                Date = dateConf,
            },
        };
    }

    private async Task TrainAndPersistAsync(string? expectedVaultPath, CancellationToken cancellationToken)
    {
        var lockAcquired = false;
        try
        {
            await _trainLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            lockAcquired = true;

            List<ReceiptLayoutAnnotation> annotations;
            long capturedVersion;
            lock (_stateGate)
            {
                annotations = new List<ReceiptLayoutAnnotation>(_model.Annotations);
                capturedVersion = _annotationVersion;
            }

            var trainedAt = _timeProvider.GetUtcNow();
            var trained = BuildTrainedModel(annotations, trainedAt);

            lock (_stateGate)
            {
                // Preserve any newer annotations added while training ran (even when capped at MaxStoredAnnotations).
                if (_annotationVersion != capturedVersion)
                {
                    trained = BuildTrainedModel(_model.Annotations, trainedAt);
                }

                _model = trained;
            }

            if (!_disposed
                && !string.IsNullOrEmpty(expectedVaultPath)
                && _vault.IsUnlocked
                && string.Equals(_vault.DbPath, expectedVaultPath, StringComparison.Ordinal))
            {
                PersistEncrypted(expectedVaultPath, trained);
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelled during disposal.
        }
        catch (ObjectDisposedException)
        {
            // Learner was disposed while background training queued.
        }
        catch (Exception ex)
        {
            if (_logger is not null)
            {
                LogTrainingFailed(_logger, ex);
            }
        }
        finally
        {
            if (lockAcquired)
            {
                try { _trainLock.Release(); } catch (ObjectDisposedException) { }
            }

            lock (_stateGate)
            {
                if (_pendingTrainJobs > 0)
                {
                    _pendingTrainJobs--;
                }
            }
        }
    }

    private static PersistedReceiptModel BuildTrainedModel(
        IReadOnlyList<ReceiptLayoutAnnotation> annotations,
        DateTimeOffset trainedAtUtc)
    {
        var global = new Dictionary<string, FieldPrototype>(StringComparer.Ordinal);
        foreach (var field in Enum.GetValues<ReceiptTagField>())
        {
            var boxes = annotations
                .SelectMany(a => a.Boxes)
                .Where(b => b.Field == field)
                .ToList();

            if (boxes.Count > 0)
            {
                global[field.ToString()] = BuildPrototype(boxes);
            }
        }

        var merchantTemplates = new Dictionary<string, MerchantLayoutTemplate>(StringComparer.Ordinal);
        var byMerchant = annotations
            .Where(a => !string.IsNullOrWhiteSpace(ResolveMerchantKey(a)))
            .GroupBy(a => ResolveMerchantKey(a)!, StringComparer.Ordinal);

        foreach (var group in byMerchant)
        {
            var fields = new Dictionary<string, FieldPrototype>(StringComparer.Ordinal);
            foreach (var field in Enum.GetValues<ReceiptTagField>())
            {
                var boxes = group.SelectMany(a => a.Boxes).Where(b => b.Field == field).ToList();
                if (boxes.Count > 0)
                {
                    fields[field.ToString()] = BuildPrototype(boxes);
                }
            }

            if (fields.Count > 0)
            {
                var tokens = TokenizeSignificant(group.Key);
                merchantTemplates[group.Key] = new MerchantLayoutTemplate(group.Key, tokens, fields, group.Count());
            }
        }

        return new PersistedReceiptModel
        {
            Version = 1,
            LastTrainedAtUtc = trainedAtUtc,
            Annotations = annotations.ToList(),
            GlobalPrototypes = global,
            MerchantTemplates = merchantTemplates,
        };
    }

    private static FieldPrototype BuildPrototype(List<ReceiptFieldBox> boxes)
    {
        var meanX = boxes.Average(b => b.Box.CenterX);
        var meanY = boxes.Average(b => b.Box.CenterY);
        var meanW = boxes.Average(b => b.Box.Width);
        var meanH = boxes.Average(b => b.Box.Height);

        var stdX = Math.Max(0.10, Math.Sqrt(boxes.Average(b => Math.Pow(b.Box.CenterX - meanX, 2))));
        var stdY = Math.Max(0.06, Math.Sqrt(boxes.Average(b => Math.Pow(b.Box.CenterY - meanY, 2))));

        var leftAnchorWeights = new Dictionary<string, int>(StringComparer.Ordinal);
        var aboveAnchorWeights = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var box in boxes)
        {
            foreach (var token in TokenizeSignificant(box.LeftAnchorText))
            {
                leftAnchorWeights[token] = leftAnchorWeights.GetValueOrDefault(token) + 1;
            }

            foreach (var token in TokenizeSignificant(box.AboveAnchorText))
            {
                aboveAnchorWeights[token] = aboveAnchorWeights.GetValueOrDefault(token) + 1;
            }
        }

        return new FieldPrototype(
            meanX,
            meanY,
            meanW,
            meanH,
            stdX,
            stdY,
            boxes.Count,
            leftAnchorWeights,
            aboveAnchorWeights);
    }

    private static string? ResolveMerchantKey(ReceiptLayoutAnnotation annotation)
    {
        var fromBox = annotation.Boxes.FirstOrDefault(b => b.Field == ReceiptTagField.Merchant)?.ExtractedText;
        var candidate = !string.IsNullOrWhiteSpace(fromBox) ? fromBox : annotation.MerchantHint;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        var folded = FoldText(candidate);
        return folded.Length >= 3 ? folded : null;
    }

    private static MerchantLayoutTemplate? MatchMerchantTemplate(
        Dictionary<string, MerchantLayoutTemplate> templates,
        string fullReceiptFolded,
        string? baselineMerchant)
    {
        if (templates.Count == 0)
        {
            return null;
        }

        var baselineFolded = FoldText(baselineMerchant);
        MerchantLayoutTemplate? best = null;
        var bestScore = 0.0;

        foreach (var template in templates.Values)
        {
            if (template.Tokens.Count == 0)
            {
                continue;
            }

            if (!string.IsNullOrEmpty(baselineFolded) && string.Equals(baselineFolded, template.MerchantKey, StringComparison.Ordinal))
            {
                return template;
            }

            var hits = template.Tokens.Count(t => fullReceiptFolded.Contains(t, StringComparison.Ordinal));
            var ratio = hits / (double)template.Tokens.Count;
            if (ratio >= 0.65 && ratio > bestScore)
            {
                bestScore = ratio;
                best = template;
            }
        }

        return best;
    }

    private static FieldPrototype? ResolvePrototype(
        ReceiptTagField field,
        MerchantLayoutTemplate? template,
        PersistedReceiptModel model)
    {
        var key = field.ToString();
        if (template is not null && template.Fields.TryGetValue(key, out var specific))
        {
            return specific;
        }

        return model.GlobalPrototypes.TryGetValue(key, out var global) ? global : null;
    }

    private static bool ShouldOverride(float learnedScore, FieldConfidence baselineConfidence, bool preferLearned)
    {
        if (learnedScore < 0.52f)
        {
            return false;
        }

        if (baselineConfidence.Band is ConfidenceBand.NotFound or ConfidenceBand.Low)
        {
            return true;
        }

        return preferLearned && learnedScore >= 0.72f;
    }

    private static (string? Merchant, float Score) PredictMerchant(
        List<NormalizedLine> lines,
        FieldPrototype proto)
    {
        string? best = null;
        var bestScore = 0.0;

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var text = line.Text.Trim(' ', '*', '-', '.', ':', '|');
            if (text.Length < 3 || !text.Any(char.IsLetter) || text.Count(char.IsDigit) > text.Count(char.IsLetter))
            {
                continue;
            }

            var spatial = SpatialSimilarity(line.Rect, proto);
            var above = i > 0 ? AnchorSimilarity(lines[i - 1].Text, proto.AboveAnchorWeights) : 0.0;
            var score = (spatial * 0.75) + (above * 0.25);

            if (score > bestScore)
            {
                bestScore = score;
                best = text;
            }
        }

        return (best, (float)Math.Clamp(bestScore, 0.0, 0.99));
    }

    private static (decimal? Amount, float Score) PredictAmount(
        List<NormalizedLine> lines,
        FieldPrototype proto,
        DecimalSeparator separator)
    {
        decimal? bestAmount = null;
        var bestScore = 0.0;

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var amount = ReceiptBoxExtractor.TryParseAmount(line.Text, separator);
            if (amount is null)
            {
                continue;
            }

            // Pick the rightmost numeric word's box as the value's position.
            var valueWord = line.Words.LastOrDefault(w => ReceiptBoxExtractor.TryParseAmount(w.Word.Text, separator) is not null);
            var targetRect = valueWord.Word.Text is not null ? valueWord.Rect : line.Rect;

            var spatial = SpatialSimilarity(targetRect, proto);
            var leftText = string.Join(" ", line.Words.TakeWhile(w => w.Rect.X < targetRect.X).Select(w => w.Word.Text));
            var leftScore = AnchorSimilarity(leftText, proto.LeftAnchorWeights);
            var aboveScore = i > 0 ? AnchorSimilarity(lines[i - 1].Text, proto.AboveAnchorWeights) : 0.0;

            var hasAnchors = proto.LeftAnchorWeights.Count > 0 || proto.AboveAnchorWeights.Count > 0;
            var score = hasAnchors
                ? (spatial * 0.45) + (leftScore * 0.45) + (aboveScore * 0.10)
                : spatial * 0.85;

            if (score > bestScore)
            {
                bestScore = score;
                bestAmount = amount;
            }
        }

        return (bestAmount, (float)Math.Clamp(bestScore, 0.0, 0.99));
    }

    private static (ReceiptDateCandidate? Date, float Score) PredictDate(
        List<NormalizedLine> lines,
        FieldPrototype proto,
        bool? dayFirst)
    {
        ReceiptDateCandidate? best = null;
        var bestScore = 0.0;

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var parsed = ReceiptDateParser.Parse(line.Text, dayFirst: dayFirst);
            if (parsed is null)
            {
                continue;
            }

            var spatial = SpatialSimilarity(line.Rect, proto);
            var leftScore = AnchorSimilarity(line.Text, proto.LeftAnchorWeights);
            var aboveScore = i > 0 ? AnchorSimilarity(lines[i - 1].Text, proto.AboveAnchorWeights) : 0.0;
            var hasAnchors = proto.LeftAnchorWeights.Count > 0 || proto.AboveAnchorWeights.Count > 0;
            var score = hasAnchors
                ? (spatial * 0.45) + (leftScore * 0.40) + (aboveScore * 0.15)
                : spatial * 0.85;

            if (score > bestScore)
            {
                bestScore = score;
                best = parsed;
            }
        }

        return (best, (float)Math.Clamp(bestScore, 0.0, 0.99));
    }

    private static double SpatialSimilarity(NormalizedRect candidate, FieldPrototype proto)
    {
        var dx = (candidate.CenterX - proto.MeanX) / Math.Max(proto.StdX * 1.8, 0.14);
        var dy = (candidate.CenterY - proto.MeanY) / Math.Max(proto.StdY * 1.8, 0.09);
        return Math.Exp(-0.5 * ((dx * dx) + (dy * dy)));
    }

    private static double AnchorSimilarity(string? text, Dictionary<string, int> weights)
    {
        if (string.IsNullOrWhiteSpace(text) || weights.Count == 0)
        {
            return 0.0;
        }

        var tokens = TokenizeSignificant(text);
        if (tokens.Count == 0)
        {
            return 0.0;
        }

        var maxWeight = weights.Values.Max();
        var matchedWeight = 0;
        foreach (var token in tokens.Distinct(StringComparer.Ordinal))
        {
            if (weights.TryGetValue(token, out var w))
            {
                matchedWeight += w;
            }
        }

        return Math.Min(1.0, matchedWeight / (double)Math.Max(1, maxWeight));
    }

    private static List<string> TokenizeSignificant(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var folded = FoldText(text);
        var tokens = new List<string>();
        var current = new StringBuilder();
        foreach (var ch in folded)
        {
            if (char.IsLetter(ch))
            {
                current.Append(ch);
            }
            else if (current.Length > 0)
            {
                if (current.Length >= 2)
                {
                    tokens.Add(current.ToString());
                }
                current.Clear();
            }
        }

        if (current.Length >= 2)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }

    private static string FoldText(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var normalized = input.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(char.ToUpperInvariant(ch));
            }
        }

        return sb.ToString().Trim();
    }

    private static List<NormalizedLine> BuildNormalizedLines(IReadOnlyList<OcrWord> ocrWords, double w, double h)
    {
        var items = ocrWords
            .Where(word => !string.IsNullOrWhiteSpace(word.Text))
            .Select(word => (
                Word: word,
                Rect: NormalizedRect.CreateClamped(
                    word.BoundingBox.Left / w,
                    word.BoundingBox.Top / h,
                    word.BoundingBox.Right / w,
                    word.BoundingBox.Bottom / h)))
            .OrderBy(x => x.Rect.Y)
            .ThenBy(x => x.Rect.X)
            .ToList();

        if (items.Count == 0)
        {
            return [];
        }

        var medianH = items.Select(x => x.Rect.Height).OrderBy(v => v).ElementAt(items.Count / 2);
        var yTol = Math.Max(medianH * 0.65, 0.012);

        var lines = new List<NormalizedLine>();
        var current = new List<(OcrWord Word, NormalizedRect Rect)> { items[0] };
        var currentY = items[0].Rect.CenterY;

        for (var i = 1; i < items.Count; i++)
        {
            if (Math.Abs(items[i].Rect.CenterY - currentY) <= yTol)
            {
                current.Add(items[i]);
            }
            else
            {
                lines.Add(CreateLine(current));
                current = [items[i]];
                currentY = items[i].Rect.CenterY;
            }
        }

        lines.Add(CreateLine(current));
        return lines;
    }

    private static NormalizedLine CreateLine(List<(OcrWord Word, NormalizedRect Rect)> words)
    {
        var ordered = words.OrderBy(w => w.Rect.X).ToList();
        var text = string.Join(" ", ordered.Select(w => w.Word.Text.Trim()));
        var rect = NormalizedRect.CreateClamped(
            ordered.Min(w => w.Rect.X),
            ordered.Min(w => w.Rect.Y),
            ordered.Max(w => w.Rect.Right),
            ordered.Max(w => w.Rect.Bottom));
        return new NormalizedLine(text, rect, ordered);
    }

    private PersistedReceiptModel EnsureLoadedForCurrentVault()
    {
        lock (_stateGate)
        {
            EnsureLoadedLocked();
            return _model;
        }
    }

    private void EnsureLoadedLocked()
    {
        var currentVault = _vault.IsUnlocked ? _vault.DbPath : null;
        if (string.Equals(_loadedVaultPath, currentVault, StringComparison.Ordinal))
        {
            return;
        }

        _loadedVaultPath = currentVault;
        _model = new PersistedReceiptModel();

        if (string.IsNullOrEmpty(currentVault))
        {
            return;
        }

        var path = Path.Combine(currentVault, ModelDirectoryName, ModelFileName);
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            var sealedBlob = File.ReadAllBytes(path);
            var plain = _cipher.Decrypt(sealedBlob, VaultFilePurposes.ReceiptLayoutModel);
            var parsed = JsonSerializer.Deserialize<PersistedReceiptModel>(plain, JsonOptions);
            if (parsed is not null)
            {
                _model = parsed;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or VaultFileCipherException or JsonException)
        {
            if (_logger is not null)
            {
                LogLoadFailed(_logger, ex);
            }
            _model = new PersistedReceiptModel();
        }
    }

    private void PersistEncrypted(string vaultDirectory, PersistedReceiptModel model)
    {
        var path = Path.Combine(vaultDirectory, ModelDirectoryName, ModelFileName);
        var plain = JsonSerializer.SerializeToUtf8Bytes(model, JsonOptions);
        byte[] sealedBlob;
        try
        {
            sealedBlob = _cipher.Encrypt(plain, VaultFilePurposes.ReceiptLayoutModel);
        }
        finally
        {
            Array.Clear(plain);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(sealedBlob, 0, sealedBlob.Length);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            if (File.Exists(temp))
            {
                try { File.Delete(temp); } catch { /* best effort */ }
            }
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _disposeCts.Cancel();
        _disposeCts.Dispose();
        _trainLock.Dispose();
    }

    private sealed record NormalizedLine(
        string Text,
        NormalizedRect Rect,
        List<(OcrWord Word, NormalizedRect Rect)> Words);

    private sealed record FieldPrototype(
        double MeanX,
        double MeanY,
        double MeanW,
        double MeanH,
        double StdX,
        double StdY,
        int SampleCount,
        Dictionary<string, int> LeftAnchorWeights,
        Dictionary<string, int> AboveAnchorWeights);

    private sealed record MerchantLayoutTemplate(
        string MerchantKey,
        List<string> Tokens,
        Dictionary<string, FieldPrototype> Fields,
        int SampleCount);

    private sealed record PersistedReceiptModel
    {
        public int Version { get; init; } = 1;
        public DateTimeOffset? LastTrainedAtUtc { get; init; }
        public List<ReceiptLayoutAnnotation> Annotations { get; init; } = new();
        public Dictionary<string, FieldPrototype> GlobalPrototypes { get; init; } = new(StringComparer.Ordinal);
        public Dictionary<string, MerchantLayoutTemplate> MerchantTemplates { get; init; } = new(StringComparer.Ordinal);
    }

    [LoggerMessage(
        EventId = 1,
        EventName = "ai.receipt_learner.load_failed",
        Level = LogLevel.Warning,
        Message = "Could not load stored local receipt layout model; starting fresh.")]
    private static partial void LogLoadFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 2,
        EventName = "ai.receipt_learner.train_failed",
        Level = LogLevel.Warning,
        Message = "Background local receipt layout model training failed.")]
    private static partial void LogTrainingFailed(ILogger logger, Exception exception);
}
