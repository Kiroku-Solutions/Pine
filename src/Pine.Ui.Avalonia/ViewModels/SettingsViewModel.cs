using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia;
using Avalonia.Styling;
using Pine.Core.Configuration;
using Pine.Core.Settings;

namespace Pine.Ui.Avalonia.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty]
    public partial int SelectedThemeIndex { get; set; }

    [ObservableProperty]
    public partial int SelectedLanguageIndex { get; set; }

    private readonly ISettingRepository _settingRepository;
    private bool _isInitializing = true;

    public SettingsViewModel(ISettingRepository settingRepository)
    {
        _settingRepository = settingRepository;
        
        // Default values
        SelectedThemeIndex = 2; // System
        SelectedLanguageIndex = 0; // English
        
        _ = LoadSettingsAsync();
    }

    private async Task LoadSettingsAsync()
    {
        var themeSetting = await _settingRepository.GetAsync("Appearance.Theme");
        if (themeSetting != null && int.TryParse(themeSetting.Value, out int themeIndex))
        {
            SelectedThemeIndex = themeIndex;
            ApplyTheme(themeIndex);
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
        ApplyTheme(value);
        _ = SaveSettingAsync("Appearance.Theme", value.ToString());
    }

    partial void OnSelectedLanguageIndexChanged(int value)
    {
        if (_isInitializing) return;
        _ = SaveSettingAsync("Appearance.Language", value.ToString());
    }

    private static void ApplyTheme(int value)
    {
        if (global::Avalonia.Application.Current == null) return;
        
        switch(value)
        {
            case 0: // Light
                global::Avalonia.Application.Current.RequestedThemeVariant = ThemeVariant.Light;
                break;
            case 1: // Dark
                global::Avalonia.Application.Current.RequestedThemeVariant = ThemeVariant.Dark;
                break;
            case 2: // System
                global::Avalonia.Application.Current.RequestedThemeVariant = ThemeVariant.Default;
                break;
        }
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
