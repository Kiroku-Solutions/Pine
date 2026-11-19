using System;
using Microsoft.Extensions.DependencyInjection;
using Pine.Ui.Avalonia.Services;
using Pine.Ui.Avalonia.ViewModels;
using Pine.Ui.Avalonia.ViewModels.Importing;

namespace Pine.Ui.Avalonia.Composition;

/// <summary>
/// The container-backed <see cref="IViewModelFactory"/> (TD-28).
/// </summary>
/// <remarks>
/// <para>
/// THIS TYPE HOLDS AN <c>IServiceProvider</c> ON PURPOSE, AND IT IS THE ONLY VIEW-MODEL-FACING TYPE
/// THAT MAY. A container has to be reached somewhere; the point of TD-28 was never to abolish that,
/// it was to stop the reach happening from inside a view model, where it is invisible to the type
/// system and to any test that does not click the button. Here it happens behind a named method per
/// page, in <c>Composition/</c>, which is the assembly's one declared home for the container.
/// </para>
/// <para>
/// SINGLETON, AND HOLDING THE ROOT PROVIDER. Every page it creates is either registered transient
/// or, for the six that take an argument no container can supply (a callback, or the shell they are
/// shown in), built with <c>ActivatorUtilities</c> -- and either way depends only on singletons
/// (<c>IMediator</c> is the singleton scoping decorator; see <c>ScopedMediator</c>), so nothing
/// scoped escapes through this seam and <c>ValidateScopes</c> stays honest. This is the same
/// provider <c>MainViewModel</c> and <c>ShellViewModel</c> used to hold themselves -- it has moved,
/// not grown.
/// </para>
/// </remarks>
public sealed class PineViewModelFactory : IViewModelFactory
{
    private readonly IServiceProvider _provider;
    private readonly PineHeadCapabilities _capabilities;

    /// <summary>
    /// Creates the factory over a head's container.
    /// </summary>
    /// <param name="provider">The provider built by <see cref="PineCompositionRoot.Build"/>.</param>
    /// <param name="capabilities">
    /// What this head can serve. Consulted by the three receipt methods, which are no longer gated
    /// by a container registration (TD-121).
    /// </param>
    public PineViewModelFactory(IServiceProvider provider, PineHeadCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(capabilities);

        _provider = provider;
        _capabilities = capabilities;
    }

    /// <inheritdoc />
    public VaultSelectionViewModel CreateVaultSelection() => _provider.GetRequiredService<VaultSelectionViewModel>();

    /// <inheritdoc />
    public SetupViewModel CreateSetup() => _provider.GetRequiredService<SetupViewModel>();

    /// <inheritdoc />
    public UnlockViewModel CreateUnlock() => _provider.GetRequiredService<UnlockViewModel>();

    /// <inheritdoc />
    public ShellViewModel CreateShell() => _provider.GetRequiredService<ShellViewModel>();

    /// <inheritdoc />
    public DashboardViewModel CreateDashboard(IShellNavigator shell)
    {
        ArgumentNullException.ThrowIfNull(shell);

        // On every head, unlike the receipt pages: the dashboard exists everywhere, and it hides its
        // own Scan Receipt button where the head has no receipt pipeline (item 1.4).
        return ActivatorUtilities.CreateInstance<DashboardViewModel>(_provider, shell);
    }

    /// <inheritdoc />
    public TransactionsViewModel CreateTransactions() => _provider.GetRequiredService<TransactionsViewModel>();

    /// <inheritdoc />
    public BudgetsViewModel CreateBudgets() => _provider.GetRequiredService<BudgetsViewModel>();

    /// <inheritdoc />
    public RulesViewModel CreateRules() => _provider.GetRequiredService<RulesViewModel>();

    /// <inheritdoc />
    public SyncConflictsViewModel CreateSyncConflicts() => _provider.GetRequiredService<SyncConflictsViewModel>();

    /// <inheritdoc />
    public SyncPairingViewModel CreateSyncPairing() => _provider.GetRequiredService<SyncPairingViewModel>();

