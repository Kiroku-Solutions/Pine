using System.Globalization;

namespace Pine.Core.Common;

/// <summary>
/// The regional format this machine's operating system uses, as it was when the process started.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS EXISTS (TD-21, found in plan item 2.5's review pass). Once the heads made the process culture
/// follow the display language, <see cref="CultureInfo.CurrentCulture"/> stopped meaning "where this user
/// lives" and started meaning "which language they chose". Most readers wanted the second. One did not:
/// <c>ReceiptDateParser</c> decides whether an ambiguous <c>05/03/2026</c> printed on a receipt is 5 March or
/// 3 May from the current culture, and a receipt is printed where the user shops, not in the language of
/// their screen. An Ecuadorian who chose English would have had every ambiguous receipt read month-first.
/// </para>
/// <para>
/// CAPTURED ON FIRST READ, AND THE FIRST READ IS ARRANGED. The value is read once, the first time anything
/// asks, and never again — reading it later would read back the culture the process was told to follow.
/// <c>DisplayCultureFollower</c> reads it before it changes the process culture, and <c>Localizer</c>'s
/// constructor reads it, so in a running head it is always captured before anything could have moved it.
/// </para>
/// </remarks>
public static class MachineCulture
{
    private static readonly CultureInfo Captured = CultureInfo.ReadOnly(CultureInfo.CurrentCulture);

    /// <summary>The operating system's regional format when this process started.</summary>
    public static CultureInfo RegionalFormat => Captured;
}
