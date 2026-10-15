using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Pine.Ui.Avalonia.Localization;
using Xunit;

namespace Pine.Tests.Unit.Localization;

/// <summary>
/// The string table itself: parity between the shipped languages, the fallback chain, and the
/// change notification the whole feature rests on.
/// </summary>
public class LocalizerTests
{
    /// <summary>
    /// `en.json` and `es.json` carry exactly the same keys.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS IS `41-Localization` §13 MADE EXECUTABLE: "a locale marked *complete* MUST be 100%
    /// translated before release". Without it, a missing Spanish string is invisible -- the
    /// fallback chain quietly serves the English one, so a half-translated screen looks like a
    /// design choice rather than a bug.
    /// </para>
    /// <para>
    /// IT READS THE SHIPPED RESOURCES, NOT A FIXTURE. A parity test over test data would pass while
    /// the application shipped mismatched tables, which is the only failure mode worth catching.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryShippedLanguage_CarriesTheSameKeys()
    {
        var english = Localizer.KeysOf("en");
        var spanish = Localizer.KeysOf("es");

        english.Should().NotBeEmpty("a parity test over two empty tables passes and proves nothing");

        spanish.Should().BeEquivalentTo(english,
            "a locale that ships as complete may not contain untranslated strings (41-Localization "
            + "§13). Keys in en and not in es are untranslated; keys in es and not in en are dead");
    }

    /// <summary>
    /// Every language the picker offers actually has a table behind it.
    /// </summary>
    /// <remarks>
    /// The list of languages and the set of resource files are two separate edits, and D8 says
    /// "adding a language later is adding one JSON file" -- which is exactly the change that would
    /// otherwise ship an option that renders an all-English screen.
    /// </remarks>
    [Fact]
    public void EveryOfferedLanguage_HasATable()
    {
        foreach (var option in Localizer.Instance.Available)
        {
            Localizer.KeysOf(option.Code).Should().NotBeEmpty(
                "the picker offers '{0}', so a table must ship for it", option.Code);
        }
    }

    /// <summary>
    /// A key missing from the active language falls through to English; a key missing from both
    /// renders as itself.
    /// </summary>
    /// <remarks>
    /// §5.2. THE LAST STEP MATTERS MORE THAN IT LOOKS: an unresolved key renders as the key, never
    /// as an empty string. A blank button cannot be reported, reproduced, or described over the
    /// phone; a button reading <c>Settings.Save</c> names its own defect.
    /// </remarks>
    [Fact]
    public void AnUnknownKey_RendersAsItself_RatherThanAsNothing()
    {
        var rendered = Localizer.Instance["Nothing.Defines.This.Key"];

        rendered.Should().NotBeNullOrEmpty();
        rendered.Should().Contain("Nothing.Defines.This.Key");
    }

    /// <summary>
    /// An unknown or region-specific code resolves to a language that actually ships.
    /// </summary>
    /// <remarks>
    /// D7 is the reason <c>es-EC</c> matters here by name: it is the locale the OCR extractor
    /// already works in, and shipping neutral <c>es</c> is what makes it resolve without a fallback
    /// ladder. Japanese resolves to English because Japanese is not shipped (D8) -- a half-rendered
    /// screen would be worse than an English one.
    /// </remarks>
    [Theory]
    [InlineData("es", "es")]
    [InlineData("es-EC", "es")]
    [InlineData("es-419", "es")]
    [InlineData("ES-mx", "es")]
    [InlineData("en-GB", "en")]
    [InlineData("ja-JP", "en")]
    [InlineData("", "en")]
    [InlineData(null, "en")]
    public void AnyCulture_ResolvesToAShippedLanguage(string? code, string expected)
        => Localizer.Resolve(code).Should().Be(expected);

