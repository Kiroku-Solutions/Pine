using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Pine.Core.Ai;

namespace Pine.Infrastructure.Ai.Extraction.LayoutLm;

/// <summary>What the model read for the four fields, before it is mapped onto a receipt.</summary>
/// <param name="Spans">The best span of each field the model found.</param>
public sealed record LayoutLmPrediction(IReadOnlyDictionary<LayoutLmField, LayoutLmSpan> Spans);

/// <summary>
/// Runs a LayoutLMv3 token-classification model (ONNX Runtime, CPU, offline) over OCR lines and the page
/// image and returns Merchant, Date, Total and Tax.
/// </summary>
/// <remarks>
/// <para>
/// THIS TYPE REALLY RUNS THE MODEL, and a test proves it (TD-36 deleted an extractor that loaded a model
/// nothing used). <see cref="InferenceCount"/> counts <c>session.Run</c> calls; the integration test
/// asserts it moves and that the logits differ with the input.
/// </para>
/// <para>
/// MODEL CONTRACT (interchangeable: same I/O, configurable path). Inputs <c>input_ids</c> int64 [B,S],
/// <c>bbox</c> int64 [B,S,4] in 0..1000, <c>attention_mask</c> int64 [B,S], <c>pixel_values</c> float32
/// [B,3,224,224]; output float32 [B,S,9] logits. Labels: 0 O, 1/2 B/I-MERCHANT, 3/4 B/I-DATE,
/// 5/6 B/I-TOTAL, 7/8 B/I-TAX. Long pages are split into 512-token windows with stride 128; a word is
/// read from the first sub-token of the window in which it has the most context.
/// </para>
/// <para>
/// THE SESSION IS LOADED ON FIRST USE, not at construction, so registering the extractor costs nothing and
/// a missing or broken model surfaces as an exception from <see cref="ExtractFields"/> (which the caller
/// turns into a heuristic fallback) rather than from the container. Log records carry sizes and counts,
/// never receipt text.
/// </para>
/// <para>
/// TUNED-CONSTANTS BOUNDARY (TD-118). Thread counts and window sizes are the algorithm for this model, not
/// options. See <c>docs/13-AI-Architecture.md</c> section 7.1.
/// </para>
/// </remarks>
public sealed partial class LayoutLmFieldExtractor : IVisualFieldExtractor, IDisposable
{
    private const int MaxIntraOpThreads = 4;

    private static readonly string[] InputNames = ["input_ids", "bbox", "attention_mask", "pixel_values"];

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex MultipleSpaces();

    [GeneratedRegex(@"\d[\d.,]*")]
    private static partial Regex NumberRegex();

    private static readonly Action<ILogger, int, int, int, Exception?> Read = LoggerMessage.Define<int, int, int>(
        LogLevel.Debug,
        new EventId(4102, nameof(Read)),
        "LayoutLM read {Windows} window(s) over {Words} line(s); fields found: {Found}.");

    private static readonly Action<ILogger, long, Exception?> Loaded = LoggerMessage.Define<long>(
        LogLevel.Information,
        new EventId(4103, nameof(Loaded)),
        "LayoutLM model loaded ({Megabytes} MB).");

    private readonly string _modelPath;
    private readonly string _tokenizerPath;
    private readonly ILogger? _logger;
    private readonly object _loadGate = new();
    private InferenceSession? _session;
    private LayoutLmTokenizer? _tokenizer;
    private string _outputName = "logits";
    private long _inferenceCount;
    private bool _disposed;

    /// <summary>Creates the extractor; nothing is read from disk until the first extraction.</summary>
    public LayoutLmFieldExtractor(LayoutLmOptions options, ILogger<LayoutLmFieldExtractor>? logger = null)
        : this(options?.ModelPath ?? throw new ArgumentNullException(nameof(options)),
               options.ResolveTokenizerPath() ?? throw new ArgumentException("A model path is required.", nameof(options)),
               logger)
    {
    }

    /// <summary>Creates the extractor over explicit model and tokenizer files.</summary>
    public LayoutLmFieldExtractor(string modelPath, string tokenizerPath, ILogger? logger = null)
    {
        _modelPath = modelPath ?? throw new ArgumentNullException(nameof(modelPath));
        _tokenizerPath = tokenizerPath ?? throw new ArgumentNullException(nameof(tokenizerPath));
        _logger = logger;
    }

    /// <summary>Number of times the model graph has been executed (one per window).</summary>
    public long InferenceCount => Interlocked.Read(ref _inferenceCount);

    /// <inheritdoc />
    public ReceiptExtractionResult ExtractFields(IReadOnlyList<OcrWord> ocrWords, ReadOnlyMemory<byte> encodedImage)
    {
        var prediction = Predict(ocrWords, encodedImage);
        return ToReceipt(prediction);
    }

