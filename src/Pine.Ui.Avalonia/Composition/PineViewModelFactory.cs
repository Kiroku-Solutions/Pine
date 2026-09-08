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
/// SINGLETON, AND HOLDING THE ROOT PROVIDER. Every page it creates is registered transient and
/// depends only on singletons (<c>IMediator</c> is the singleton scoping decorator; see
/// <c>ScopedMediator</c>), so nothing scoped escapes through this seam and
/// <c>ValidateScopes</c> stays honest. This is the same provider <c>MainViewModel</c> and
/// <c>ShellViewModel</c> used to hold themselves -- it has moved, not grown.
/// </para>
/// </remarks>
public sealed class PineViewModelFactory : IViewModelFactory
{
    private readonly IServiceProvider _provider;

    /// <summary>
    /// Creates the factory over a head's container.
    /// </summary>
    /// <param name="provider">The provider built by <see cref="PineCompositionRoot.Build"/>.</param>
    public PineViewModelFactory(IServiceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        _provider = provider;
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
    public DashboardViewModel CreateDashboard() => _provider.GetRequiredService<DashboardViewModel>();

    /// <inheritdoc />
    public TransactionsViewModel CreateTransactions() => _provider.GetRequiredService<TransactionsViewModel>();

    /// <inheritdoc />
    public BudgetsViewModel CreateBudgets() => _provider.GetRequiredService<BudgetsViewModel>();

    /// <inheritdoc />
    public SettingsViewModel CreateSettings() => _provider.GetRequiredService<SettingsViewModel>();

    /// <inheritdoc />
    public DiagnosticsViewModel CreateDiagnostics() => _provider.GetRequiredService<DiagnosticsViewModel>();

    /// <inheritdoc />
    public ScanReceiptViewModel CreateScanReceipt() => _provider.GetRequiredService<ScanReceiptViewModel>();

    /// <inheritdoc />
    public LiveScannerViewModel CreateLiveScanner() => _provider.GetRequiredService<LiveScannerViewModel>();

    /// <inheritdoc />
    public CropReceiptViewModel CreateCropReceipt() => _provider.GetRequiredService<CropReceiptViewModel>();

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
