using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;

namespace Pine.Ui.Avalonia.ViewModels;

/// <summary>
/// English display names for the ISO 4217 codes in <c>IsoCurrencies</c>, for the currency picker (TD-76).
/// </summary>
/// <remarks>
/// A display aid only: it carries no rounding or validation policy (that is <c>IsoCurrencies</c>) and is
/// deliberately independent of the host's ICU data, so the picker reads the same on every machine. The
/// names live in an embedded text resource (<c>currency-names-english.txt</c>) rather than in code literals,
/// because user-facing text does not belong in C# (41-Localization). They are English in both languages;
/// the code, which is what users actually search by, is not translated. A code with no entry shows as
/// just its code, and a unit test pins that every code in the table has a name.
/// </remarks>
internal static class CurrencyNames
{
    private const string ResourceName = "Pine.Ui.Avalonia.currency-names-english.txt";

    private static readonly FrozenDictionary<string, string> Names = Load();

    /// <summary>The English name for <paramref name="code"/>, or the code itself when none is known.</summary>
    internal static string NameOf(string code) => Names.TryGetValue(code, out var name) ? name : code;

    private static FrozenDictionary<string, string> Load()
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);

        using var stream = typeof(CurrencyNames).Assembly.GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            return names.ToFrozenDictionary(StringComparer.Ordinal);
        }

        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            var tab = line.IndexOf((char)9, StringComparison.Ordinal);
            if (line.StartsWith('#') || tab <= 0)
            {
                continue;
            }

            names[line[..tab]] = line[(tab + 1)..].Trim();
        }

        return names.ToFrozenDictionary(StringComparer.Ordinal);
    }
}