    /// <summary>Runs the model and decodes the four fields.</summary>
    public LayoutLmPrediction Predict(IReadOnlyList<OcrWord> ocrWords, ReadOnlyMemory<byte> encodedImage)
    {
        ArgumentNullException.ThrowIfNull(ocrWords);
        var (session, tokenizer) = EnsureLoaded();

        var image = LayoutLmImagePreprocessor.Process(encodedImage.Span);
        var chunks = LayoutLmEncoder.Encode(ocrWords, image.Width, image.Height, tokenizer, out var kept);
        if (chunks.Count == 0)
        {
            return new LayoutLmPrediction(new Dictionary<LayoutLmField, LayoutLmSpan>());
        }

        var wordProbabilities = new float[kept.Count][];
        var wordContext = new int[kept.Count];
        foreach (var chunk in chunks)
        {
            var probabilities = RunChunk(session, chunk, image.PixelValues);
            for (var t = 0; t < chunk.InputIds.Length; t++)
            {
                var word = chunk.WordIds[t];
                if (word < 0 || !chunk.IsFirstSubtoken[t])
                {
                    continue;
                }

                // Distance to the nearest window edge: more context on both sides gives the better read.
                var context = Math.Min(t, chunk.InputIds.Length - 1 - t);
                if (wordProbabilities[word] is null || context > wordContext[word])
                {
                    wordProbabilities[word] = probabilities[t];
                    wordContext[word] = context;
                }
            }
        }

        var texts = kept.Select(i => ocrWords[i].Text!.Trim()).ToList();
        var spans = LayoutLmDecoder.Decode(texts, wordProbabilities);
        if (_logger is not null)
        {
            Read(_logger, chunks.Count, kept.Count, spans.Count, null);
        }

        return new LayoutLmPrediction(spans);
    }

    /// <summary>Maps a prediction onto the receipt result: parsed amounts and date, per-field confidence.</summary>
    public static ReceiptExtractionResult ToReceipt(LayoutLmPrediction prediction)
    {
        ArgumentNullException.ThrowIfNull(prediction);
        var spans = prediction.Spans;

        string? merchant = null;
        var merchantConfidence = FieldConfidence.NotFound;
        if (spans.TryGetValue(LayoutLmField.Merchant, out var m))
        {
            var cleaned = MultipleSpaces().Replace(m.Text, " ").Trim();
            if (cleaned.Length > 0)
            {
                merchant = cleaned;
                merchantConfidence = Confidence(m.Probability);
            }
        }

        decimal? total = null;
        var totalConfidence = FieldConfidence.NotFound;
        if (spans.TryGetValue(LayoutLmField.Total, out var t) && TryParseAmount(t.Text, out var totalValue))
        {
            total = totalValue;
            totalConfidence = Confidence(t.Probability);
        }

        decimal? tax = null;
        if (spans.TryGetValue(LayoutLmField.Tax, out var x) && TryParseAmount(x.Text, out var taxValue))
        {
            tax = taxValue;
        }

        ReceiptDateCandidate? date = null;
        var dateConfidence = FieldConfidence.NotFound;
        if (spans.TryGetValue(LayoutLmField.Date, out var d))
        {
            date = ReceiptDateParser.Parse(d.Text);
            if (date is not null)
            {
                dateConfidence = Confidence(d.Probability);
            }
        }

        return new ReceiptExtractionResult(
            Merchant: merchant,
            TotalAmount: total,
            CurrencyCode: null,
            Date: date?.Value,
            Confidence: new ReceiptConfidence(merchantConfidence, totalConfidence, dateConfidence, Array.Empty<FieldConfidence>()),
            LineItems: null,
            DateIsAmbiguous: date?.IsAmbiguous ?? false,
            AlternativeDate: date?.Alternative,
            TaxAmount: tax);
    }

    /// <summary>Parses a printed amount ("$12.50", "1,591,600", "60.000") with the same separator rules the heuristics use.</summary>
    public static bool TryParseAmount(string text, out decimal amount)
    {
        amount = 0m;
        var match = NumberRegex().Match(text ?? string.Empty);
        if (!match.Success)
        {
            return false;
        }

        var number = match.Value.TrimEnd('.', ',');
        if (number.Length == 0)
        {
            return false;
        }

        // "1,591,600" / "1.591.600": one repeated separator with 3-digit groups is thousands, not a decimal point.
        // The heuristics' normaliser reads the last separator as the decimal one, which is right for "1.234,56" and wrong here.
        var normalised = IsPureThousands(number)
            ? number.Replace(".", string.Empty).Replace(",", string.Empty)
            : HeuristicFieldExtractor.NormalizeDecimalString(number);

        if (!decimal.TryParse(normalised, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) || value <= 0m)
        {
            return false;
        }

        amount = value;
        return true;
    }

