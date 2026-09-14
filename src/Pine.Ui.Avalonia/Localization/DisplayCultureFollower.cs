using System;
using System.Globalization;

namespace Pine.Ui.Avalonia.Localization;

/// <summary>
/// Keeps the process's formatting culture equal to the localizer's (TD-21).
/// </summary>
/// <remarks>
/// <para>
/// WHY THE PROCESS CULTURE, AND NOT A CULTURE PASSED TO EVERY FORMATTER. What a screen formats does not
/// all go through Pine's code: Avalonia evaluates a binding's <c>StringFormat</c>, a
/// <c>NumericUpDown</c>'s decimal separator, a <c>DatePicker</c>'s month names and a <c>DataGrid</c>
/// cell's number in <see cref="CultureInfo.CurrentCulture"/>, and so does <c>Money.ToString</c>. A
/// language switch that changed only the localizer's culture left every one of those in the operating
/// system's — which is how a Spanish screen came to write "1,500.00" and "Sep 14, 2026" beside Spanish
/// sentences. One setting, followed by everything, is the version that cannot disagree with itself.
/// </para>
/// <para>
/// MEASURED SAFE BEFORE IT WAS DONE. Changing the culture a whole process formats and parses in is only
/// safe if nothing that persists or compares data depends on it implicitly. Both test suites were run
/// with the process culture forced to <c>es-EC</c> — decimal comma, day-first dates — and passed in
/// full: 1258 unit and 252 integration tests, including import, import keys, backups and migrations.
/// </para>
/// <para>
/// ONLY THE HEADS ATTACH THIS, through <c>PineCompositionRoot.InitializeUi</c>, which no test calls. A
/// test switching the shared localizer's language would otherwise switch the culture of every test
/// running beside it.
/// </para>
/// </remarks>
public static class DisplayCultureFollower
{
    /// <summary>
    /// Applies the localizer's culture to the process now, and again every time its language changes.
    /// </summary>
    /// <param name="localizer">The process's localizer.</param>
    /// <returns>A handle that stops following when disposed.</returns>
    public static IDisposable Follow(ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        // Capture the machine's region BEFORE the process culture moves, for the readers that need where the
        // user is rather than what language they chose (receipt date order). See MachineCulture.
        _ = Pine.Core.Common.MachineCulture.RegionalFormat;

        Apply(localizer.CurrentCulture);

        void OnLanguageChanged(object? sender, EventArgs e) => Apply(localizer.CurrentCulture);
        localizer.LanguageChanged += OnLanguageChanged;

        return new Subscription(() => localizer.LanguageChanged -= OnLanguageChanged);
    }

    /// <summary>
    /// Makes <paramref name="culture"/> the formatting culture of this thread and of every thread that
    /// has not chosen its own.
    /// </summary>
    /// <param name="culture">The culture to format in.</param>
    /// <remarks>
    /// Both are set. <see cref="CultureInfo.DefaultThreadCurrentCulture"/> reaches the thread pool and
    /// any thread that never set a culture; <see cref="CultureInfo.CurrentCulture"/> reaches the calling
    /// thread — the UI thread, where the language is switched — and flows into the tasks it starts. The
    /// UI culture is left alone: which strings are shown is the string table's decision, not .NET's.
    /// </remarks>
    public static void Apply(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.CurrentCulture = culture;
    }

    private sealed class Subscription(Action unsubscribe) : IDisposable
    {
        private Action? _unsubscribe = unsubscribe;

        public void Dispose()
        {
            _unsubscribe?.Invoke();
            _unsubscribe = null;
        }
    }
}
