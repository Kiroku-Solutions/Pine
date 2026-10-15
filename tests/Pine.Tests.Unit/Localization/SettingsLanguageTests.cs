using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MediatR;
using NSubstitute;
using Pine.Application.Settings.Queries;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.ViewModels;
using Xunit;

namespace Pine.Tests.Unit.Localization;

/// <summary>
/// The assertion whose absence is the whole of TD-45.
/// </summary>
/// <remarks>
/// <para>
/// THE DEFECT, EXACTLY. The Settings screen has offered a "Display Language" picker with English
/// and Spanish since the multi-vault work. Choosing Spanish persisted
/// <c>Appearance.Language = "1"</c> into the vault and changed nothing on screen, because nothing
/// in <c>src/</c> ever read that key for any purpose and nothing ever assigned
/// <c>CurrentUICulture</c>. The picker was not miswired -- there was nothing on the other end of
/// the wire.
/// </para>
/// <para>
/// ITS SIBLINGS ON THE SAME PANEL HAD TWO LEGS AND IT HAD ONE. <c>OnSelectedThemeIndexChanged</c>
/// applies and persists; <c>OnSelectedPaletteIndexChanged</c> applies and persists;
/// <c>OnSelectedLanguageIndexChanged</c> only persisted. A test at this level is the cheapest
/// instrument that would have noticed, and it did not exist.
/// </para>
/// </remarks>
public class SettingsLanguageTests
{
    /// <summary>Choosing a language moves the localizer, which is what "apply" means.</summary>
    [Fact]
    public void ChoosingALanguage_MovesTheLocalizer()
    {
        var localizer = new Localizer();
        var viewModel = NewSettings(out _, localizer);

        viewModel.SelectedLanguage = localizer.Available[1];

        localizer.CurrentLanguage.Should().Be(
            "es",
            "this is the leg the picker never had: it persisted a number and applied nothing");
    }

    /// <summary>The choice is persisted, outside the vault.</summary>
    /// <remarks>
    /// D3's consequence, asserted rather than assumed: an in-vault preference cannot be read by the
    /// unlock screen, which is rendered before any vault is open and is the first thing a user
    /// sees.
    /// </remarks>
    [Fact]
    public async Task ChoosingALanguage_PersistsOutsideTheVault()
    {
        var localizer = new Localizer();
        var viewModel = NewSettings(out var store, localizer);

        // INITIALISATION FIRST, AND THAT IS PART OF THE ASSERTION. Every change handler on this
        // page opens with `if (_isInitializing) return;` so that applying a stored value does not
        // write it straight back. Persisting is therefore only expected after the page is loaded --
        // whereas APPLYING is not gated at all, which is what ChoosingALanguage_MovesTheLocalizer
        // relies on.
        await viewModel.InitializeAsync(CancellationToken.None);

        viewModel.SelectedLanguage = localizer.Available[1];

        store.Read().Should().Be("es");
    }

    /// <summary>
    /// Apply happens before persist, so a failed write leaves the screen in the chosen language.
    /// </summary>
    /// <remarks>
    /// §6.2, and the same order <c>ThemeManager</c> uses. A store with nowhere to write is the
    /// sharpest available version of "the write failed": it returns <see langword="false"/> every
    /// time, and the language must still have moved.
    /// </remarks>
    [Fact]
    public void WhenThePreferenceCannotBeWritten_TheLanguageStillChanges()
    {
        var localizer = new Localizer();
        var store = new LanguagePreferenceStore(path: null);
        var viewModel = new SettingsViewModel(Substitute.For<ISender>(), localizer, store);

        viewModel.SelectedLanguage = localizer.Available[1];

        localizer.CurrentLanguage.Should().Be("es",
            "a cosmetic write failing must not leave the user looking at a language they did not choose");
    }

    /// <summary>
    /// A pre-TD-45 in-vault choice transfers to the new store on first load (D5).
    /// </summary>
    [Theory]
    [InlineData("0", "en")]
    [InlineData("1", "es")]
    public async Task ALegacyInVaultChoice_MigratesOnce(string legacy, string expected)
    {
        var localizer = new Localizer();
        var store = new LanguagePreferenceStore(path: null);
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<GetSettingQuery>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
                ((GetSettingQuery)callInfo[0]).Key == SettingsViewModel.LanguageSettingKey
                    ? Task.FromResult<string?>(legacy)
                    : Task.FromResult<string?>(null));

        var viewModel = new SettingsViewModel(sender, localizer, store);
        await viewModel.InitializeAsync(CancellationToken.None);

        localizer.CurrentLanguage.Should().Be(expected);
        store.Read().Should().Be(expected, "nobody loses a choice they already made");
    }

    /// <summary>
    /// Migration never overwrites a choice the user has already made in the new store.
    /// </summary>
    /// <remarks>
    /// ONE-WAY AND IDEMPOTENT (§6.3). Without this, opening an older vault would silently reset the
    /// interface language to whatever that vault happened to remember -- a per-vault value
    /// overriding a per-user one, which is the confusion D4 removed.
    /// </remarks>
    [Fact]
    public async Task ALegacyChoice_DoesNotOverwriteAPreferenceAlreadyMade()
    {
        var localizer = new Localizer();
        var store = new LanguagePreferenceStore(path: null);
        store.Write("en");

        // ONLY the language key answers. A substitute that returned "1" for every setting would
        // also answer the theme query, and ThemeManager.ApplyVariant touches Avalonia objects that
        // demand the UI thread -- turning this into a test about dispatcher affinity.
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<GetSettingQuery>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
                ((GetSettingQuery)callInfo[0]).Key == SettingsViewModel.LanguageSettingKey
                    ? Task.FromResult<string?>("1")
                    : Task.FromResult<string?>(null));

        var viewModel = new SettingsViewModel(sender, localizer, store);
        await viewModel.InitializeAsync(CancellationToken.None);

        store.Read().Should().Be("en", "a populated store is never overwritten from a vault");
    }

    /// <summary>The picker offers what the localizer ships, not a hardcoded pair.</summary>
    /// <remarks>
    /// The two <c>ComboBoxItem</c> elements this replaced were English literals inside the language
    /// picker itself -- the one control where an untranslated string is least forgivable.
    /// </remarks>
    [Fact]
    public void ThePicker_OffersTheShippedLanguages()
    {
        var localizer = new Localizer();

        NewSettings(out _, localizer).Languages.Should().BeSameAs(localizer.Available);
    }

    private static SettingsViewModel NewSettings(out LanguagePreferenceStore store, ILocalizer localizer)
    {
        store = new LanguagePreferenceStore(path: null);
        return new SettingsViewModel(Substitute.For<ISender>(), localizer, store);
    }
}
