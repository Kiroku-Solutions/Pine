using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MediatR;
using NSubstitute;
using Pine.Application.Settings;
using Pine.Application.Settings.Commands;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.ViewModels;
using Xunit;

namespace Pine.Tests.Unit.ViewModels;

/// <summary>TD-119 preferences on the Settings screen. Nothing here has been rendered.</summary>
public class SettingsPreferencesTests
{
    private static (SettingsViewModel Vm, ISender Sender) New()
    {
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<GetUserPreferencesQuery>(), Arg.Any<CancellationToken>())
            .Returns(new UserPreferencesSnapshot(TimeSpan.FromMinutes(15), 0.35, 40));
        return (new SettingsViewModel(sender, new Localizer(), new LanguagePreferenceStore(path: null), null, null), sender);
    }

    [Fact]
    public async Task Load_ShowsTheEffectivePreferences()
    {
        var (vm, _) = New();

        await vm.LoadPreferencesAsync();

        vm.AutoLockMinutesText.Should().Be("15");
        vm.CategorisationConfidenceText.Should().Be("0.35");
        vm.TransactionsPageSizeText.Should().Be("40");
    }

    [Fact]
    public async Task Save_WithValidValues_WritesAllThreeKeys()
    {
        var (vm, sender) = New();
        vm.AutoLockMinutesText = "20";
        vm.CategorisationConfidenceText = "0.5";
        vm.TransactionsPageSizeText = "60";

        await vm.SavePreferencesCommand.ExecuteAsync(null);

        await sender.Received(1).Send(Arg.Is<SaveSettingCommand>(c => c.Key == UserPreferenceKeys.AutoLockIdleMinutes && c.Value == "20"), Arg.Any<CancellationToken>());
        await sender.Received(1).Send(Arg.Is<SaveSettingCommand>(c => c.Key == UserPreferenceKeys.CategorisationConfidenceThreshold && c.Value == "0.5"), Arg.Any<CancellationToken>());
        await sender.Received(1).Send(Arg.Is<SaveSettingCommand>(c => c.Key == UserPreferenceKeys.TransactionsPageSize && c.Value == "60"), Arg.Any<CancellationToken>());
        vm.PreferencesFailed.Should().BeFalse();
        vm.PreferencesMessage.Should().Be("Preferences saved.");
    }

    [Fact]
    public async Task Save_WithOneOutOfRangeValue_WritesNothing_AndShowsALocalisedRefusal()
    {
        var (vm, sender) = New();
        vm.AutoLockMinutesText = "20";
        vm.CategorisationConfidenceText = "7";
        vm.TransactionsPageSizeText = "60";

        await vm.SavePreferencesCommand.ExecuteAsync(null);

        await sender.DidNotReceive().Send(Arg.Any<SaveSettingCommand>(), Arg.Any<CancellationToken>());
        vm.PreferencesFailed.Should().BeTrue();
        vm.PreferencesMessage.Should().Be("Enter a number from 0 to 1, for example 0.3.");
    }

    [Fact]
    public async Task Save_WhenTheWriteThrows_ShowsAGenericSentence_NotTheExceptionText()
    {
        var (vm, sender) = New();
        sender.Send(Arg.Any<SaveSettingCommand>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException(@"boom at C:\Users\camilo\vault.db"));
        vm.AutoLockMinutesText = "20";
        vm.CategorisationConfidenceText = "0.5";
        vm.TransactionsPageSizeText = "60";

        await vm.SavePreferencesCommand.ExecuteAsync(null);

        vm.PreferencesFailed.Should().BeTrue();
        vm.PreferencesMessage.Should().Be("Preferences could not be saved.").And.NotContain("camilo");
    }
}
