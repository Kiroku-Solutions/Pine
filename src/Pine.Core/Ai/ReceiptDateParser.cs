using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Pine.Core.Ai;

/// <summary>
/// One reading of a date printed on a receipt, together with the other reading when the printed
/// string admits two.
/// </summary>
/// <remarks>
/// <para>
/// WHY THE ALTERNATIVE IS CARRIED RATHER THAN RESOLVED (TD-21). <c>05/03/2026</c> is 5 March in
/// Quito and 3 May in Chicago, and no amount of parsing skill can tell which one the paper meant.
/// The previous code answered anyway — it tried <see cref="CultureInfo.InvariantCulture"/> first,
/// which IS month-first, so its <c>es-EC</c> leg was unreachable for exactly the strings it existed
/// to handle, and the transaction landed in the wrong month's budget with no trace. Returning both
/// readings moves that decision to the one party who can actually make it: the person holding the
/// receipt.
/// </para>
/// </remarks>
public readonly record struct ReceiptDateCandidate(
    DateTimeOffset Value,
    bool IsAmbiguous,
    DateTimeOffset? Alternative);

/// <summary>
/// Parses the date out of a line of receipt text using an explicit, ordered list of formats.
/// </summary>
/// <remarks>
/// <para>
/// WHY <c>TryParseExact</c> AND NOT <c>TryParse</c>. A culture's <c>TryParse</c> accepts far more
/// than that culture's own short pattern, so cascading cultures through <c>TryParse</c> means the
/// first culture in the list swallows nearly everything and the rest are decoration. That is
/// precisely the defect this type replaces. Matching against named patterns makes the order
/// meaningful and the behaviour falsifiable.
/// </para>
/// <para>
/// WHY ISO IS ALWAYS FIRST. <c>yyyy-MM-dd</c> cannot be read two ways, so trying it first can never
/// cost a correct answer and always saves an unnecessary ambiguity prompt.
/// </para>
/// <para>
/// Only BCL types are used, so this stays in <c>Pine.Core</c> and is testable without an OCR engine.
/// </para>
/// </remarks>
public static class ReceiptDateParser
{
    /// <summary>Patterns that have exactly one reading.</summary>
    private static readonly string[] UnambiguousFormats =
    [
        "yyyy-MM-dd", "yyyy/MM/dd", "yyyy.MM.dd",
    ];

    private static readonly string[] DayFirstFormats =
    [
        "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy", "dd.MM.yyyy", "d.M.yyyy",
        "dd/MM/yy", "d/M/yy", "dd-MM-yy", "d-M-yy", "dd.MM.yy", "d.M.yy",
    ];

    private static readonly string[] MonthFirstFormats =
    [
        "MM/dd/yyyy", "M/d/yyyy", "MM-dd-yyyy", "M-d-yyyy", "MM.dd.yyyy", "M.d.yyyy",
        "MM/dd/yy", "M/d/yy", "MM-dd-yy", "M-d-yy", "MM.dd.yy", "M.d.yy",
    ];

