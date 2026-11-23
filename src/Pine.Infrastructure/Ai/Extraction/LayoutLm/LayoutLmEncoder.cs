using Pine.Core.Ai;

namespace Pine.Infrastructure.Ai.Extraction.LayoutLm;

/// <summary>One 512-token window of a document, ready to feed the model.</summary>
/// <param name="InputIds">Token ids including <c>&lt;s&gt;</c> and <c>&lt;/s&gt;</c>.</param>
/// <param name="Boxes">One 0..1000 box (x0, y0, x1, y1) per token.</param>
/// <param name="WordIds">The index of the word each token belongs to; -1 for the special tokens.</param>
/// <param name="IsFirstSubtoken">True where the token is the first sub-token of its word, the one the model was trained to label.</param>
public sealed record LayoutLmChunk(int[] InputIds, int[][] Boxes, int[] WordIds, bool[] IsFirstSubtoken);

/// <summary>
/// Turns OCR words and boxes into model windows exactly as the training script did
/// (<c>layout_utils.build_example</c> + the HF processor with <c>truncation, max_length=512, stride=128,
/// return_overflowing_tokens</c>).
/// </summary>
/// <remarks>
/// TUNED-CONSTANTS BOUNDARY (TD-118). The window, stride and box scale are properties of the
/// trained model and are literals here on purpose. See <c>docs/13-AI-Architecture.md</c> section 7.1.
/// </remarks>
public static class LayoutLmEncoder
{
    /// <summary>Tokens per window including the two special tokens.</summary>
    public const int MaxLength = 512;

    /// <summary>Tokens shared by consecutive windows.</summary>
    public const int Stride = 128;

    /// <summary>
    /// Cap on windows per document (about 3,000 tokens). A receipt is a few hundred; the cap only exists so a
    /// pathological page cannot turn one scan into an unbounded number of inferences.
    /// </summary>
    public const int MaxChunks = 8;

    private const int BoxScale = 1000;
    private const int ContentLength = MaxLength - 2;

    /// <summary>
    /// Pixel box to 0..1000 integers, clamped and ordered; <c>layout_utils.normalize_box</c>, including
    /// Python's round-half-to-even.
    /// </summary>
    public static int[] NormalizeBox(float x0, float y0, float x1, float y1, int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "width/height must be positive");
        }

        static int Scale(double v, int d) =>
            Math.Clamp((int)Math.Round(BoxScale * v / d, MidpointRounding.ToEven), 0, BoxScale);

        var nx0 = Scale(x0, width);
        var nx1 = Scale(x1, width);
        var ny0 = Scale(y0, height);
        var ny1 = Scale(y1, height);
        return [Math.Min(nx0, nx1), Math.Min(ny0, ny1), Math.Max(nx0, nx1), Math.Max(ny0, ny1)];
    }

    /// <summary>
    /// Encodes the words. Words whose trimmed text is empty are dropped, as in training; the returned
    /// <paramref name="keptWordIndexes"/> maps each kept word (the indexes used in <see cref="LayoutLmChunk.WordIds"/>)
    /// back to its position in <paramref name="words"/>.
    /// </summary>
    public static IReadOnlyList<LayoutLmChunk> Encode(
        IReadOnlyList<OcrWord> words,
        int imageWidth,
        int imageHeight,
        LayoutLmTokenizer tokenizer,
        out IReadOnlyList<int> keptWordIndexes)
    {
        ArgumentNullException.ThrowIfNull(words);
        ArgumentNullException.ThrowIfNull(tokenizer);

        var kept = new List<int>();
        var ids = new List<int>();
        var boxes = new List<int[]>();
        var wordIds = new List<int>();
        var first = new List<bool>();

        for (var i = 0; i < words.Count; i++)
        {
            var text = words[i].Text?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            var pieces = tokenizer.EncodeWord(text);
            if (pieces.Length == 0)
            {
                continue;
            }

            var b = words[i].BoundingBox;
            var box = NormalizeBox(b.X, b.Y, b.X + b.Width, b.Y + b.Height, imageWidth, imageHeight);
            var keptIndex = kept.Count;
            kept.Add(i);
            for (var p = 0; p < pieces.Length; p++)
            {
                ids.Add(pieces[p]);
                boxes.Add(box);
                wordIds.Add(keptIndex);
                first.Add(p == 0);
            }
        }

        keptWordIndexes = kept;
        var chunks = new List<LayoutLmChunk>();
        if (ids.Count == 0)
        {
            return chunks;
        }

        var step = ContentLength - Stride;
        for (var start = 0; chunks.Count < MaxChunks; start += step)
        {
            var end = Math.Min(start + ContentLength, ids.Count);
            chunks.Add(BuildChunk(ids, boxes, wordIds, first, start, end));
            if (end >= ids.Count)
            {
                break;
            }
        }

        return chunks;
    }

    private static LayoutLmChunk BuildChunk(List<int> ids, List<int[]> boxes, List<int> wordIds, List<bool> first, int start, int end)
    {
        var length = end - start + 2;
        var chunkIds = new int[length];
        var chunkBoxes = new int[length][];
        var chunkWords = new int[length];
        var chunkFirst = new bool[length];

        // The tokenizer config of the training run sets cls/sep/pad boxes to [0,0,0,0].
        chunkIds[0] = LayoutLmTokenizer.BosId;
        chunkBoxes[0] = [0, 0, 0, 0];
        chunkWords[0] = -1;
        for (var i = start; i < end; i++)
        {
            var o = i - start + 1;
            chunkIds[o] = ids[i];
            chunkBoxes[o] = boxes[i];
            chunkWords[o] = wordIds[i];
            chunkFirst[o] = first[i];
        }

        chunkIds[length - 1] = LayoutLmTokenizer.EosId;
        chunkBoxes[length - 1] = [0, 0, 0, 0];
        chunkWords[length - 1] = -1;
        return new LayoutLmChunk(chunkIds, chunkBoxes, chunkWords, chunkFirst);
    }
}
