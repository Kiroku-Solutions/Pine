using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pine.Core.Configuration;
using Pine.Core.Settings;
using Pine.Ui.Avalonia.Utils;

namespace Pine.Ui.Avalonia.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty]
    public partial int SelectedThemeIndex { get; set; }

    [ObservableProperty]
    public partial int SelectedPaletteIndex { get; set; }

    [ObservableProperty]
    public partial int SelectedLanguageIndex { get; set; }

    /// <summary>Accent palettes offered in the appearance picker.</summary>
    public IReadOnlyList<ThemePalette> Palettes { get; } = ThemeManager.Palettes;

    private readonly ISettingRepository _settingRepository;
    private bool _isInitializing = true;

    public SettingsViewModel(ISettingRepository settingRepository)
    {
        _settingRepository = settingRepository;

        // Default values
        SelectedThemeIndex = 2; // System
        SelectedPaletteIndex = ThemeManager.CurrentPaletteIndex;
        SelectedLanguageIndex = 0; // English

        _ = LoadSettingsAsync();
    }

    private async Task LoadSettingsAsync()
    {
        var themeSetting = await _settingRepository.GetAsync(ThemeManager.VariantSettingKey);
        if (themeSetting != null && int.TryParse(themeSetting.Value, out int themeIndex))
        {
            SelectedThemeIndex = themeIndex;
            ThemeManager.ApplyVariant(themeIndex);
        }

        var paletteSetting = await _settingRepository.GetAsync(ThemeManager.PaletteSettingKey);
        if (paletteSetting != null)
        {
            ThemeManager.ApplyPalette(paletteSetting.Value);
            SelectedPaletteIndex = ThemeManager.CurrentPaletteIndex;
        }

        var langSetting = await _settingRepository.GetAsync("Appearance.Language");
        if (langSetting != null && int.TryParse(langSetting.Value, out int langIndex))
        {
            SelectedLanguageIndex = langIndex;
        }

        _isInitializing = false;
    }

    partial void OnSelectedThemeIndexChanged(int value)
    {
        if (_isInitializing) return;
        ThemeManager.ApplyVariant(value);
        _ = SaveSettingAsync(ThemeManager.VariantSettingKey, value.ToString());
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
        _ = SaveSettingAsync("Appearance.Language", value.ToString());
    }

    private async Task SaveSettingAsync(string key, string value)
    {
        var setting = Setting.Create(key, value);
        await _settingRepository.SaveAsync(setting);
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