    /// <summary>
    /// Matches a date-shaped run of digits. Deliberately permissive about component width and
    /// separator; deciding what the components MEAN is the format list's job, not the regex's.
    /// </summary>
#pragma warning disable SYSLIB1045
    private static readonly Regex DatePattern = new(
        @"\b(\d{4}[/.\-]\d{1,2}[/.\-]\d{1,2}|\d{1,2}[/.\-]\d{1,2}[/.\-]\d{2,4})\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
#pragma warning restore SYSLIB1045

    /// <summary>
    /// Reports whether the culture writes the day before the month in its short date pattern.
    /// </summary>
    /// <remarks>
    /// The pattern is inspected rather than the culture being compared against a list of names: a
    /// list would have to be maintained, and would be wrong for the first locale nobody thought of.
    /// </remarks>
    public static bool PrefersDayFirst(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        var pattern = culture.DateTimeFormat.ShortDatePattern;
        var dayIndex = pattern.IndexOf('d', StringComparison.Ordinal);
        var monthIndex = pattern.IndexOf('M', StringComparison.Ordinal);

        if (dayIndex < 0 || monthIndex < 0)
        {
            return false;
        }

        return dayIndex < monthIndex;
    }

    /// <summary>
    /// Parses the first date-shaped token in <paramref name="text"/>.
    /// </summary>
    /// <param name="text">A line of receipt text, which may contain other words.</param>
    /// <param name="culture">
    /// The culture whose component order is preferred for an ambiguous string. Defaults to
    /// <see cref="Pine.Core.Common.MachineCulture.RegionalFormat"/> — the single place this type reads
    /// ambient state. Not <see cref="CultureInfo.CurrentCulture"/>, which follows the display language
    /// in a running head; a receipt's date order is the region's, whatever language the screen is in.
    /// </param>
    /// <param name="now">The clock the plausibility window is measured against. Defaults to now.</param>
    /// <returns>
    /// The preferred reading, flagged when the other reading is equally plausible, or <c>null</c>
    /// when the text holds no plausible date at all.
    /// </returns>
    public static ReceiptDateCandidate? Parse(string text, CultureInfo? culture = null, DateTimeOffset? now = null, bool? dayFirst = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = DatePattern.Match(text);
        if (!match.Success)
        {
            return ParseWithMonthName(text, now ?? DateTimeOffset.Now);
        }

        var token = match.Groups[1].Value;
        var reference = now ?? DateTimeOffset.Now;

        // ISO first: it has one reading, so it can never be the ambiguous case.
        if (TryExact(token, UnambiguousFormats, reference, out var iso))
        {
            return new ReceiptDateCandidate(iso, IsAmbiguous: false, Alternative: null);
        }

        // The machine's region, not the display language: a receipt is printed where the user shops (TD-21).
        var dayFirstPreferred = dayFirst ?? PrefersDayFirst(culture ?? Pine.Core.Common.MachineCulture.RegionalFormat);

        var preferredFormats = dayFirstPreferred ? DayFirstFormats : MonthFirstFormats;
        var otherFormats = dayFirstPreferred ? MonthFirstFormats : DayFirstFormats;

        var hasPreferred = TryExact(token, preferredFormats, reference, out var preferred);
        // A document-level order (another date on the same receipt has a day above 12) settles the question.
        var hasOther = TryExact(token, otherFormats, reference, out var other);
        if (dayFirst.HasValue && hasPreferred)
        {
            hasOther = false;
        }

        if (!hasPreferred && !hasOther)
        {
            return null;
        }

        if (!hasPreferred)
        {
            // Only one reading survives -- 13/07 cannot be month-first -- so there is nothing to ask.
            return new ReceiptDateCandidate(other, IsAmbiguous: false, Alternative: null);
        }

        if (!hasOther || other == preferred)
        {
            return new ReceiptDateCandidate(preferred, IsAmbiguous: false, Alternative: null);
        }

        return new ReceiptDateCandidate(preferred, IsAmbiguous: true, Alternative: other);
    }

    /// <summary>
    /// Infers the day/month order of the whole receipt from every numeric date on it: "26/02/2019" can only be
    /// day-first, "02/26/2019" only month-first. Returns null when nothing settles it (or the dates disagree).
    /// </summary>
    public static bool? InferDayFirst(IEnumerable<string> texts)
    {
        ArgumentNullException.ThrowIfNull(texts);
        bool dayFirst = false, monthFirst = false;
        foreach (var text in texts)
        {
            foreach (Match m in DatePattern.Matches(text ?? string.Empty))
            {
                var parts = m.Groups[1].Value.Split('/', '.', '-');
                if (parts.Length != 3 || parts[0].Length == 4)
                {
                    continue;
                }

                if (int.TryParse(parts[0], out var a) && a is > 12 and <= 31) { dayFirst = true; }
                if (int.TryParse(parts[1], out var b) && b is > 12 and <= 31) { monthFirst = true; }
            }
        }

        return dayFirst == monthFirst ? null : dayFirst;
    }

    private static readonly Dictionary<string, int> MonthNames = BuildMonthNames();

    private static Dictionary<string, int> BuildMonthNames()
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        string[][] names =
        [
            ["enero", "ene", "january", "jan"], ["febrero", "feb", "february"], ["marzo", "mar", "march"],
            ["abril", "abr", "april", "apr"], ["mayo", "may"], ["junio", "jun", "june"],
            ["julio", "jul", "july"], ["agosto", "ago", "august", "aug"],
            ["septiembre", "setiembre", "sep", "sept", "set", "september"], ["octubre", "oct", "october"],
            ["noviembre", "nov", "november"], ["diciembre", "dic", "december", "dec"],
        ];
        for (var i = 0; i < names.Length; i++)
        {
            foreach (var n in names[i]) { map[n] = i + 1; }
        }

        return map;
    }

#pragma warning disable SYSLIB1045
    // "15 de noviembre de 2026", "15-NOV-2026", "15 nov. 2026"
    private static readonly Regex DayMonthYear = new(
        @"\b(\d{1,2})(?:\s*(?:del|de)\s*|\s*[-/.\s]\s*)([A-Za-z]{3,10})\.?(?:\s*(?:del|de)\s*|\s*[-/.,\s]\s*)(\d{4})\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    // "noviembre 15, 2026", "Nov 15 2026"
    private static readonly Regex MonthDayYear = new(
        @"\b([A-Za-z]{3,10})\.?\s+(\d{1,2})(?:st|nd|rd|th)?,?\s+(\d{4})\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
#pragma warning restore SYSLIB1045

    /// <summary>Month-name dates (Spanish and English). They have one reading, so they are never ambiguous.</summary>
    private static ReceiptDateCandidate? ParseWithMonthName(string text, DateTimeOffset now)
    {
        foreach (Match m in DayMonthYear.Matches(text))
        {
            if (TryBuild(m.Groups[3].Value, m.Groups[2].Value, m.Groups[1].Value, now, out var d))
            {
                return new ReceiptDateCandidate(d, false, null);
            }
        }

        foreach (Match m in MonthDayYear.Matches(text))
        {
            if (TryBuild(m.Groups[3].Value, m.Groups[1].Value, m.Groups[2].Value, now, out var d))
            {
                return new ReceiptDateCandidate(d, false, null);
            }
        }

        return null;
    }

    private static bool TryBuild(string year, string month, string day, DateTimeOffset now, out DateTimeOffset result)
    {
        result = default;
        if (!MonthNames.TryGetValue(month.TrimEnd('.'), out var mo)
            || !int.TryParse(year, out var y) || !int.TryParse(day, out var d)
            || y <= 2000 || y > now.Year + 1 || d < 1 || d > DateTime.DaysInMonth(y, mo))
        {
            return false;
        }

        result = new DateTimeOffset(new DateTime(y, mo, d, 0, 0, 0, DateTimeKind.Local));
        return true;
    }

    /// <summary>
    /// Tries every format in order, keeping only a result inside the plausibility window.
    /// </summary>
    /// <remarks>
    /// The window is applied per candidate rather than to whichever string parsed first: applying it
    /// afterwards, as the previous code did, let an implausible reading suppress a plausible one.
    /// <see cref="DateTimeStyles.AssumeLocal"/> is kept so the offset attached matches what the rest
    /// of the pipeline already produces.
    /// </remarks>
    private static bool TryExact(string token, string[] formats, DateTimeOffset now, out DateTimeOffset result)
    {
        foreach (var format in formats)
        {
            if (DateTimeOffset.TryParseExact(token, format, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed)
                && parsed.Year > 2000
                && parsed.Year <= now.Year + 1)
            {
                result = parsed;
                return true;
            }
        }

        result = default;
        return false;
    }
}
