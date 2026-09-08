using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Pine.Application.Security;
using Pine.Ui.Avalonia.Services;
using Pine.Ui.Avalonia.Diagnostics;

namespace Pine.Ui.Avalonia.ViewModels;

public partial class MainViewModel : ObservableObject, INavigationService
{
    private readonly IViewModelFactory _viewModels;
    private readonly IVaultSession _vaultSession;
    private readonly IVaultAutoLockService _autoLockService;

    /// <summary>
    /// The vault registry this screen consults at startup to decide between the picker and
    /// first-run setup.
    /// </summary>
    /// <remarks>
    /// INJECTED, NOT RESOLVED (TD-28). This used to be pulled out of the container inside
    /// <c>InitializeAsync</c>, which is the last thing that kept a service locator in this type
    /// once navigation stopped needing one. <c>VaultManager</c> is a singleton on every head, so
    /// taking it in the constructor is the same object at the same time -- it is only the reach
    /// that has gone.
    /// </remarks>
    private readonly VaultManager _vaultManager;

    [ObservableProperty]
    public partial ObservableObject CurrentPage { get; set; } = null!;

    public MainViewModel(
        IViewModelFactory viewModels,
        VaultManager vaultManager,
        IVaultSession vaultSession,
        IVaultAutoLockService autoLockService)
    {
        _viewModels = viewModels;
        _vaultManager = vaultManager;
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
        //
        // TD-34: this assignment now does more than drop a reference. It runs the shell's own
        // OnCurrentPageChanging hook, which unregisters that page from the messenger and disposes
        // it if it holds anything -- so a lock that happens while the live scanner is running also
        // puts the camera down. That is why it stays an assignment to the property and is not
        // "optimised" into a field write.
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

        var picker = _viewModels.CreateVaultSelection();
        picker.LockNotice = message;
        NavigateTo(picker);
    }

    private static string FormatIdleTimeout(TimeSpan timeout)
    {
        var minutes = (int)Math.Round(timeout.TotalMinutes);

        return minutes >= 1
            ? $"{minutes} minute{(minutes == 1 ? string.Empty : "s")}"
            : $"{(int)Math.Round(timeout.TotalSeconds)} seconds";
    }

    /// <summary>
    /// Synchronous entry point for the app lifetime, called once by <c>App</c>.
    /// </summary>
    /// <remarks>
    /// THE OLD COMMENT HERE HAD IT EXACTLY BACKWARDS, AND THE CORRECTION IS THE POINT (TD-35). It
    /// read "kicks off the async bootstrap without an <c>async void</c> so exceptions stay
    /// observable". A discarded <see cref="Task"/> is the LESS observable of the two: an
    /// <c>async void</c> at least rethrows on the captured synchronisation context, where a global
    /// handler could see it, whereas a discarded faulted task is collected in silence and this
    /// process installs no <c>UnobservedTaskException</c> handler at all. It is routed through
    /// <see cref="ObservedTask"/> now, which is what the old comment claimed the discard was doing.
    /// </remarks>
    public void Initialize() =>
        ObservedTask.Run(
            InitializeAsync,
            null,
            "app-bootstrap",
            "Pine could not work out which screen to open. The vault picker is shown instead.");

    private async Task InitializeAsync()
    {
        try
        {
            var vaults = await _vaultManager.GetVaultsAsync();

            if (vaults.Count > 0)
            {
                NavigateTo(_viewModels.CreateVaultSelection());
            }
            else
            {
                NavigateTo(_viewModels.CreateSetup());
            }
        }
        catch (Exception)
        {
            // Fail safe: fall back to first-run setup rather than crashing startup.
            NavigateTo(_viewModels.CreateSetup());
        }
    }

    /// <inheritdoc />
    public void NavigateTo(ObservableObject page)
    {
        ArgumentNullException.ThrowIfNull(page);

        CurrentPage = page;
        ArmAutoLockIfEnteringShell(page);
    }

    /// <summary>
    /// Retires the page being replaced: unregisters it from the messenger and disposes it (TD-34).
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY THE ONE-PARAMETER HOOK. CommunityToolkit.Mvvm 8.4.2 generates both
    /// <c>OnCurrentPageChanging(value)</c> and <c>OnCurrentPageChanging(oldValue, newValue)</c> for
    /// an <c>[ObservableProperty]</c> partial property, but the generated setter invokes the second
    /// one as <c>OnCurrentPageChanging(default, value)</c>: the "old value" it hands you is always
    /// <see langword="null"/>. The one-parameter hook runs before the backing field is assigned, so
    /// <see cref="CurrentPage"/> read here IS the outgoing page.
    /// </para>
    /// <para>
    /// This sits on the property rather than inside <see cref="NavigateTo"/> because the vault-lock
    /// path assigns <c>shell.CurrentPage</c> directly and never comes through <c>NavigateTo</c>; a
    /// retirement step that lived in the navigator would simply be skipped there.
    /// </para>
    /// </remarks>
    partial void OnCurrentPageChanging(ObservableObject value) => PageActivation.Deactivate(CurrentPage);

    /// <summary>
    /// Registers the incoming page with the messenger before <see cref="NavigateTo"/> returns
    /// (TD-34). See <c>PageActivation</c> for why the timing cannot be relaxed to view load.
    /// </summary>
    partial void OnCurrentPageChanged(ObservableObject value) => PageActivation.Activate(value);

    /// <summary>
    /// TD-11: the shell is the only screen that can show decrypted vault data, so arriving there is
    /// exactly the moment the idle watchdog needs to be watching. The check is on the runtime
    /// instance now (TD-28): there is one <c>NavigateTo</c> and it is handed the page itself, so
    /// there is no generic argument left to test and no reason to want one.
    /// </summary>
    /// <remarks>
    /// Arming here rather than in every caller that navigates into the shell
    /// (<c>UnlockViewModel</c>, <c>SetupViewModel</c>) means neither of those needs to know the
    /// watchdog exists. Calling <see cref="IVaultAutoLockService.StartMonitoring"/> on an
    /// already-armed watchdog is a documented no-op, so this is safe to run on every navigation,
    /// not only the first one into the shell.
    /// </remarks>
    private void ArmAutoLockIfEnteringShell(ObservableObject page)
    {
        if (page is ShellViewModel)
        {
            _autoLockService.StartMonitoring();
        }
    }
}
