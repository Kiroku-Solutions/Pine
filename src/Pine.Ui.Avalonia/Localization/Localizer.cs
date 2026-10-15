using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace Pine.Ui.Avalonia.Localization;

/// <summary>
/// The string table, and the one object every translated string in Pine is read from.
/// </summary>
/// <remarks>
/// <para>
/// WHY THE RESOURCES ARE <c>EmbeddedResource</c> AND NOT <c>AvaloniaResource</c>. The design
/// (§5) named <c>AvaloniaResource</c>, and §13 recorded, as an unverified risk, that Avalonia 12
/// might have moved the asset-loading API -- with <c>Assembly.GetManifestResourceStream</c> named
/// as the fallback. **The risk was routed around rather than resolved.** `AssetLoader` does still
/// exist in Avalonia 12.0.5, but it resolves through <c>AvaloniaLocator</c>, which means loading
/// the string table requires an Avalonia platform to have been configured. Tying "can this
/// application read its own words" to "has the rendering platform been set up" is a dependency
/// this type should not have, and §8 is explicit that nothing here may take down a screen. The
/// manifest-stream route is BCL-only, needs no platform, no source generator and no satellite
/// assembly, and is therefore also immune to the trimming failure mode that ruled out `.resx` in
/// the first place (§3.3). Recorded as a deviation from §5.
/// </para>
/// <para>
/// WHY <see cref="JsonDocument"/> AND NOT <c>JsonSerializer.Deserialize&lt;T&gt;</c> (D11).
/// Reflection-based deserialisation is precisely what trimming breaks, silently, on the two AOT
/// heads. Document traversal needs no type metadata at all.
/// </para>
/// <para>
/// THE ENGLISH TABLE IS LOADED TWICE ON PURPOSE -- once as the active language when English is
/// active, and always as the fallback dictionary. It is the resolution floor (§5.2), so it must be
/// present whatever the active language is.
/// </para>
/// </remarks>
public sealed class Localizer : ILocalizer
{
    /// <summary>The language every fallback chain ends at.</summary>
    public const string FallbackLanguage = "en";

    private static readonly IReadOnlyList<LanguageOption> Languages =
    [
        new LanguageOption("en", "English"),
        new LanguageOption("es", "Español"),
    ];

    private readonly Dictionary<string, string> _fallback;
    private readonly CultureInfo _regionalFormat;
    private Dictionary<string, string> _active;

    /// <summary>
    /// Creates the localizer on the fallback language, with this machine's regional format.
    /// </summary>
    /// <remarks>
    /// The regional format is <c>MachineCulture.RegionalFormat</c>, captured once for the process. Reading
    /// <see cref="CultureInfo.CurrentCulture"/> here instead would, for any localizer built after
    /// <c>DisplayCultureFollower</c> ran, read back the display culture and never return to the region.
    /// </remarks>
    public Localizer()
        : this(Pine.Core.Common.MachineCulture.RegionalFormat)
    {
    }

    /// <summary>
    /// Creates the localizer on the fallback language, with an explicit regional format.
    /// </summary>
    /// <param name="regionalFormat">The culture the user's machine formats with; see <see cref="DisplayCulture"/>.</param>
    public Localizer(CultureInfo regionalFormat)
    {
        ArgumentNullException.ThrowIfNull(regionalFormat);

        _regionalFormat = regionalFormat;
        _fallback = Load(FallbackLanguage);
        _active = _fallback;
        CurrentLanguage = FallbackLanguage;
        CurrentCulture = DisplayCulture(FallbackLanguage, regionalFormat);
    }

    /// <summary>
    /// The instance XAML reads through, created once.
    /// </summary>
    /// <remarks>
    /// A STATIC IS A XAML NECESSITY, NOT A DESIGN PREFERENCE (§5.1). A
    /// <see cref="global::Avalonia.Markup.Xaml.MarkupExtension"/> is constructed by the XAML loader
    /// and has no access to the DI container, so <c>TrExtension</c> needs a statically reachable
    /// source. The same object is registered as the <see cref="ILocalizer"/> singleton, so view
    /// models and tests see exactly what the markup sees. **Nothing except <c>TrExtension</c> may
    /// use this property** -- everything else injects the interface, which is what makes the
    /// localizer substitutable in a test.
    /// </remarks>
    public static Localizer Instance { get; } = new();

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <inheritdoc />
    public event EventHandler? LanguageChanged;

