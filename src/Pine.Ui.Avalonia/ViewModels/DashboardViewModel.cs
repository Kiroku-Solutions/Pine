using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediatR;
using Microsoft.Extensions.Logging;
using Pine.Ui.Avalonia.Diagnostics;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.Services;
using Pine.Ui.Avalonia.ViewModels.Widgets;

namespace Pine.Ui.Avalonia.ViewModels;

/// <summary>
/// The dashboard: a greeting, three actions, and the tiles.
/// </summary>
/// <remarks>
/// <para>
/// THIS PAGE READS NOTHING OF ITS OWN ANY MORE (post-audit plan item 1.3). It used to load the
/// accounts into an <c>Accounts</c> collection and compute a <c>TotalBalance</c> -- and no view bound
/// either. The total was correct and five tests pinned it; the user never saw it. The account read ran
/// on every visit, on every <c>DataChangedMessage</c>, and after every dialog closed, and its only
/// visible effect was an error sentence if it failed. The total moved into a tile, where it is on
/// screen; the read went with it. Each tile reads, refreshes and reports its own failure.
/// </para>
/// <para>
/// WHICH IS ALSO WHY AN IMPORT NOW REFRESHES THE DASHBOARD. The import wizard's close callback re-ran
/// that unseen account read and nothing else, and the wizard sent no <c>DataChangedMessage</c>, so
/// after importing a statement every tile kept its old figures. The wizard broadcasts now, like every
/// other write path, and the tiles answer the broadcast.
/// </para>
/// </remarks>
public partial class DashboardViewModel : ObservableRecipient, IAsyncInitializable, IDisposable
{
    private readonly IMediator _mediator;
    private readonly ILocalizer _localizer;
    private readonly LocalizedMessageSlot _greeting;

    /// <summary>
    /// Builds the two dialogs this page opens (TD-28/TD-58).
    /// </summary>
    /// <remarks>
    /// Both dialogs used to be built with <c>new</c> here, so their optional <c>ILogger&lt;T&gt;</c>
    /// defaulted to <see langword="null"/> on every real run and the import wizard was handed a
    /// hard-coded <c>TimeProvider.System</c>. Going through the factory fixes both by construction.
    /// </remarks>
    private readonly IViewModelFactory _viewModels;

    private readonly ILogger<DashboardViewModel>? _logger;

    /// <summary>The page heading.</summary>
    /// <remarks>
    /// It was the literal "Welcome to Pine" -- the largest text on the page, in English on a Spanish
    /// dashboard. It is a live key now, re-rendered on a language switch.
    /// </remarks>
    [ObservableProperty]
    public partial string Greeting { get; set; } = string.Empty;

    /// <summary>The five tiles, in the order the grid shows them.</summary>
    public ObservableCollection<IDashboardWidgetViewModel> Widgets { get; } = new();

    [ObservableProperty]
    public partial bool IsCreateAccountDialogVisible { get; set; }

    [ObservableProperty]
    public partial CreateAccountViewModel? CreateAccountVm { get; set; }

    [ObservableProperty]
    public partial bool IsImportWizardVisible { get; set; }

    [ObservableProperty]
    public partial Pine.Ui.Avalonia.ViewModels.Importing.ImportWizardViewModel? ImportWizardVm { get; set; }

    /// <param name="mediator">The request pipeline, handed to the tiles.</param>
    /// <param name="viewModels">Builds the two dialogs this page opens.</param>
    /// <param name="logger">Where the tiles' failures are recorded.</param>
    /// <param name="localizer">
    /// The string table the greeting and the tiles' text come from. Optional and defaulting to
    /// <see cref="Localizer.Instance"/> -- the instance the container registers and the one markup
    /// binds to -- so a test building this page by hand still gets real text.
    /// </param>
    public DashboardViewModel(
        IMediator mediator,
        IViewModelFactory viewModels,
        ILogger<DashboardViewModel>? logger = null,
        ILocalizer? localizer = null)
    {
        _mediator = mediator;
        _viewModels = viewModels;
        _logger = logger;
        _localizer = localizer ?? Localizer.Instance;
        _greeting = new LocalizedMessageSlot(_localizer, text => Greeting = text ?? string.Empty);
        _greeting.Set("Dashboard.Header.Greeting");
        InitializeWidgets();

        // No Register call (TD-34) and no load (TD-35) here: activation follows IsActive, and
        // InitializeAsync is started by PageActivation when this page becomes the current one.
    }

