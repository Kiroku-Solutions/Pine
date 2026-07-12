using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pine.Application.Security;
using Pine.Ui.Avalonia.Services;

namespace Pine.Ui.Avalonia.ViewModels;

public partial class VaultSelectionViewModel : ObservableObject
{
    private readonly VaultManager _vaultManager;
    private readonly INavigationService _navigationService;

    [ObservableProperty]
    public partial ObservableCollection<VaultProfile> Vaults { get; set; } = new();

    public VaultSelectionViewModel(VaultManager vaultManager, INavigationService navigationService)
    {
        _vaultManager = vaultManager;
        _navigationService = navigationService;
        _ = LoadVaultsAsync();
    }

    private async Task LoadVaultsAsync()
    {
        var vaults = await _vaultManager.GetVaultsAsync();
        Vaults.Clear();
        foreach (var vault in vaults)
        {
            Vaults.Add(vault);
        }
    }

    [RelayCommand]
    private void CreateNewVault()
    {
        _navigationService.NavigateTo<SetupViewModel>();
    }

    [RelayCommand]
    private void SelectVault(VaultProfile vault)
    {
        if (vault != null)
        {
            _navigationService.NavigateTo<UnlockViewModel>(vm => vm.Initialize(vault));
        }
    }
}