    /// <inheritdoc />
    public CultureInfo CurrentCulture { get; private set; }

    /// <inheritdoc />
    public string CurrentLanguage { get; private set; }

    /// <inheritdoc />
    public IReadOnlyList<LanguageOption> Available => Languages;

    /// <inheritdoc />
    /// <remarks>
    /// RESOLUTION ORDER, AND WHY THE LAST STEP IS THE KEY AND NOT AN EMPTY STRING (§5.2). Active
    /// language, then <c>en</c>, then the key itself. In <c>DEBUG</c> the key is wrapped in
    /// exclamation marks so a missing string is impossible to miss while developing; in
    /// <c>RELEASE</c> it renders bare, because a user is better served by a button that reads
    /// <c>Settings.Save</c> than by one that reads nothing at all and cannot be described over the
    /// phone.
    /// </remarks>
    public string this[string key]
    {
        get
        {
            if (string.IsNullOrEmpty(key))
            {
                return string.Empty;
            }

            if (_active.TryGetValue(key, out var value) || _fallback.TryGetValue(key, out value))
            {
                return value;
            }

#if DEBUG
            return $"!{key}!";
#else
            return key;
#endif
        }
    }

    /// <inheritdoc />
    public string Format(string key, params object?[] arguments)
    {
        var template = this[key];

        if (arguments is null || arguments.Length == 0)
        {
            return template;
        }

        try
        {
            return string.Format(CurrentCulture, template, arguments);
        }
        catch (FormatException)
        {
            // A translator wrote a placeholder the call site does not supply, or vice versa. That
            // is a bad string, not a reason to fail the screen (§8). The unformatted template is
            // the most useful thing left: it names the key's content and it is visibly wrong.
            return template;
        }
    }