    /// <inheritdoc />
    public SyncRunViewModel CreateSyncRun() => _provider.GetRequiredService<SyncRunViewModel>();

    /// <inheritdoc />
    public SettingsViewModel CreateSettings() => _provider.GetRequiredService<SettingsViewModel>();

    /// <inheritdoc />
    public DiagnosticsViewModel CreateDiagnostics() => _provider.GetRequiredService<DiagnosticsViewModel>();

    // THE THREE THAT TAKE THEIR SHELL (TD-121). Built exactly like the callback-taking three below:
    // the caller supplies the one argument no container can know -- here, WHICH shell -- and every
    // other constructor parameter comes from the container. They are not registered, so the
    // container can no longer build one with the window's navigator in it, which is what it did.
    //
    // THE CAPABILITY CHECK IS EXPLICIT NOW, AND HAS TO BE. It used to be implicit: these pages were
    // registered only on a receipt-vision head, so GetRequiredService threw everywhere else. With no
    // registration, ActivatorUtilities would happily build the review page on Android -- every one
    // of its dependencies exists there -- and hand it a pipeline that refuses vision on first use.

    /// <inheritdoc />
    public ScanReceiptViewModel CreateScanReceipt(IShellNavigator shell) => CreateReceiptPage<ScanReceiptViewModel>(shell, _capabilities.HasReceiptScan);

    /// <inheritdoc />
    public LiveScannerViewModel CreateLiveScanner(IShellNavigator shell) => CreateReceiptPage<LiveScannerViewModel>(shell, _capabilities.HasReceiptVision);

    /// <inheritdoc />
    public CropReceiptViewModel CreateCropReceipt(IShellNavigator shell) => CreateReceiptPage<CropReceiptViewModel>(shell, _capabilities.HasReceiptVision);

    private TPage CreateReceiptPage<TPage>(IShellNavigator shell, bool available)
    {
        ArgumentNullException.ThrowIfNull(shell);

        if (!available)
        {
            throw new InvalidOperationException(
                $"The {_capabilities.Profile} head cannot run the pipeline behind {typeof(TPage).Name}. " +
                "The shell hides the buttons that lead here on such a head.");
        }

        return ActivatorUtilities.CreateInstance<TPage>(_provider, shell);
    }

    // THE THREE THAT TAKE A CALLBACK (TD-58). ActivatorUtilities.CreateInstance supplies the
    // caller's Action for the one parameter the container cannot serve and resolves EVERY other
    // constructor parameter from the container -- which is a real behaviour change, not just a
    // tidier call site. These three were previously built with `new` by their parent view model,
    // which passed the mediator it happened to hold and nothing else, so their optional
    // ILogger<T> parameter silently took its default of null and every UserFacingError.Report call
    // inside them recorded the underlying exception NOWHERE. They now get the head's real logger
    // (a dent in TD-104). The import wizard additionally stops being handed a hard-coded
    // TimeProvider.System: the container registers a TimeProvider, and it is now the one used.

    /// <inheritdoc />
    public AddTransactionViewModel CreateAddTransaction(Action onComplete)
    {
        ArgumentNullException.ThrowIfNull(onComplete);

        return ActivatorUtilities.CreateInstance<AddTransactionViewModel>(_provider, onComplete);
    }

    /// <inheritdoc />
    public EditTransactionViewModel CreateEditTransaction(Pine.Core.Transactions.Transaction transaction, Action onComplete)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(onComplete);

        return ActivatorUtilities.CreateInstance<EditTransactionViewModel>(_provider, transaction, onComplete);
    }

    /// <inheritdoc />
    public CreateAccountViewModel CreateCreateAccount(Action onComplete)
    {
        ArgumentNullException.ThrowIfNull(onComplete);

        return ActivatorUtilities.CreateInstance<CreateAccountViewModel>(_provider, onComplete);
    }

    /// <inheritdoc />
    public ImportWizardViewModel CreateImportWizard(Action onClose)
    {
        ArgumentNullException.ThrowIfNull(onClose);

        return ActivatorUtilities.CreateInstance<ImportWizardViewModel>(_provider, onClose);
    }
}
