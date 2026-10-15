namespace Pine.Ui.Avalonia.Localization;

/// <summary>
/// One language the user can pick, as the picker's item type.
/// </summary>
/// <param name="Code">The BCP-47 code -- <c>"en"</c>, <c>"es"</c>. Neutral, never region-specific.</param>
/// <param name="NativeName">
/// The language's name IN THAT LANGUAGE: "English", "Español".
/// </param>
/// <remarks>
/// <para>
/// WHY A RECORD AND NOT AN <c>int</c> INDEX (D6). The picker used to bind
/// <c>SelectedIndex</c> to an <c>int</c> and persist that integer. An index is a position in a
/// list, and it means something different the day a language is inserted in alphabetical order --
/// every user who chose Spanish silently gets whatever now sits at slot 1. A code is stable
/// against reordering and against insertion, and it is also what .NET's own culture resolution
/// speaks.
/// </para>
/// <para>
/// LANGUAGE NAMES ARE NEVER TRANSLATED (D9). <see cref="NativeName"/> is deliberately NOT a
/// localisation key. A user who has accidentally set the interface to a language they cannot read
/// has exactly one way back: recognising their own language in this list. Rendering it through the
/// localizer would translate "Spanish" into Spanish only when the UI is already Spanish, which is
/// the one case where the user does not need the help.
/// </para>
/// </remarks>
public sealed record LanguageOption(string Code, string NativeName)
{
    /// <summary>The native name, so a bare <c>ComboBox</c> renders correctly with no template.</summary>
    public override string ToString() => NativeName;
}
