using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Pine.Core.Ai;

/// <summary>
/// Extracts OCR text and surrounding anchor words for a user-drawn normalized bounding box on a receipt.
/// Pure BCL so both the interactive tagging UI and the local receipt trainer share one implementation.
/// </summary>
public static partial class ReceiptBoxExtractor
{
    [GeneratedRegex(@"(?<![\d.,])(\d{1,3}(?:[.,]\d{3})*[.,]\d{2}|\d+(?:[.,]\d{1,2})?)(?![\d])", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex AmountTokenRegex();

    /// <summary>
    /// Computes the OCR text inside <paramref name="box"/> plus the nearest left and above anchor words
    /// (e.g. "TOTAL A PAGAR", "IVA 15%", "FECHA EMISION") that help the local model generalise.
    /// </summary>
    public static ReceiptFieldBox Extract(
        ReceiptTagField field,
        NormalizedRect box,
        IReadOnlyList<OcrWord>? words,
        int imageWidth,
        int imageHeight,
        string? customTagName = null)
    {
        var boxId = Guid.NewGuid();
        var cleanCustom = field == ReceiptTagField.Custom && !string.IsNullOrWhiteSpace(customTagName)
            ? customTagName.Trim()
            : null;

        if (words is null || words.Count == 0)
        {
            return new ReceiptFieldBox(field, box, string.Empty, CustomTagName: cleanCustom, BoxId: boxId);
        }

        var (w, h) = ResolveDimensions(words, imageWidth, imageHeight);
        if (w <= 0 || h <= 0)
        {
            return new ReceiptFieldBox(field, box, string.Empty, CustomTagName: cleanCustom, BoxId: boxId);
        }

        var normalizedWords = words
            .Where(word => !string.IsNullOrWhiteSpace(word.Text))
            .Select(word =>
            {
                var b = word.BoundingBox;
                var rect = NormalizedRect.CreateClamped(b.Left / w, b.Top / h, b.Right / w, b.Bottom / h);
                return (Word: word, Rect: rect);
            })
            .ToList();

        if (normalizedWords.Count == 0)
        {
            return new ReceiptFieldBox(field, box, string.Empty, CustomTagName: cleanCustom, BoxId: boxId);
        }

        var effectiveBox = box;
        // If the user tapped (very tiny box), expand slightly to catch the word under the tap.
        var isTap = box.Width < 0.015 && box.Height < 0.015;
        List<(OcrWord Word, NormalizedRect Rect)> inside;
        if (isTap)
        {
            var hit = normalizedWords
                .OrderBy(item => DistanceToRect(box.CenterX, box.CenterY, item.Rect))
                .FirstOrDefault();

            inside = DistanceToRect(box.CenterX, box.CenterY, hit.Rect) <= 0.04
                ? [hit]
                : [];
            if (inside.Count == 1)
            {
                effectiveBox = hit.Rect;
            }
        }
        else
        {
            inside = normalizedWords
                .Where(item => OverlapFraction(item.Rect, box) >= 0.25 || ContainsCenter(box, item.Rect))
                .ToList();
        }

        // Find left anchor tokens on the same horizontal band immediately to the left of the box.
        var rowTolerance = Math.Max(effectiveBox.Height * 0.65, 0.025);
        var leftAnchors = normalizedWords
            .Where(item => !inside.Contains(item))
            .Where(item => Math.Abs(item.Rect.CenterY - effectiveBox.CenterY) <= rowTolerance)
            .Where(item => item.Rect.Right <= effectiveBox.X + 0.02 && effectiveBox.X - item.Rect.Right <= 0.55)
            .OrderBy(item => item.Rect.X)
            .TakeLast(4)
            .Select(item => item.Word.Text.Trim())
            .ToList();

        // Find above anchor tokens immediately above the box.
        var aboveAnchors = normalizedWords
            .Where(item => !inside.Contains(item))
            .Where(item => item.Rect.Bottom <= effectiveBox.Y + 0.01 && effectiveBox.Y - item.Rect.Bottom <= 0.09)
            .Where(item => item.Rect.Right >= effectiveBox.X - 0.10 && item.Rect.X <= effectiveBox.Right + 0.10)
            .OrderBy(item => item.Rect.Y)
            .ThenBy(item => item.Rect.X)
            .TakeLast(4)
            .Select(item => item.Word.Text.Trim())
            .ToList();

        var leftAnchorText = leftAnchors.Count > 0 ? string.Join(" ", leftAnchors) : null;
        var aboveAnchorText = aboveAnchors.Count > 0 ? string.Join(" ", aboveAnchors) : null;

        if (inside.Count == 0)
        {
            return new ReceiptFieldBox(field, effectiveBox, string.Empty, leftAnchorText, aboveAnchorText, cleanCustom, boxId);
        }

        // Sort top-to-bottom into rows (using half median word height), then left-to-right.
        var ordered = OrderReadingOrder(inside);
        var extractedText = string.Join(" ", ordered.Select(x => x.Word.Text.Trim())).Trim();

        return new ReceiptFieldBox(field, effectiveBox, extractedText, leftAnchorText, aboveAnchorText, cleanCustom, boxId);
    }

    /// <summary>Parses a product description and optional trailing price from a tagged line-item box.</summary>
    public static (string Description, decimal Quantity, decimal UnitPrice, decimal TotalPrice) ParseLineItem(
        string? text,
        DecimalSeparator separator = DecimalSeparator.Unknown)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return (string.Empty, 1m, 0m, 0m);
        }

        var trimmed = text.Trim();
        var matches = AmountTokenRegex().Matches(trimmed);
        if (matches.Count > 0)
        {
            var last = matches[^1];
            var trailing = trimmed[(last.Index + last.Length)..].Trim();
            // Reject quantities/dosages with unit suffixes like "400mg" or "1L"; allow only empty or currency symbol after the price.
            var hasValidTrailing = trailing.Length == 0
                || trailing is "$" or "€" or "£" or "¥" or "₹";

            if (hasValidTrailing && TryParseNumberToken(last.Groups[1].Value, separator, out var price) && price > 0m)
            {
                var descPart = trimmed[..last.Index].Trim(' ', '-', ':', '$', '€', '£', '¥', '₹');
                var description = string.IsNullOrWhiteSpace(descPart) ? trimmed : descPart;
                return (description, 1m, price, price);
            }
        }

        return (trimmed, 1m, 0m, 0m);
    }

