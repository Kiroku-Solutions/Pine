using System;
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
    public static ReceiptDateCandidate? Parse(string text, CultureInfo? culture = null, DateTimeOffset? now = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = DatePattern.Match(text);
        if (!match.Success)
        {
            return null;
        }

        var token = match.Groups[1].Value;
        var reference = now ?? DateTimeOffset.Now;

        // ISO first: it has one reading, so it can never be the ambiguous case.
        if (TryExact(token, UnambiguousFormats, reference, out var iso))
        {
            return new ReceiptDateCandidate(iso, IsAmbiguous: false, Alternative: null);
        }

        // The machine's region, not the display language: a receipt is printed where the user shops (TD-21).
        var dayFirstPreferred = PrefersDayFirst(culture ?? Pine.Core.Common.MachineCulture.RegionalFormat);

        var preferredFormats = dayFirstPreferred ? DayFirstFormats : MonthFirstFormats;
        var otherFormats = dayFirstPreferred ? MonthFirstFormats : DayFirstFormats;

        var hasPreferred = TryExact(token, preferredFormats, reference, out var preferred);
        var hasOther = TryExact(token, otherFormats, reference, out var other);

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
