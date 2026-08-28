using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Pine.Application.Security;
using Pine.Ui.Avalonia.Services;

namespace Pine.Ui.Avalonia.ViewModels;

public partial class MainViewModel : ObservableObject, INavigationService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IVaultSession _vaultSession;
    private readonly IVaultAutoLockService _autoLockService;

    [ObservableProperty]
    public partial ObservableObject CurrentPage { get; set; } = null!;

    public MainViewModel(
        IServiceProvider serviceProvider,
        IVaultSession vaultSession,
        IVaultAutoLockService autoLockService)
    {
        _serviceProvider = serviceProvider;
        _vaultSession = vaultSession;
        _autoLockService = autoLockService;

        // TD-11: MainViewModel is the one long-lived object that hosts every screen (the shell
        // included), so it is the right place to react to a lock no matter which screen was
        // showing when it happened. Subscribed once, for the app's lifetime -- MainViewModel is a
        // singleton in every composition root and is never disposed before the process exits.
        _vaultSession.Locked += OnVaultLocked;
    }

    /// <summary>
    /// Runs on whatever thread locked the session -- for an idle lock, that is the auto-lock
    /// watchdog's timer thread, never the UI thread (see <see cref="IVaultSession.Locked"/>).
    /// <see cref="Dispatcher.UIThread"/> is the only safe way to touch <see cref="CurrentPage"/>
    /// or any view model from here; the Application layer cannot do this marshalling itself
    /// because it must not reference Avalonia.
    /// </summary>
    private void OnVaultLocked(object? sender, VaultLockedEventArgs e) =>
        Dispatcher.UIThread.Post(() => HandleVaultLocked(e.Reason));

    private void HandleVaultLocked(VaultLockReason reason)
    {
        // Nothing left to watch until the vault is unlocked again. CheckNow() already no-ops while
        // locked, so this is purely to stop polling a session that has nothing to report -- not a
        // correctness requirement, just not wasting a timer.
        _autoLockService.StopMonitoring();

        // The process is on its way out; there is nobody left to read a navigation change, and
        // touching CurrentPage during shutdown risks racing the window tearing itself down.
        if (reason == VaultLockReason.Shutdown)
        {
            return;
        }

        // A lock that fires while the shell isn't even showing (the defensive re-lock in the
        // catch blocks of UnlockViewModel/SetupViewModel, for instance) is already on the right
        // screen; forcing a fresh navigation there would just interrupt whatever the user is
        // doing to recover.
        if (CurrentPage is not ShellViewModel shell)
        {
            return;
        }

        // The key is gone; nothing that was showing decrypted vault data may survive it. Dropping
        // the shell's own inner page (its Dashboard/Transactions/etc. child, which is what
        // actually holds the loaded data) matters most on the composition roots where
        // ShellViewModel is registered as a Singleton rather than Transient (see the DI notes in
        // Pine.Ui.Macos/Web/Android/Ios Program.cs) -- there, nothing else would ever drop that
        // reference for us.
        shell.CurrentPage = null!;

        var message = reason switch
        {
            VaultLockReason.IdleTimeout =>
                $"Pine locked your vault after {FormatIdleTimeout(_autoLockService.IdleTimeout)} of inactivity.",
            VaultLockReason.ApplicationSuspended =>
                "Pine locked your vault because the app was moved to the background.",
            // UserRequested: the manual lock button, or a lock the user just triggered themselves.
            // Told plainly rather than left silent so this screen never reads as a crash.
            _ => "Your vault was locked.",
        };

        NavigateTo<VaultSelectionViewModel>(vm => vm.LockNotice = message);
    }

    private static string FormatIdleTimeout(TimeSpan timeout)
    {
        var minutes = (int)Math.Round(timeout.TotalMinutes);

        return minutes >= 1
            ? $"{minutes} minute{(minutes == 1 ? string.Empty : "s")}"
            : $"{(int)Math.Round(timeout.TotalSeconds)} seconds";
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
        ArmAutoLockIfEnteringShell<TViewModel>();
    }

    public void NavigateTo<TViewModel>(Action<TViewModel> initialize) where TViewModel : ObservableObject
    {
        var vm = _serviceProvider.GetRequiredService<TViewModel>();
        initialize?.Invoke(vm);
        CurrentPage = vm;
        ArmAutoLockIfEnteringShell<TViewModel>();
    }

    /// <summary>
    /// TD-11: the shell is the only screen that can show decrypted vault data, so arriving there is
    /// exactly the moment the idle watchdog needs to be watching. <c>typeof(TViewModel)</c> is
    /// checked against the generic argument rather than the resolved instance so this works
    /// identically for both <c>NavigateTo</c> overloads without inspecting <see cref="CurrentPage"/>.
    /// </summary>
    /// <remarks>
    /// Arming here rather than in every caller of <c>NavigateTo&lt;ShellViewModel&gt;()</c>
    /// (<c>UnlockViewModel</c>, <c>SetupViewModel</c>) means neither of those needs to know the
    /// watchdog exists. Calling <see cref="IVaultAutoLockService.StartMonitoring"/> on an
    /// already-armed watchdog is a documented no-op, so this is safe to run on every navigation,
    /// not only the first one into the shell.
    /// </remarks>
    private void ArmAutoLockIfEnteringShell<TViewModel>()
    {
        if (typeof(TViewModel) == typeof(ShellViewModel))
        {
            _autoLockService.StartMonitoring();
        }
    }
}