    /// <summary>Parses a monetary amount from extracted box text using the receipt's decimal convention.</summary>
    public static decimal? TryParseAmount(string? text, DecimalSeparator separator = DecimalSeparator.Unknown)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var matches = AmountTokenRegex().Matches(text);
        for (var i = matches.Count - 1; i >= 0; i--)
        {
            if (TryParseNumberToken(matches[i].Groups[1].Value, separator, out var value) && value >= 0m)
            {
                return value;
            }
        }

        return null;
    }

    public static (double Width, double Height) ResolveDimensions(
        IReadOnlyList<OcrWord> words,
        int imageWidth,
        int imageHeight)
    {
        if (imageWidth > 0 && imageHeight > 0)
        {
            return (imageWidth, imageHeight);
        }

        var maxX = words.Max(w => (double)w.BoundingBox.Right);
        var maxY = words.Max(w => (double)w.BoundingBox.Bottom);
        return (Math.Max(maxX, 1.0), Math.Max(maxY, 1.0));
    }

    private static bool TryParseNumberToken(string token, DecimalSeparator separator, out decimal result)
    {
        result = 0m;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var hasDot = token.Contains('.');
        var hasComma = token.Contains(',');
        string normalized;

        if (hasDot && hasComma)
        {
            var lastDot = token.LastIndexOf('.');
            var lastComma = token.LastIndexOf(',');
            normalized = lastComma > lastDot
                ? token.Replace(".", string.Empty, StringComparison.Ordinal).Replace(',', '.')
                : token.Replace(",", string.Empty, StringComparison.Ordinal);
        }
        else if (hasComma)
        {
            var afterComma = token.Length - token.LastIndexOf(',') - 1;
            if (separator == DecimalSeparator.Comma || afterComma is 1 or 2)
            {
                normalized = token.Replace(',', '.');
            }
            else
            {
                normalized = token.Replace(",", string.Empty, StringComparison.Ordinal);
            }
        }
        else if (hasDot)
        {
            var afterDot = token.Length - token.LastIndexOf('.') - 1;
            if (separator == DecimalSeparator.Comma && afterDot == 3)
            {
                normalized = token.Replace(".", string.Empty, StringComparison.Ordinal);
            }
            else
            {
                normalized = token;
            }
        }
        else
        {
            normalized = token;
        }

        return decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out result);
    }

    private static List<(OcrWord Word, NormalizedRect Rect)> OrderReadingOrder(List<(OcrWord Word, NormalizedRect Rect)> items)
    {
        if (items.Count <= 1)
        {
            return items;
        }

        var heights = items.Select(i => i.Rect.Height).OrderBy(h => h).ToList();
        var rowTol = Math.Max(heights[heights.Count / 2] * 0.6, 0.012);

        var sortedByY = items.OrderBy(i => i.Rect.Y).ThenBy(i => i.Rect.X).ToList();
        var rows = new List<List<(OcrWord Word, NormalizedRect Rect)>>();
        var current = new List<(OcrWord Word, NormalizedRect Rect)> { sortedByY[0] };
        var currentY = sortedByY[0].Rect.CenterY;

        for (var i = 1; i < sortedByY.Count; i++)
        {
            if (Math.Abs(sortedByY[i].Rect.CenterY - currentY) <= rowTol)
            {
                current.Add(sortedByY[i]);
            }
            else
            {
                rows.Add(current.OrderBy(x => x.Rect.X).ToList());
                current = [sortedByY[i]];
                currentY = sortedByY[i].Rect.CenterY;
            }
        }

        rows.Add(current.OrderBy(x => x.Rect.X).ToList());
        return rows.SelectMany(r => r).ToList();
    }

    private static bool ContainsCenter(NormalizedRect container, NormalizedRect item) =>
        item.CenterX >= container.X && item.CenterX <= container.Right
        && item.CenterY >= container.Y && item.CenterY <= container.Bottom;

    private static double OverlapFraction(NormalizedRect item, NormalizedRect box)
    {
        var xOverlap = Math.Max(0.0, Math.Min(item.Right, box.Right) - Math.Max(item.X, box.X));
        var yOverlap = Math.Max(0.0, Math.Min(item.Bottom, box.Bottom) - Math.Max(item.Y, box.Y));
        var interArea = xOverlap * yOverlap;
        var itemArea = Math.Max(item.Width * item.Height, 1e-6);
        return interArea / itemArea;
    }

    private static double DistanceToRect(double px, double py, NormalizedRect rect)
    {
        var dx = px < rect.X ? rect.X - px : (px > rect.Right ? px - rect.Right : 0.0);
        var dy = py < rect.Y ? rect.Y - py : (py > rect.Bottom ? py - rect.Bottom : 0.0);
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}
