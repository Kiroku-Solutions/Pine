using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Pine.Application.Security;
using Pine.Ui.Avalonia.Services;

namespace Pine.Ui.Avalonia.ViewModels;

public partial class ShellViewModel : ObservableObject, INavigationService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IVaultSession _vaultSession;

    [ObservableProperty]
    public partial ObservableObject CurrentPage { get; set; } = null!;

    public ShellViewModel(IServiceProvider serviceProvider, IVaultSession vaultSession)
    {
        _serviceProvider = serviceProvider;
        _vaultSession = vaultSession;

        // Start on the Dashboard
        NavigateToDashboard();
    }

    /// <summary>
    /// TD-11: before this there was no way for a user to lock deliberately -- only an idle timeout
    /// or a failed unlock attempt ever called <c>Lock()</c>. <see cref="MainViewModel"/> owns
    /// reacting to the resulting <see cref="IVaultSession.Locked"/> event (navigating out of the
    /// shell and explaining why), so this command's only job is to ask for the lock.
    /// </summary>
    [RelayCommand]
    private void LockVault()
    {
        _vaultSession.Lock(VaultLockReason.UserRequested);
    }

    public void NavigateTo<TViewModel>() where TViewModel : ObservableObject
    {
        CurrentPage = _serviceProvider.GetRequiredService<TViewModel>();
        
        UpdateActiveStates<TViewModel>();
    }

    public void NavigateTo<TViewModel>(Action<TViewModel> initialize) where TViewModel : ObservableObject
    {
        var vm = _serviceProvider.GetRequiredService<TViewModel>();
        initialize?.Invoke(vm);
        CurrentPage = vm;
        
        UpdateActiveStates<TViewModel>();
    }

    private void UpdateActiveStates<TViewModel>()
    {
        IsDashboardActive = typeof(TViewModel) == typeof(DashboardViewModel);
        IsTransactionsActive = typeof(TViewModel) == typeof(TransactionsViewModel);
        IsBudgetsActive = typeof(TViewModel) == typeof(BudgetsViewModel);
        IsDiagnosticsActive = typeof(TViewModel) == typeof(DiagnosticsViewModel);
        IsScanReceiptActive = typeof(TViewModel) == typeof(ScanReceiptViewModel);
        IsLiveScannerActive = typeof(TViewModel) == typeof(LiveScannerViewModel);
        IsSettingsActive = typeof(TViewModel) == typeof(SettingsViewModel);
    }

    [ObservableProperty]
    public partial bool IsDashboardActive { get; set; }

    [ObservableProperty]
    public partial bool IsTransactionsActive { get; set; }

    [ObservableProperty]
    public partial bool IsBudgetsActive { get; set; }

    [ObservableProperty]
    public partial bool IsDiagnosticsActive { get; set; }

    [ObservableProperty]
    public partial bool IsScanReceiptActive { get; set; }

    [ObservableProperty]
    public partial bool IsLiveScannerActive { get; set; }

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
    private void NavigateToBudgets()
    {
        NavigateTo<BudgetsViewModel>(vm => _ = vm.InitializeAsync());
    }

    [RelayCommand]
    private void NavigateToSettings()
    {
        NavigateTo<SettingsViewModel>();
    }

    /// <summary>
    /// Opens the diagnostics panel and asks it to read.
    /// </summary>
    /// <remarks>
    /// TD-15: this used the parameterless overload, which was correct while every value on that
    /// page was a constant fabricated in its constructor. Now that the panel measures what it
    /// shows, navigation has to kick the read off -- otherwise the page opens reading "Not read
    /// yet" in every card until the user finds the Refresh button. Same idiom as
    /// <see cref="NavigateToBudgets"/>: fire the load, do not await it, so navigation stays
    /// synchronous and the page paints immediately.
    /// </remarks>
    [RelayCommand]
    private void NavigateToDiagnostics()
    {
        NavigateTo<DiagnosticsViewModel>(vm => _ = vm.RefreshAsync());
    }

    [RelayCommand]
    private void NavigateToScanReceipt()
    {
        NavigateTo<ScanReceiptViewModel>();
    }

    [RelayCommand]
    private void NavigateToLiveScanner()
    {
        NavigateTo<LiveScannerViewModel>();
    }
}
