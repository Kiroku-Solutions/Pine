using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia;
using Avalonia.Styling;

namespace Pine.Ui.Avalonia.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty]
    public partial int SelectedThemeIndex { get; set; }

    [ObservableProperty]
    public partial int SelectedLanguageIndex { get; set; }

    private bool _isInitializing = true;

    public SettingsViewModel()
    {
        // Read the actual current theme instead of resetting it
        if (global::Avalonia.Application.Current != null)
        {
            var currentTheme = global::Avalonia.Application.Current.RequestedThemeVariant;
            if (currentTheme == ThemeVariant.Light)
                SelectedThemeIndex = 0;
            else if (currentTheme == ThemeVariant.Dark)
                SelectedThemeIndex = 1;
            else
                SelectedThemeIndex = 2;
        }
        else
        {
            SelectedThemeIndex = 2; // Default to System
        }
        
        // 0: English, 1: Spanish
        SelectedLanguageIndex = 0;

        _isInitializing = false;
    }

    partial void OnSelectedThemeIndexChanged(int value)
    {
        if (_isInitializing) return;
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