    private static bool IsPureThousands(string number)
    {
        var separator = number.Contains(',', StringComparison.Ordinal) ? ',' : '.';
        if (number.Contains(',', StringComparison.Ordinal) && number.Contains('.', StringComparison.Ordinal))
        {
            return false;
        }

        var groups = number.Split(separator);
        return groups.Length > 2 && groups[0].Length is >= 1 and <= 3 && groups.Skip(1).All(g => g.Length == 3);
    }

    private static FieldConfidence Confidence(double probability) =>
        new(probability, FieldConfidence.BandFor(probability));

    private float[][] RunChunk(InferenceSession session, LayoutLmChunk chunk, float[] pixelValues)
    {
        var length = chunk.InputIds.Length;
        var ids = new DenseTensor<long>([1, length]);
        var mask = new DenseTensor<long>([1, length]);
        var boxes = new DenseTensor<long>([1, length, 4]);
        for (var i = 0; i < length; i++)
        {
            ids[0, i] = chunk.InputIds[i];
            mask[0, i] = 1;
            for (var c = 0; c < 4; c++)
            {
                boxes[0, i, c] = chunk.Boxes[i][c];
            }
        }

        var pixels = new DenseTensor<float>(pixelValues, [1, 3, LayoutLmImagePreprocessor.Size, LayoutLmImagePreprocessor.Size]);
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(InputNames[0], ids),
            NamedOnnxValue.CreateFromTensor(InputNames[1], boxes),
            NamedOnnxValue.CreateFromTensor(InputNames[2], mask),
            NamedOnnxValue.CreateFromTensor(InputNames[3], pixels),
        };

        using var results = session.Run(inputs);
        Interlocked.Increment(ref _inferenceCount);
        var logits = results.First(r => r.Name == _outputName).AsTensor<float>();
        if (logits.Dimensions.Length != 3 || logits.Dimensions[1] != length || logits.Dimensions[2] != LayoutLmDecoder.LabelCount)
        {
            throw new InvalidOperationException("The model output does not have the expected [1, tokens, 9] shape.");
        }

        var flat = logits.ToArray();
        var probabilities = new float[length][];
        for (var t = 0; t < length; t++)
        {
            probabilities[t] = LayoutLmDecoder.Softmax(flat.AsSpan(t * LayoutLmDecoder.LabelCount, LayoutLmDecoder.LabelCount));
        }

        return probabilities;
    }

    /// <summary>The softmax probabilities of the first window (the model runs for real), for diagnostics and tests; [tokens, 9] flattened.</summary>
    internal float[] RawLogitsOfFirstWindow(IReadOnlyList<OcrWord> ocrWords, ReadOnlyMemory<byte> encodedImage)
    {
        var (session, tokenizer) = EnsureLoaded();
        var image = LayoutLmImagePreprocessor.Process(encodedImage.Span);
        var chunks = LayoutLmEncoder.Encode(ocrWords, image.Width, image.Height, tokenizer, out _);
        var probabilities = RunChunk(session, chunks[0], image.PixelValues);
        return probabilities.SelectMany(p => p).ToArray();
    }

    private (InferenceSession Session, LayoutLmTokenizer Tokenizer) EnsureLoaded()
    {
        lock (_loadGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_session is not null && _tokenizer is not null)
            {
                return (_session, _tokenizer);
            }

            if (!File.Exists(_modelPath))
            {
                throw new FileNotFoundException("The LayoutLM model file does not exist.", _modelPath);
            }

            if (!File.Exists(_tokenizerPath))
            {
                throw new FileNotFoundException("The LayoutLM tokenizer.json does not exist.", _tokenizerPath);
            }

            var tokenizer = LayoutLmTokenizer.Load(_tokenizerPath);
            using var options = new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                IntraOpNumThreads = Math.Min(MaxIntraOpThreads, Environment.ProcessorCount),
                InterOpNumThreads = 1,
                // Sequence length changes per page; a memory pattern planned for one shape only wastes RAM on the next.
                EnableMemoryPattern = false,
            };

            var session = new InferenceSession(_modelPath, options);
            foreach (var name in InputNames)
            {
                if (!session.InputMetadata.ContainsKey(name))
                {
                    session.Dispose();
                    throw new InvalidOperationException($"The model has no input named '{name}'.");
                }
            }

            _outputName = session.OutputMetadata.ContainsKey("logits") ? "logits" : session.OutputMetadata.Keys.First();
            _tokenizer = tokenizer;
            _session = session;
            if (_logger is not null)
            {
                Loaded(_logger, new FileInfo(_modelPath).Length / (1024 * 1024), null);
            }

            return (session, tokenizer);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_loadGate)
        {
            _disposed = true;
            _session?.Dispose();
            _session = null;
        }
    }
}