    /// <summary>
    /// Starts every tile (TD-35).
    /// </summary>
    /// <remarks>
    /// THE TILES ARE STARTED, NOT AWAITED, AND THAT IS THE PRODUCT DECISION. Each reads independently;
    /// awaiting them all would make this complete only when the slowest had finished, and would let
    /// one failing tile decide what the whole page says. Each is observed on its own, and its failure
    /// lands ON THE TILE -- an empty tile after a failed read would otherwise say "you have no
    /// budgets" about a vault that has some.
    /// </remarks>
    /// <param name="cancellationToken">Threaded to the tiles.</param>
    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        foreach (var widget in Widgets)
        {
            if (widget is DashboardWidgetViewModelBase tile)
            {
                ObservedTask.Run(
                    () => tile.InitializeAsync(cancellationToken),
                    _logger,
                    $"dashboard-widget-initialise:{tile.GetType().Name}",
                    _localizer["Dashboard.Tile.LoadFailed"],
                    tile.ReportFailure);
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>
    /// <see cref="InitializeAsync"/> cannot throw -- each tile is observed separately -- but the policy
    /// is answered anyway, because "it cannot throw today" is a property of the body, not of the
    /// signature.
    /// </remarks>
    public InitializationFailurePolicy InitializationFailurePolicy => new(
        _logger,
        "dashboard-initialise",
        _localizer["Dashboard.Tile.LoadFailed"],
        message => _greeting.SetRendered(message));

    /// <summary>
    /// Releases the greeting and the tiles, which each hold subscriptions to the process-lifetime
    /// localizer.
    /// </summary>
    /// <remarks>
    /// Called by <c>PageActivation</c> when the user navigates away (TD-34). Without it every visit
    /// would leave the tiles subscribed to the localizer -- and held alive by it -- for the life of the
    /// process, the exact leak shape TD-34 removed from the messenger.
    /// </remarks>
    public void Dispose()
    {
        _greeting.Dispose();

        foreach (var widget in Widgets)
        {
            if (widget is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Registers this page AND the tiles it owns (TD-34).
    /// </summary>
    /// <remarks>
    /// THE TILES ARE UNREACHABLE FROM ANYWHERE ELSE. <see cref="InitializeWidgets"/> builds all five
    /// with <c>new</c>, so no navigator can drive their <c>IsActive</c> the way it drives this page's.
    /// Every one of them consumes <c>DataChangedMessage</c>; without this propagation none would
    /// register, and no tile would refresh after an edit or an import.
    /// </remarks>
    protected override void OnActivated()
    {
        base.OnActivated();

        SetWidgetsActive(true);
    }

    /// <inheritdoc cref="OnActivated"/>
    protected override void OnDeactivated()
    {
        SetWidgetsActive(false);

        base.OnDeactivated();
    }

    private void SetWidgetsActive(bool isActive)
    {
        foreach (var widget in Widgets)
        {
            if (widget is ObservableRecipient recipient)
            {
                recipient.IsActive = isActive;
            }
        }
    }

    /// <summary>
    /// Builds the five tiles. No I/O: each tile reads in its own <c>InitializeAsync</c> (TD-35).
    /// </summary>
    /// <remarks>
    /// FIVE TILES, EVERY ONE OF THEM READING SOMETHING (post-audit plan item 1.3). There were twelve,
    /// and ten rendered "No data yet" for ever. Net Worth became the total balance, which this page
    /// was already computing and not showing; Income, Expenses and Savings became one "this month"
    /// tile over a new query; the budget tile was wired to the query the budgets page already uses.
    /// Calendar, Goals, Investments, Charts and AI Suggestions were deleted: none has a subsystem
    /// behind it, and TD-99's rule is that a screen shows only what it can measure. The tiles are
    /// handed this page's logger, because the container never sees them and cannot inject one.
    /// </remarks>
    private void InitializeWidgets()
    {
        Widgets.Add(new TotalBalanceWidgetViewModel(_mediator, _localizer) { OrderIndex = 0 });
        Widgets.Add(new CashFlowWidgetViewModel(_mediator, _localizer) { OrderIndex = 1 });
        Widgets.Add(new BalanceWidgetViewModel(_mediator, _localizer) { OrderIndex = 2 });
        Widgets.Add(new BudgetWidgetViewModel(_mediator, _localizer) { OrderIndex = 3 });
        Widgets.Add(new RecentTransactionsWidgetViewModel(_mediator, _localizer) { OrderIndex = 4 });

        foreach (var widget in Widgets)
        {
            if (widget is DashboardWidgetViewModelBase tile)
            {
                tile.DiagnosticsLogger = _logger;
            }
        }
    }

    // The two dialogs only close themselves now. What they wrote reaches the tiles through
    // DataChangedMessage -- CreateAccountViewModel has always sent it, and the import wizard does as
    // of item 1.3 -- so a refresh started from here would be a second read of the same thing.

    [RelayCommand]
    private void ShowCreateAccountDialog()
    {
        CreateAccountVm = _viewModels.CreateCreateAccount(() =>
        {
            IsCreateAccountDialogVisible = false;
            CreateAccountVm = null;
        });
        IsCreateAccountDialogVisible = true;
    }

    [RelayCommand]
    private void ShowImportWizard()
    {
        ImportWizardVm = _viewModels.CreateImportWizard(() =>
        {
            IsImportWizardVisible = false;
            ImportWizardVm = null;
        });
        IsImportWizardVisible = true;
    }
}
