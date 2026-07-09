using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Pine.Ui.Avalonia.Services;

namespace Pine.Ui.Avalonia.ViewModels;

public partial class ShellViewModel : ObservableObject, INavigationService
{
    private readonly IServiceProvider _serviceProvider;

    [ObservableProperty]
    public partial ObservableObject CurrentPage { get; set; } = null!;

    public ShellViewModel(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        
        // Start on the Dashboard
        NavigateToDashboard();
    }

    public void NavigateTo<TViewModel>() where TViewModel : ObservableObject
    {
        CurrentPage = _serviceProvider.GetRequiredService<TViewModel>();
        
        IsDashboardActive = typeof(TViewModel) == typeof(DashboardViewModel);
        IsTransactionsActive = typeof(TViewModel) == typeof(TransactionsViewModel);
        IsSettingsActive = typeof(TViewModel) == typeof(SettingsViewModel);
    }

    [ObservableProperty]
    public partial bool IsDashboardActive { get; set; }

    [ObservableProperty]
    public partial bool IsTransactionsActive { get; set; }

    [ObservableProperty]
    public partial bool IsSettingsActive { get; set; }

    [RelayCommand]
    private void NavigateToDashboard()
    {
        NavigateTo<DashboardViewModel>();
    }

    [RelayCommand]
    private void NavigateToTransactions()
    {
        NavigateTo<TransactionsViewModel>();
    }

    [RelayCommand]
    private void NavigateToSettings()
    {
        NavigateTo<SettingsViewModel>();
    }
}
