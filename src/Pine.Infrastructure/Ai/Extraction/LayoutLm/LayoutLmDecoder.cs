namespace Pine.Infrastructure.Ai.Extraction.LayoutLm;

/// <summary>The four fields the model labels.</summary>
public enum LayoutLmField
{
    /// <summary>The business name.</summary>
    Merchant,

    /// <summary>The document date.</summary>
    Date,

    /// <summary>The amount to pay.</summary>
    Total,

    /// <summary>The tax amount.</summary>
    Tax,
}

/// <summary>A run of consecutive words the model labelled as one field.</summary>
/// <param name="Field">Which field.</param>
/// <param name="Text">The words joined by single spaces.</param>
/// <param name="Probability">Mean softmax probability of the predicted label over the span's words.</param>
/// <param name="FirstWord">Index of the first word (in the kept-word numbering).</param>
/// <param name="WordCount">Number of words in the span.</param>
public sealed record LayoutLmSpan(LayoutLmField Field, string Text, double Probability, int FirstWord, int WordCount);

/// <summary>Softmax, BIO decoding and span selection, with no dependency on the runtime.</summary>
/// <remarks>
/// Label order is the one in the trained checkpoint's <c>id2label.json</c>:
/// 0 O, 1 B-MERCHANT, 2 I-MERCHANT, 3 B-DATE, 4 I-DATE, 5 B-TOTAL, 6 I-TOTAL, 7 B-TAX, 8 I-TAX.
/// </remarks>
public static class LayoutLmDecoder
{
    /// <summary>Number of labels the model outputs.</summary>
    public const int LabelCount = 9;

    /// <summary>Numerically stable softmax of one logit vector.</summary>
    public static float[] Softmax(ReadOnlySpan<float> logits)
    {
        var max = float.NegativeInfinity;
        foreach (var l in logits)
        {
            max = Math.Max(max, l);
        }

        var result = new float[logits.Length];
        double sum = 0;
        for (var i = 0; i < logits.Length; i++)
        {
            var e = Math.Exp(logits[i] - max);
            result[i] = (float)e;
            sum += e;
        }

        for (var i = 0; i < result.Length; i++)
        {
            result[i] = (float)(result[i] / sum);
        }

        return result;
    }

    /// <summary>
    /// Decodes per-word label probabilities into the best span of each field.
    /// </summary>
    /// <param name="words">The words, in reading order.</param>
    /// <param name="probabilities">
    /// One probability vector (<see cref="LabelCount"/> long) per word, or <c>null</c> for a word the model did not
    /// score. A <c>null</c> ends any open span.
    /// </param>
    /// <returns>For each field found, the span with the highest mean probability (the earliest on ties).</returns>
    public static IReadOnlyDictionary<LayoutLmField, LayoutLmSpan> Decode(
        IReadOnlyList<string> words,
        IReadOnlyList<float[]?> probabilities)
    {
        ArgumentNullException.ThrowIfNull(words);
        ArgumentNullException.ThrowIfNull(probabilities);
        if (words.Count != probabilities.Count)
        {
            throw new ArgumentException("One probability vector per word is required.", nameof(probabilities));
        }

        var best = new Dictionary<LayoutLmField, LayoutLmSpan>();
        LayoutLmField? openField = null;
        var openStart = 0;
        var openSum = 0.0;
        var openCount = 0;

        void Close()
        {
            if (openField is { } field && openCount > 0)
            {
                var text = string.Join(' ', Enumerable.Range(openStart, openCount).Select(i => words[i].Trim()));
                var span = new LayoutLmSpan(field, text, openSum / openCount, openStart, openCount);
                if (!best.TryGetValue(field, out var current) || span.Probability > current.Probability)
                {
                    best[field] = span;
                }
            }

            openField = null;
            openSum = 0;
            openCount = 0;
        }

        for (var i = 0; i < words.Count; i++)
        {
            var p = probabilities[i];
            if (p is null)
            {
                Close();
                continue;
            }

            if (p.Length != LabelCount)
            {
                throw new ArgumentException($"Expected {LabelCount} probabilities per word.", nameof(probabilities));
            }

            var label = ArgMax(p);
            if (label == 0)
            {
                Close();
                continue;
            }

            var field = (LayoutLmField)((label - 1) / 2);
            var isBegin = label % 2 == 1;
            if (isBegin || openField != field)
            {
                // An I- tag with no open span of its field starts one: the model is tolerated, not trusted, to emit B- first.
                Close();
                openField = field;
                openStart = i;
            }

            openSum += p[label];
            openCount++;
        }

        Close();
        return best;
    }

    private static int ArgMax(float[] values)
    {
        var index = 0;
        for (var i = 1; i < values.Length; i++)
        {
            if (values[i] > values[index])
            {
                index = i;
            }
        }

        return index;
    }
}
