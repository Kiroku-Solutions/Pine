using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Pine.Ui.Avalonia.Services;

namespace Pine.Ui.Avalonia.ViewModels;

public partial class MainViewModel : ObservableObject, INavigationService
{
    private readonly IServiceProvider _serviceProvider;

    [ObservableProperty]
    public partial ObservableObject CurrentPage { get; set; } = null!;

    public MainViewModel(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// Synchronous entry point for the app lifetime. Kicks off the async
    /// bootstrap without an <c>async void</c> so exceptions stay observable.
    /// </summary>
    public void Initialize() => _ = InitializeAsync();

    private async Task InitializeAsync()
    {
        try
        {
            var vaultManager = _serviceProvider.GetRequiredService<Pine.Application.Security.VaultManager>();
            var vaults = await vaultManager.GetVaultsAsync();

            if (vaults.Count > 0)
            {
                NavigateTo<VaultSelectionViewModel>();
            }
            else
            {
                NavigateTo<SetupViewModel>();
            }
        }
        catch (Exception)
        {
            // Fail safe: fall back to first-run setup rather than crashing startup.
            NavigateTo<SetupViewModel>();
        }
    }

    public void NavigateTo<TViewModel>() where TViewModel : ObservableObject
    {
        CurrentPage = _serviceProvider.GetRequiredService<TViewModel>();
    }

    public void NavigateTo<TViewModel>(Action<TViewModel> initialize) where TViewModel : ObservableObject
    {
        var vm = _serviceProvider.GetRequiredService<TViewModel>();
        initialize?.Invoke(vm);
        CurrentPage = vm;
    }
}
