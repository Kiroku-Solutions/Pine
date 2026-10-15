using System;
using System.Globalization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pine.Application.Settings;
using Pine.Application.Settings.Commands;
using Pine.Core.Common;
using Pine.Ui.Avalonia.Diagnostics;
using Pine.Ui.Avalonia.Localization;

namespace Pine.Ui.Avalonia.ViewModels;

// TD-119: the three operative preferences (idle auto-lock, categorisation confidence, page size) as
// editable fields. Each is saved through SaveSettingCommand, whose validator refuses an out-of-range
// value with a localised sentence; nothing is stored until all three are valid.
public partial class SettingsViewModel
{
    private LocalizedMessageSlot? _preferencesMessageSlot;

    private LocalizedMessageSlot PreferencesMessageSlot
        => _preferencesMessageSlot ??= new LocalizedMessageSlot(_localizer, text => PreferencesMessage = text);

    /// <summary>Idle auto-lock, whole minutes, as typed.</summary>
    [ObservableProperty]
    public partial string AutoLockMinutesText { get; set; } = string.Empty;

    /// <summary>Categorisation confidence threshold, 0 to 1, as typed (invariant culture).</summary>
    [ObservableProperty]
    public partial string CategorisationConfidenceText { get; set; } = string.Empty;

    /// <summary>Transactions per page, as typed.</summary>
    [ObservableProperty]
    public partial string TransactionsPageSizeText { get; set; } = string.Empty;

    /// <summary>The outcome sentence for saving preferences, in the active language.</summary>
    [ObservableProperty]
    public partial string? PreferencesMessage { get; set; }

    /// <summary>Whether <see cref="PreferencesMessage"/> reports a failure.</summary>
    [ObservableProperty]
    public partial bool PreferencesFailed { get; set; }

    /// <summary>Loads the effective preferences into the fields.</summary>
    public async Task LoadPreferencesAsync()
    {
        try
        {
            var preferences = await _sender.Send(new GetUserPreferencesQuery());
            AutoLockMinutesText = ((int)Math.Round(preferences.AutoLockIdleTimeout.TotalMinutes)).ToString(CultureInfo.InvariantCulture);
            CategorisationConfidenceText = preferences.CategorisationConfidenceThreshold.ToString("0.##", CultureInfo.InvariantCulture);
            TransactionsPageSizeText = preferences.TransactionsPageSize.ToString(CultureInfo.InvariantCulture);
        }
        catch (Exception ex)
        {
            PreferencesFailed = true;
            PreferencesMessageSlot.Set(UserFacingError.Report(
                _logger, ex, "load-preferences", new UserFacingMessage("Settings.Preferences.Failure")));
        }
    }

    /// <summary>Releases the message slot.</summary>
    internal void DisposePreferences() => _preferencesMessageSlot?.Dispose();

    [RelayCommand]
    private async Task SavePreferencesAsync()
    {
        PreferencesFailed = false;
        PreferencesMessageSlot.Clear();

        try
        {
            (string Key, string Value)[] entries =
            [
                (UserPreferenceKeys.AutoLockIdleMinutes, AutoLockMinutesText.Trim()),
                (UserPreferenceKeys.CategorisationConfidenceThreshold, CategorisationConfidenceText.Trim()),
                (UserPreferenceKeys.TransactionsPageSize, TransactionsPageSizeText.Trim()),
            ];

            // Validate all three before writing any, so a refusal never leaves a half-saved set.
            foreach (var (key, value) in entries)
            {
                var errors = await new SaveSettingCommandValidator().ValidateAsync(new SaveSettingCommand(key, value));
                if (errors.Count > 0)
                {
                    PreferencesFailed = true;
                    PreferencesMessageSlot.Set(errors[0].UserFacingMessage);
                    return;
                }
            }

            foreach (var (key, value) in entries)
            {
                await _sender.Send(new SaveSettingCommand(key, value));
            }

            PreferencesMessageSlot.Set("Settings.Preferences.Success");
        }
        catch (Exception ex)
        {
            PreferencesFailed = true;
            PreferencesMessageSlot.Set(UserFacingError.Report(
                _logger, ex, "save-preferences", new UserFacingMessage("Settings.Preferences.Failure")));
        }
    }
}
