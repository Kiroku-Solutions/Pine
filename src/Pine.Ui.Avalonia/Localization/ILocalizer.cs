using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;

namespace Pine.Ui.Avalonia.Localization;

/// <summary>
/// The contract every user-facing string in Pine goes through.
/// </summary>
/// <remarks>
/// <para>
/// WHY THE INDEXER IS THE PRIMARY MEMBER, AND NOT A <c>Get(string)</c> METHOD. Live language
/// switching without recreating the visual tree (D2) works because a XAML binding can target an
/// INDEXER, and raising <c>PropertyChanged("Item[]")</c> invalidates EVERY indexer binding on this
/// object at once. One notification re-renders the whole application. A <c>Get</c> method has no
/// such hook: a binding cannot target a method call, and the alternative is walking the visual tree
/// or rebuilding every view, which is what "restart to change language" usually means.
/// </para>
/// <para>
/// IT EXTENDS <see cref="INotifyPropertyChanged"/> FOR THAT REASON ALONE. The interface is a
/// binding source, not merely a service.
/// </para>
/// </remarks>
public interface ILocalizer : INotifyPropertyChanged
{
    /// <summary>The translation for <paramref name="key"/>, resolved through the fallback chain.</summary>
    /// <param name="key">A stable string ID, e.g. <c>Settings.Title</c>.</param>
    /// <returns>
    /// Never <see langword="null"/> and never empty. Resolution order is the active language, then
    /// <c>en</c>, then the key itself -- see <c>Localizer</c> for why an unresolved key renders as
    /// the key rather than as blank space.
    /// </returns>
    string this[string key] { get; }

    /// <summary>
    /// The translation for <paramref name="key"/> with <paramref name="arguments"/> substituted.
    /// </summary>
    /// <param name="key">A stable string ID whose value contains composite-format placeholders.</param>
    /// <param name="arguments">The values for <c>{0}</c>, <c>{1}</c> and so on.</param>
    /// <returns>The formatted sentence.</returns>
    /// <remarks>
    /// Formatting uses <see cref="CurrentCulture"/>, so a number inside a Spanish sentence is
    /// rendered with Spanish separators.
    /// </remarks>
    string Format(string key, params object?[] arguments);

    /// <summary>
    /// The culture numbers and dates are written in: the active language, in the machine's region when
    /// the region speaks it (TD-21). See <c>Localizer.DisplayCulture</c>.
    /// </summary>
    CultureInfo CurrentCulture { get; }

    /// <summary>The active language's BCP-47 code.</summary>
    string CurrentLanguage { get; }

    /// <summary>Every language the user may choose, in display order.</summary>
    IReadOnlyList<LanguageOption> Available { get; }

    /// <summary>
    /// Switches the active language.
    /// </summary>
    /// <param name="code">A BCP-47 code. An unknown code falls back to <c>en</c> rather than throwing.</param>
    /// <remarks>
    /// Raises the indexer notification and <see cref="LanguageChanged"/>, in that order, and only
    /// when the language actually changed.
    /// </remarks>
    void SetLanguage(string code);

    /// <summary>
    /// Raised after the active language changed.
    /// </summary>
    /// <remarks>
    /// For view models, which hold a KEY and its arguments rather than a rendered sentence (D2) and
    /// must re-render themselves. XAML does not need this event -- the indexer notification already
    /// covers every <c>{loc:Tr}</c> binding.
    /// </remarks>
    event EventHandler? LanguageChanged;
}
