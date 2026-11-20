using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Pine.Core.Ai;

/// <summary>Which character a receipt uses as its decimal separator, as inferred from the whole document.</summary>
public enum DecimalSeparator
{
    /// <summary>No evidence either way (for example every amount is an integer or a lone "x.yyy").</summary>
    Unknown = 0,

    /// <summary>"1,234.56" convention: the point is the decimal separator.</summary>
    Point = 1,

    /// <summary>"1.234,56" convention: the comma is the decimal separator.</summary>
    Comma = 2,
}

/// <summary>
/// The one place receipt amounts are read from text. Heuristics, LayoutLM decoding and tests all go through here.
/// </summary>
/// <remarks>
/// <para>
/// WHY A DOCUMENT-LEVEL SEPARATOR. "1.000" and "2,500" are readable two ways: thousands (1000, 2500) or
/// three-decimal quantities (1.000 kg, 2.500 l). The token alone cannot say; the rest of the receipt can. If
/// most other amounts are "x,yy" the comma is the decimal separator, so "2,500" is 2.5 and "1.000" is 1000.
/// If they are "x.yy" the point is decimal and "1.000" is 1.0. With no evidence the legacy reading stays
/// (a single separator followed by three digits is a thousands group), which is what CORD-style "60.000"
/// needs. A leading "0" ("0,500") can never be a thousands group.
/// </para>
/// <para>BCL only, so it lives in Pine.Core and is testable without an OCR engine.</para>
/// </remarks>
public static class ReceiptNumberParser
{
#pragma warning disable SYSLIB1045
    private static readonly Regex EvidenceToken = new(@"(?<![\d.,])\d[\d.,]*\d(?![\d.,])", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex NumberCore = new(@"\d[\d.,]*(?:\s\d{2,3}(?!\d))*", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PercentToken = new(@"\d\s*%", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex DigitBearingToken = new(@"[\dOolI][\dOolI.,]*", RegexOptions.Compiled | RegexOptions.CultureInvariant);
#pragma warning restore SYSLIB1045

    /// <summary>Infers the document's decimal separator from every number printed on it.</summary>
    /// <param name="texts">Lines (or any text fragments) of the receipt.</param>
    public static DecimalSeparator Infer(IEnumerable<string> texts)
    {
        ArgumentNullException.ThrowIfNull(texts);
        int point = 0, comma = 0;

        foreach (var text in texts)
        {
            foreach (Match m in EvidenceToken.Matches(text ?? string.Empty))
            {
                var after = m.Index + m.Length;
                while (after < text!.Length && text[after] == ' ')
                {
                    after++;
                }

                if (after < text.Length && text[after] == '%')
                {
                    continue;
                }

                // A repeated thousands group ("1.591.600") is conclusive on its own (weight 2); a lone "x,yy" may be an
                // OCR slip, so a style needs two such tokens before it overrides the thousands reading.
                var weight = m.Value.Count(c => c is '.' or ',') > 1 && !(m.Value.Contains('.') && m.Value.Contains(',')) ? 2 : 1;
                switch (Vote(m.Value))
                {
                    case DecimalSeparator.Point: point += weight; break;
                    case DecimalSeparator.Comma: comma += weight; break;
                }
            }
        }

        return point >= 2 && point > comma ? DecimalSeparator.Point
            : comma >= 2 && comma > point ? DecimalSeparator.Comma
            : DecimalSeparator.Unknown;
    }

    private static DecimalSeparator Vote(string token)
    {
        var dots = token.Count(c => c == '.');
        var commas = token.Count(c => c == ',');
        if (dots == 0 && commas == 0)
        {
            return DecimalSeparator.Unknown;
        }

        if (dots > 0 && commas > 0)
        {
            return token.LastIndexOf('.') > token.LastIndexOf(',') ? DecimalSeparator.Point : DecimalSeparator.Comma;
        }

        var sep = dots > 0 ? '.' : ',';
        var parts = token.Split(sep);
        var decimalOf = sep == '.' ? DecimalSeparator.Point : DecimalSeparator.Comma;
        var thousandsOf = sep == '.' ? DecimalSeparator.Comma : DecimalSeparator.Point;

        if (parts.Length > 2)
        {
            // "1.591.600" is thousands (the OTHER separator is the decimal one); "05.03.2026" says nothing.
            return parts.Skip(1).All(p => p.Length == 3) && parts[0].Length is >= 1 and <= 3 ? thousandsOf : DecimalSeparator.Unknown;
        }

        var after = parts[1].Length;
        if (after != 3)
        {
            return decimalOf;
        }

        return parts[0] == "0" ? decimalOf : DecimalSeparator.Unknown;
    }

    /// <summary>True when the text is a percentage ("12,00%", "6.25 %"), which is a rate and never an amount.</summary>
    public static bool IsPercent(string? text) =>
        !string.IsNullOrWhiteSpace(text) && PercentToken.IsMatch(text);

    /// <summary>
    /// Parses one printed number or amount ("$2,08", "1.234,56", "2 08", "TOTAL: 9,99 EUR", "-5,00", "(5,00)").
    /// Returns false for percentages and text with no digits. The result keeps its sign.
    /// </summary>
    public static bool TryParse(string? text, DecimalSeparator separator, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(text) || IsPercent(text))
        {
            return false;
        }

        var cleaned = FixOcrConfusions(text);
        var m = NumberCore.Match(cleaned);
        if (!m.Success)
        {
            return false;
        }

        var negative = (m.Index > 0 && cleaned[m.Index - 1] == '-') || (cleaned.Contains('(') && cleaned.Contains(')'));
        var token = m.Value.Trim();

        // "2 08" is 2.08 (the OCR lost the separator); "1 234" and "1 234 567" are thousands groups.
        var parts = token.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 1)
        {
            var first = parts[0];
            var firstIsPlain = first.All(char.IsDigit);
            if (parts.Length == 2 && firstIsPlain && parts[1].Length == 2)
            {
                token = first + "." + parts[1];
            }
            else if (firstIsPlain && first.Length <= 3 && parts.Skip(1).All(p => p.Length == 3 && p.All(char.IsDigit)))
            {
                token = string.Concat(parts);
            }
            else
            {
                token = first;
            }
        }

        token = token.TrimEnd('.', ',');
        if (token.Length == 0 || !decimal.TryParse(Canonical(token, separator), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            return false;
        }

        value = negative ? -parsed : parsed;
        return true;
    }

    /// <summary>Parses a strictly positive amount (a total, a tax). Zero and negative values are rejected.</summary>
    public static bool TryParseAmount(string? text, DecimalSeparator separator, out decimal amount)
    {
        if (TryParse(text, separator, out var v) && v > 0m)
        {
            amount = v;
            return true;
        }

        amount = 0m;
        return false;
    }

    /// <summary>Reads a bare token (digits, points and commas only) into a decimal; false when it is not a number.</summary>
    public static bool TryParseToken(string token, DecimalSeparator separator, out decimal value) =>
        decimal.TryParse(Canonical(token.TrimEnd('.', ','), separator), NumberStyles.Number, CultureInfo.InvariantCulture, out value);

    /// <summary>True when a lone separator group in <paramref name="token"/> is a thousands group under <paramref name="separator"/>.</summary>
    public static bool HasThousandsGrouping(string token, DecimalSeparator separator)
    {
        var canonical = Canonical(token, separator);
        return (token.Contains('.') || token.Contains(',')) && !canonical.Contains('.');
    }

    private static string FixOcrConfusions(string text) =>
        DigitBearingToken.Replace(text, t =>
            t.Value.Any(char.IsDigit) ? t.Value.Replace('O', '0').Replace('o', '0').Replace('l', '1').Replace('I', '1') : t.Value);

    /// <summary>Rewrites a digits/./, token as an invariant decimal string ("1.234,56" becomes "1234.56").</summary>
    private static string Canonical(string t, DecimalSeparator separator)
    {
        var lastDot = t.LastIndexOf('.');
        var lastComma = t.LastIndexOf(',');
        if (lastDot < 0 && lastComma < 0)
        {
            return t;
        }

        if (lastDot >= 0 && lastComma >= 0)
        {
            var dec = Math.Max(lastDot, lastComma);
            return Strip(t[..dec]) + "." + t[(dec + 1)..];
        }

        var sep = lastDot >= 0 ? '.' : ',';
        var count = t.Count(c => c == sep);
        var idx = t.LastIndexOf(sep);
        var before = t[..idx];
        var after = t[(idx + 1)..];

        if (count >= 2)
        {
            var groups = t.Split(sep);
            return groups.Skip(1).All(g => g.Length == 3)
                ? t.Replace(sep.ToString(), string.Empty)
                : Strip(before) + "." + after;
        }

        if (after.Length != 3)
        {
            return before + "." + after;
        }

        // A thousands group cannot follow a lone zero ("0,500"); a longer integer part ("1178.100") is more likely an
        // OCR-damaged grouped number than a three-decimal quantity, so it stays thousands.
        var isDecimalHere = before == "0"
            || (separator == DecimalSeparator.Point && sep == '.')
            || (separator == DecimalSeparator.Comma && sep == ',');
        return isDecimalHere ? before + "." + after : before + after;

        static string Strip(string s) => s.Replace(".", string.Empty).Replace(",", string.Empty);
    }
}
