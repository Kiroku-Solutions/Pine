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

    /// <summary>
    /// TD-11: set by <c>MainViewModel</c> when it navigates here because the vault just locked
    /// (idle timeout, manual lock, or the app being suspended), so this screen can say why the
    /// user is looking at it instead of the shell they were just using -- otherwise a sudden idle
    /// lock reads as a crash. Empty on every other route into this screen (first run, "create new
    /// vault", the normal vault picker).
    /// </summary>
    [ObservableProperty]
    public partial string? LockNotice { get; set; }

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