    /// <summary>
    /// Switching raises the indexer notification and <c>LanguageChanged</c>, once each, and only
    /// when something changed.
    /// </summary>
    /// <remarks>
    /// THE "ONLY WHEN SOMETHING CHANGED" HALF IS THE POINT. Every string in the application
    /// re-renders on this notification; raising it for a no-op switch would re-render the whole UI
    /// each time the settings page reloaded and re-selected the language it was already on, which
    /// it does on every visit.
    /// </remarks>
    [Fact]
    public void SetLanguage_NotifiesOncePerRealChange()
    {
        var localizer = new Localizer();
        var indexerNotifications = 0;
        var languageChanges = 0;

        localizer.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == "Item")
            {
                indexerNotifications++;
            }
        };
        localizer.LanguageChanged += (_, _) => languageChanges++;

        localizer.SetLanguage("es");
        localizer.SetLanguage("es");
        localizer.SetLanguage("es-MX");

        indexerNotifications.Should().Be(1, "two of the three calls asked for the language already active");
        languageChanges.Should().Be(1);
        localizer.CurrentLanguage.Should().Be("es");

        localizer.SetLanguage("en");
        indexerNotifications.Should().Be(2);
    }

    /// <summary>
    /// <c>Format</c> substitutes arguments, and a broken template degrades instead of throwing.
    /// </summary>
    /// <remarks>
    /// §8: localisation is presentation, and nothing here may take down a screen. A translator who
    /// writes <c>{1}</c> where the call site supplies one argument has produced a bad string, not
    /// an outage.
    /// </remarks>
    [Fact]
    public void Format_DegradesInsteadOfThrowing_OnABrokenTemplate()
    {
        var localizer = new Localizer();

        var act = () => localizer.Format("Nothing.Defines.This.Key", 1, 2, 3);

        act.Should().NotThrow();
    }
}

/// <summary>
/// The out-of-vault preference file (D3).
/// </summary>
public class LanguagePreferenceStoreTests
{
    /// <summary>A written code is read back.</summary>
    [Fact]
    public void AWrittenPreference_IsReadBack()
    {
        var path = TempFile();

        try
        {
            var store = new LanguagePreferenceStore(path);

            store.Write("es").Should().BeTrue();
            new LanguagePreferenceStore(path).Read().Should().Be("es");
        }
        finally
        {
            Delete(path);
        }
    }

    /// <summary>Nothing written means nothing stored, which is what lets startup fall to the OS culture.</summary>
    [Fact]
    public void AnAbsentFile_ReadsAsNoPreference()
        => new LanguagePreferenceStore(Path.Combine(Path.GetTempPath(), $"pine-absent-{Guid.NewGuid():N}.json"))
            .Read().Should().BeNull();

    /// <summary>
    /// A corrupt file is treated as absent rather than as an error.
    /// </summary>
    /// <remarks>
    /// §8. The alternative -- throwing out of a preference read -- would take down startup for a
    /// cosmetic setting, and startup is where this is read.
    /// </remarks>
    [Theory]
    [InlineData("not json at all")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"language\":null}")]
    [InlineData("{\"language\":\"\"}")]
    [InlineData("{\"language\":42}")]
    public void ACorruptFile_ReadsAsNoPreference(string content)
    {
        var path = TempFile();

        try
        {
            File.WriteAllText(path, content);
            new LanguagePreferenceStore(path).Read().Should().BeNull();
        }
        finally
        {
            Delete(path);
        }
    }

    /// <summary>
    /// With no writable location the preference still applies for the session.
    /// </summary>
    /// <remarks>
    /// §7's degradation clause, following <c>PineStorageOptions.DefaultDataDirectory</c>'s
    /// precedent. The picker keeps working; the choice simply does not survive a restart.
    /// </remarks>
    [Fact]
    public void WithNoWritableLocation_ThePreferenceStillHoldsForTheSession()
    {
        var store = new LanguagePreferenceStore(path: null);

        store.Write("es").Should().BeFalse("there is nowhere to write it");
        store.Read().Should().Be("es", "but the session still knows what the user chose");
    }

    private static string TempFile()
        => Path.Combine(Path.GetTempPath(), $"pine-prefs-{Guid.NewGuid():N}.json");

    private static void Delete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
