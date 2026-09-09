using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pine.Application.Security;
using Pine.Ui.Avalonia.Composition;
using Pine.Ui.Avalonia.Services;

namespace Pine.Ui.Avalonia.ViewModels;

public partial class ShellViewModel : ObservableRecipient, INavigationService
{
    private readonly IViewModelFactory _viewModels;
    private readonly IVaultSession _vaultSession;

    /// <summary>
    /// Whether this head can serve the two receipt pages, and therefore whether their nav buttons
    /// exist (TD-85).
    /// </summary>
    /// <remarks>
    /// THE BUTTON AND THE REGISTRATION ARE NOW ONE DECISION. Before this, the sidebar listed all
    /// seven pages on all five heads while four of them registered between three and four of the
    /// view models, and every nav command resolves its page out of the container -- so
    /// "Scan Receipt" on Android was not a blank page, it was an exception on click. This property
    /// reads the same <see cref="PineHeadCapabilities"/> object that decided whether
    /// <c>PineCompositionRoot</c> registered the view models at all, so the two halves cannot
    /// disagree.
    /// </remarks>
    public bool HasReceiptVision { get; }

    [ObservableProperty]
    public partial ObservableObject CurrentPage { get; set; } = null!;

    public ShellViewModel(
        IViewModelFactory viewModels,
        IVaultSession vaultSession,
        PineHeadCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);

        _viewModels = viewModels;
        _vaultSession = vaultSession;
        HasReceiptVision = capabilities.HasReceiptVision;

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

    /// <inheritdoc />
    public void NavigateTo(ObservableObject page)
    {
        ArgumentNullException.ThrowIfNull(page);

        // Everything that retires the outgoing page and enlists the incoming one happens INSIDE
        // this assignment, in OnCurrentPageChanging/OnCurrentPageChanged below. It is written that
        // way so `shell.CurrentPage = null!` -- which MainViewModel's lock handler does directly,
        // without coming through here -- cannot skip it.
        CurrentPage = page;

        UpdateActiveStates(page);
    }

    /// <summary>
    /// Unregisters the page being replaced and releases whatever it owns (TD-34).
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE ONE-PARAMETER OVERLOAD IS THE ONLY USABLE ONE. CommunityToolkit.Mvvm 8.4.2 emits both
    /// <c>OnCurrentPageChanging(value)</c> and <c>OnCurrentPageChanging(oldValue, newValue)</c> for
    /// an <c>[ObservableProperty]</c> partial property, but it calls the two-parameter form as
    /// <c>OnCurrentPageChanging(default, value)</c> -- the old value is literally <c>default</c>,
    /// never the previous page. The one-parameter hook runs BEFORE the generated setter assigns the
    /// backing field, so reading <see cref="CurrentPage"/> here is the outgoing page, which is the
    /// value the two-parameter overload only pretends to offer.
    /// </para>
    /// <para>
    /// <see cref="CurrentPage"/> is declared non-nullable but is genuinely null both before the
    /// first navigation and after a vault lock, so this must tolerate it;
    /// <see cref="PageActivation.Deactivate"/> does.
    /// </para>
    /// </remarks>
    partial void OnCurrentPageChanging(ObservableObject value) => PageActivation.Deactivate(CurrentPage);

    /// <summary>
    /// Registers the page now showing, before <see cref="NavigateTo"/> returns (TD-34).
    /// </summary>
    /// <remarks>
    /// The timing is load-bearing, not incidental: <c>LiveScannerViewModel</c> navigates to the
    /// crop page and sends <c>ReceiptCapturedMessage</c> on the following statement. If activation
    /// were deferred to the view's <c>Loaded</c> event the recipient would not exist yet and the
    /// captured receipt would vanish silently.
    /// </remarks>
    partial void OnCurrentPageChanged(ObservableObject value) => PageActivation.Activate(value);

    /// <summary>
    /// The shell is both a page of <see cref="MainViewModel"/> and a navigator of its own, so
    /// deactivating it has to reach the page it is showing (TD-34).
    /// </summary>
    /// <remarks>
    /// Without this, locking the vault from a screen other than the one the lock handler happens to
    /// clear would leave the shell's inner Dashboard/Transactions page registered with the messenger
    /// and, for the live scanner, still holding the camera. Assigning <c>null!</c> rather than
    /// calling <see cref="PageActivation.Deactivate"/> directly is deliberate: it goes through the
    /// same changing hook every other page change uses, so there is exactly one retirement path,
    /// and it drops the reference to a view model that has just been disposed.
    /// </remarks>
    protected override void OnDeactivated()
    {
        CurrentPage = null!;

        base.OnDeactivated();
    }

    /// <summary>
    /// Lights the sidebar button belonging to the page now showing.
    /// </summary>
    /// <remarks>
    /// The seven comparisons were against <c>typeof(TViewModel)</c> when this method took a type
    /// argument (TD-28). They are pattern matches on the instance now, which is the same decision
    /// asked of the object rather than of a generic parameter -- and it is exactly as exact: these
    /// are sealed-in-practice page types with no derived variants, so nothing that was false here
    /// becomes true.
    /// </remarks>
    private void UpdateActiveStates(ObservableObject page)
    {
        IsDashboardActive = page is DashboardViewModel;
        IsTransactionsActive = page is TransactionsViewModel;
        IsBudgetsActive = page is BudgetsViewModel;
        IsRulesActive = page is RulesViewModel;
        IsDiagnosticsActive = page is DiagnosticsViewModel;
        IsScanReceiptActive = page is ScanReceiptViewModel;
        IsLiveScannerActive = page is LiveScannerViewModel;
        IsSettingsActive = page is SettingsViewModel;
    }

    [ObservableProperty]
    public partial bool IsDashboardActive { get; set; }

    [ObservableProperty]
    public partial bool IsTransactionsActive { get; set; }

    [ObservableProperty]
    public partial bool IsBudgetsActive { get; set; }

    /// <summary>Whether the rules screen is the current page.</summary>
    [ObservableProperty]
    public partial bool IsRulesActive { get; set; }

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
        NavigateTo(_viewModels.CreateDashboard());
    }

    [RelayCommand]
    private void NavigateToTransactions()
    {
        NavigateTo(_viewModels.CreateTransactions());
    }

    [RelayCommand]
    private void NavigateToBudgets()
    {
        // No load kicked off here any more (TD-35). Budgets implements IAsyncInitializable, so
        // PageActivation starts it through ObservedTask when this page becomes current -- one
        // initialisation path for every page instead of a per-navigation-command discard.
        var budgets = _viewModels.CreateBudgets();
        NavigateTo(budgets);
    }

    /// <summary>Opens the rules screen.</summary>
    /// <remarks>
    /// This command is new, and the button that calls it was already in the sidebar -- disabled,
    /// dimmed, with a "Coming soon" tooltip. That was honest while nothing evaluated a rule; it is
    /// now a page.
    /// </remarks>
    [RelayCommand]
    private void NavigateToRules()
    {
        NavigateTo(_viewModels.CreateRules());
    }

    [RelayCommand]
    private void NavigateToSettings()
    {
        NavigateTo(_viewModels.CreateSettings());
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
        // Same as NavigateToBudgets: the page declares its own initialisation and PageActivation
        // runs it (TD-35).
        var diagnostics = _viewModels.CreateDiagnostics();
        NavigateTo(diagnostics);
    }

    [RelayCommand]
    private void NavigateToScanReceipt()
    {
        NavigateTo(_viewModels.CreateScanReceipt());
    }

    [RelayCommand]
    private void NavigateToLiveScanner()
    {
        NavigateTo(_viewModels.CreateLiveScanner());
    }
}
