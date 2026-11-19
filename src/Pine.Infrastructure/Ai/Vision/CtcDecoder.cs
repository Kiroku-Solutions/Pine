using System;
using System.Collections.Generic;
using System.Text;

namespace Pine.Infrastructure.Ai.Vision;

/// <summary>
/// CTC greedy decoding of the recognition model's output plus the character dictionary. A port of
/// <c>ctc_decode</c> / <c>load_charset</c> in <c>research/scripts/onnx_ocr_reference.py</c> (docs/65 section 4).
/// </summary>
/// <remarks>
/// Pure managed code, no ONNX runtime. The model's output is already a probability distribution per time
/// step (softmax is inside the graph), class 0 is the CTC blank, and the line confidence is the mean of the
/// kept steps' maximum probabilities.
/// </remarks>
internal static class CtcDecoder
{
    /// <summary>The runtime charset: <c>["blank"] + dictionary + [" "]</c> (PaddleOCR's <c>use_space_char</c>).</summary>
    /// <param name="dictionaryLines">One character per line, as shipped in <c>dict_latin_v5.txt</c>.</param>
    /// <returns>The charset indexed by class id.</returns>
    internal static IReadOnlyList<string> BuildCharset(IEnumerable<string> dictionaryLines)
    {
        ArgumentNullException.ThrowIfNull(dictionaryLines);

        var charset = new List<string> { "blank" };
        foreach (var line in dictionaryLines)
        {
            // Only a line terminator is stripped: a dictionary entry may legitimately be a space.
            charset.Add(line.TrimEnd('\r', '\n'));
        }

        charset.Add(" ");
        return charset;
    }

    /// <summary>Reads a dictionary file (UTF-8, one character per line) into a charset.</summary>
    /// <param name="path">Path of the dictionary file.</param>
    /// <returns>The charset indexed by class id.</returns>
    internal static IReadOnlyList<string> LoadCharset(string path)
    {
        var text = System.IO.File.ReadAllText(path, Encoding.UTF8);
        var lines = new List<string>(text.Split('\n'));
        if (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return BuildCharset(lines);
    }

    /// <summary>Decodes one line.</summary>
    /// <param name="probs">Row-major <c>steps x classes</c> probabilities.</param>
    /// <param name="steps">Number of time steps.</param>
    /// <param name="classes">Number of classes per step.</param>
    /// <param name="charset">The charset from <see cref="BuildCharset"/>.</param>
    /// <returns>The text and its confidence (0 for an empty line).</returns>
    internal static (string Text, float Confidence) Decode(ReadOnlySpan<float> probs, int steps, int classes, IReadOnlyList<string> charset)
    {
        ArgumentNullException.ThrowIfNull(charset);
        if (probs.Length < steps * classes)
        {
            throw new ArgumentException("The probability matrix is smaller than steps x classes.", nameof(probs));
        }

        var text = new StringBuilder();
        double sum = 0;
        var kept = 0;
        var previous = -1;
        for (var t = 0; t < steps; t++)
        {
            var row = probs.Slice(t * classes, classes);
            var best = 0;
            var max = row[0];
            for (var k = 1; k < row.Length; k++)
            {
                if (row[k] > max)
                {
                    max = row[k];
                    best = k;
                }
            }

            if (best != 0 && best != previous)
            {
                if (best < charset.Count)
                {
                    text.Append(charset[best]);
                }

                sum += max;
                kept++;
            }

            previous = best;
        }

        return (text.ToString(), kept == 0 ? 0f : (float)(sum / kept));
    }
}
