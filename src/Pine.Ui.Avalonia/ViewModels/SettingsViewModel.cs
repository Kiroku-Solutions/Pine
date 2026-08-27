using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediatR;
using Pine.Application.Settings.Commands;
using Pine.Application.Settings.Queries;
using Pine.Ui.Avalonia.Utils;

namespace Pine.Ui.Avalonia.ViewModels;

/// <summary>
/// Appearance and vault settings page.
/// </summary>
/// <remarks>
/// Depends on <see cref="ISender"/> only, never on <c>ISettingRepository</c> (TD-01). The
/// repository is Scoped; this view model outlives any single request and its <c>LockVault</c>
/// command means the user can close one vault and open another without restarting the process, so
/// holding a Scoped repository would pin the FIRST vault's <c>PineDbContext</c> - its path and its
/// encryption key - for the rest of the session. The mediator is a singleton that opens a fresh
/// scope per request, and it is also what commits the writes: repositories no longer flush.
/// </remarks>
public partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty]
    public partial int SelectedThemeIndex { get; set; }

    [ObservableProperty]
    public partial int SelectedPaletteIndex { get; set; }

    [ObservableProperty]
    public partial int SelectedLanguageIndex { get; set; }

    /// <summary>Key of the persisted UI language setting.</summary>
    public const string LanguageSettingKey = "Appearance.Language";

    /// <summary>Accent palettes offered in the appearance picker.</summary>
    public IReadOnlyList<ThemePalette> Palettes { get; } = ThemeManager.Palettes;

    private readonly ISender _sender;
    private bool _isInitializing = true;

    public SettingsViewModel(ISender sender)
    {
        ArgumentNullException.ThrowIfNull(sender);
        _sender = sender;

        // Default values
        SelectedThemeIndex = 2; // System
        SelectedPaletteIndex = ThemeManager.CurrentPaletteIndex;
        SelectedLanguageIndex = 0; // English

        _ = LoadSettingsAsync();
    }

    private async Task LoadSettingsAsync()
    {
        var themeSetting = await _sender.Send(new GetSettingQuery(ThemeManager.VariantSettingKey));
        if (themeSetting != null && int.TryParse(themeSetting, out int themeIndex))
        {
            SelectedThemeIndex = themeIndex;
            ThemeManager.ApplyVariant(themeIndex);
        }

        var paletteSetting = await _sender.Send(new GetSettingQuery(ThemeManager.PaletteSettingKey));
        if (paletteSetting != null)
        {
            ThemeManager.ApplyPalette(paletteSetting);
            SelectedPaletteIndex = ThemeManager.CurrentPaletteIndex;
        }

        var langSetting = await _sender.Send(new GetSettingQuery(LanguageSettingKey));
        if (langSetting != null && int.TryParse(langSetting, out int langIndex))
        {
            SelectedLanguageIndex = langIndex;
        }

        _isInitializing = false;
    }

    partial void OnSelectedThemeIndexChanged(int value)
    {
        if (_isInitializing) return;
        ThemeManager.ApplyVariant(value);
        _ = SaveSettingAsync(ThemeManager.VariantSettingKey, value.ToString(CultureInfo.InvariantCulture));
    }

    partial void OnSelectedPaletteIndexChanged(int value)
    {
        if (_isInitializing) return;
        if (value < 0 || value >= ThemeManager.Palettes.Count) return;
        ThemeManager.ApplyPaletteIndex(value);
        _ = SaveSettingAsync(ThemeManager.PaletteSettingKey, ThemeManager.Palettes[value].Key);
    }

    partial void OnSelectedLanguageIndexChanged(int value)
    {
        if (_isInitializing) return;
        _ = SaveSettingAsync(LanguageSettingKey, value.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Persists one setting through the transactional command pipeline.
    /// </summary>
    private async Task SaveSettingAsync(string key, string value)
    {
        await _sender.Send(new SaveSettingCommand(key, value));
    }

#pragma warning disable CA1822

    [RelayCommand]
    private void ChangeMasterPassword()
    {
        // To be implemented via DialogService
    }

    [RelayCommand]
    private void ExportVault()
    {
        // To be implemented via DialogService/FilePicker
    }

    [RelayCommand]
    private void LockVault()
    {
        // Restart the app or navigate to UnlockAccountViewModel
    }
#pragma warning restore CA1822
}