    /// <inheritdoc />
    public void SetLanguage(string code)
    {
        var resolved = Resolve(code);

        if (string.Equals(resolved, CurrentLanguage, StringComparison.Ordinal))
        {
            return;
        }

        _active = string.Equals(resolved, FallbackLanguage, StringComparison.Ordinal)
            ? _fallback
            : Load(resolved);

        CurrentLanguage = resolved;
        CurrentCulture = DisplayCulture(resolved, _regionalFormat);

        // BOTH NAMES ARE RAISED, AND THE SECOND ONE IS THE ONE THAT WORKS.
        //
        // "Item[]" is the BCL convention for "every indexed value on this object changed" -- it is
        // what System.Windows.Data.Binding.IndexerName is defined as, and it is what the design
        // assumed. AVALONIA DOES NOT LISTEN FOR IT. Measured, with a bound TextBlock over a
        // notifying indexer, on a pumping headless dispatcher:
        //
        //     notify 'Item[]'    : before:a.b -> before:a.b     (ignored)
        //     notify 'Item'      : before:a.b -> after:a.b      (re-evaluated)
        //     notify 'Item[a.b]' : before:a.b -> before:a.b     (ignored)
        //     notify ''          : before:a.b -> before:a.b     (ignored)
        //
        // The last row is the surprising one: an empty name means "everything changed" under INPC,
        // and it does not reach an indexer binding here either. So "Item" is not a preference, it
        // is the only string that works. "Item[]" is raised first anyway, because it costs one
        // event, it is what any non-Avalonia consumer would listen for, and it is what a later
        // Avalonia would most plausibly move to.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentCulture)));
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The culture numbers and dates are written in, for a language and a machine's regional format
    /// (TD-21).
    /// </summary>
    /// <param name="language">A language code from <see cref="Available"/>.</param>
    /// <param name="regionalFormat">The culture the operating system formats with.</param>
    /// <returns>A specific, read-only culture.</returns>
    /// <remarks>
    /// <para>
    /// THE REGION IS KEPT WHEN IT SPEAKS THE LANGUAGE, AND ONLY THEN. A Spanish screen on a machine set
    /// to Ecuador writes <c>es-EC</c>'s figures and dates; a Spanish screen on a machine set to the
    /// United States writes Spain's, <c>es-ES</c>. The alternative in each direction is visibly wrong:
    /// following the region regardless of language put "sept. 14, 2026" and "1,500.00" into sentences
    /// on a Spanish screen — the state this row was left in — and following the language regardless of
    /// region would write British dates for an Ecuadorian who chose Spanish, or American ones for a
    /// British user who chose English. The language decides the conventions a sentence needs to read
    /// naturally; the region decides among the variants of that language.
    /// </para>
    /// <para>
    /// A NEUTRAL CULTURE IS NEVER RETURNED. <c>es</c> has number and date formats in .NET, but they are
    /// a guess at a region; <see cref="CultureInfo.CreateSpecificCulture"/> names the guess.
    /// </para>
    /// </remarks>
    public static CultureInfo DisplayCulture(string language, CultureInfo regionalFormat)
    {
        ArgumentNullException.ThrowIfNull(regionalFormat);

        var resolved = Resolve(language);

        if (!regionalFormat.IsNeutralCulture
            && !string.IsNullOrEmpty(regionalFormat.Name)
            && string.Equals(regionalFormat.TwoLetterISOLanguageName, resolved, StringComparison.OrdinalIgnoreCase))
        {
            return CultureInfo.ReadOnly(regionalFormat);
        }

        return CultureInfo.ReadOnly(CultureInfo.CreateSpecificCulture(resolved));
    }

    /// <summary>
    /// Maps any culture name onto a language this application actually ships.
    /// </summary>
    /// <param name="code">A BCP-47 code, possibly region-specific, possibly unknown or empty.</param>
    /// <returns>A code from <see cref="Available"/>; never anything else.</returns>
    /// <remarks>
    /// D7 IS WHAT MAKES THIS SHORT. Shipping neutral <c>es</c> rather than <c>es-ES</c> means
    /// `es-EC` -- the locale the OCR extractor already works in -- and `es-419` and `es-MX` all
    /// reduce to the same file by taking the two-letter prefix. A region-specific table would have
    /// needed a real fallback ladder here.
    /// </remarks>
    public static string Resolve(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return FallbackLanguage;
        }

        var separator = code.IndexOfAny(['-', '_']);
        var neutral = separator > 0 ? code[..separator] : code;

        foreach (var language in Languages)
        {
            if (string.Equals(language.Code, neutral, StringComparison.OrdinalIgnoreCase))
            {
                return language.Code;
            }
        }

        return FallbackLanguage;
    }

    /// <summary>
    /// Reads one language's table out of this assembly.
    /// </summary>
    /// <remarks>
    /// NEVER THROWS, BY §8. A missing or malformed table yields an empty dictionary, which the
    /// fallback chain then covers; total loss of English requires a corrupted build, and even then
    /// the screen renders keys rather than crashing.
    /// </remarks>
    private static Dictionary<string, string> Load(string code)
    {
        var resourceName = $"Pine.Ui.Avalonia.Localization.Resources.{code}.json";
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            using var stream = typeof(Localizer).Assembly.GetManifestResourceStream(resourceName);

            if (stream is null)
            {
                return entries;
            }

            using var document = JsonDocument.Parse(stream);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return entries;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    entries[property.Name] = property.Value.GetString() ?? string.Empty;
                }
            }
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch (IOException)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        return entries;
    }

    /// <summary>
    /// The key set of one shipped language table, for the parity test (§9.3).
    /// </summary>
    /// <param name="code">The language whose table to read.</param>
    /// <returns>Its keys, or an empty set if the table is missing.</returns>
    /// <remarks>
    /// Exposed so `41-Localization` §13 -- "a locale marked *complete* MUST be 100% translated" --
    /// can be an executable rule rather than a sentence. It reads the shipped resource, not a test
    /// fixture, which is the only version of this assertion worth having.
    /// </remarks>
    public static IReadOnlyCollection<string> KeysOf(string code) => Load(code).Keys;

    /// <summary>
    /// One shipped language table, key to template, for the tests that hold the code's keys and
    /// argument counts to it (TD-139).
    /// </summary>
    /// <param name="code">The language whose table to read.</param>
    /// <returns>Its entries, or an empty table if it is missing.</returns>
    public static IReadOnlyDictionary<string, string> TableOf(string code) => Load(code);
}
