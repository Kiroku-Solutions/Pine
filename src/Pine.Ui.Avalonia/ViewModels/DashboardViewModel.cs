using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MediatR;
using Pine.Application.Accounts.Queries;
using Pine.Core.Accounts;
using Pine.Core.Common.ValueObjects;
using Pine.Ui.Avalonia.ViewModels.Messages;
using Microsoft.Extensions.Logging;
using Pine.Ui.Avalonia.Diagnostics;
using Pine.Ui.Avalonia.Services;

namespace Pine.Ui.Avalonia.ViewModels;

public partial class DashboardViewModel : ObservableRecipient, IRecipient<DataChangedMessage>, IAsyncInitializable
{
    private readonly IMediator _mediator;

    /// <summary>
    /// Builds the two dialogs this page opens (TD-28/TD-58).
    /// </summary>
    /// <remarks>
    /// BOTH DIALOGS USED TO BE BUILT WITH <c>new</c> HERE, and that was not a stylistic detail. A
    /// hand-built view model gets exactly the dependencies this page happens to hold: the mediator,
    /// and nothing else. Their optional <c>ILogger&lt;T&gt;</c> therefore defaulted to
    /// <see langword="null"/> on every real run, so every failure they reported to the user was
    /// recorded nowhere. The import wizard was additionally handed a hard-coded
    /// <c>TimeProvider.System</c>, silently overriding the <c>TimeProvider</c> the container
    /// registers. Going through the factory fixes both by construction.
    /// </remarks>
    private readonly IViewModelFactory _viewModels;

    private readonly ILogger<DashboardViewModel>? _logger;

    [ObservableProperty]
    public partial string Greeting { get; set; } = "Welcome to Pine";

    [ObservableProperty]
    public partial string TotalBalance { get; set; } = "0.00 USD";

    public ObservableCollection<Account> Accounts { get; } = new();

    /// <summary>
    /// Renders the headline balance: one total per currency, each summed through <see cref="Money"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHAT THIS REPLACED WAS A FABRICATED NUMBER. The previous version added every account's
    /// <c>Balance.Amount</c> into one bare <see cref="decimal"/> and then labelled the result with
    /// whichever currency the LAST account in the loop happened to carry. A user holding 1 000 USD
    /// and 900 EUR was shown "1900 EUR" — a figure that is not a quantity of anything.
    /// <see cref="Money.Add"/> forbids exactly this; the old code got past it by never using
    /// <c>Money</c>, adding raw decimals instead.
    /// </para>
    /// <para>
    /// SO THE FIX IS TO SUM THROUGH THE DOMAIN, which makes the defect unrepresentable rather than
    /// merely absent: accounts are grouped by currency first, and each group is folded with
    /// <see cref="Money.Add"/>, so a cross-currency addition would now throw here instead of
    /// producing a plausible-looking wrong answer. Pine has no exchange-rate source, so there is no
    /// honest single number to show for a multi-currency vault, and inventing one is what the old
    /// code did.
    /// </para>
    /// <para>
    /// The previous format string was <c>{0:C2}</c>, which renders the CURRENT MACHINE's currency
    /// symbol. On a Spanish-locale machine a dollar balance came out as "1.234,56 € USD" — a euro
    /// sign next to the code USD. Each figure is now written by <see cref="Money.ToString"/>, at its
    /// own currency's scale, with its own code and no borrowed symbol.
    /// </para>
    /// </remarks>
    private static string SummariseBalances(IEnumerable<Account> accounts)
    {
        var totals = accounts
            .GroupBy(a => a.Balance.Currency)
            .Select(group => group.Aggregate(
                Money.Zero(group.Key),
                (running, account) => running + account.Balance))
            .OrderBy(total => total.Currency.Value, StringComparer.Ordinal)
            .ToList();

        return totals.Count == 0
            ? Money.Zero(new CurrencyCode("USD")).ToString()
            : string.Join("  ·  ", totals);
    }

    public ObservableCollection<Pine.Ui.Avalonia.ViewModels.Widgets.IDashboardWidgetViewModel> Widgets { get; } = new();

    [ObservableProperty]
    public partial bool IsCreateAccountDialogVisible { get; set; }

    [ObservableProperty]
    public partial CreateAccountViewModel? CreateAccountVm { get; set; }

    [ObservableProperty]
    public partial bool IsImportWizardVisible { get; set; }

    [ObservableProperty]
    public partial Pine.Ui.Avalonia.ViewModels.Importing.ImportWizardViewModel? ImportWizardVm { get; set; }

    public DashboardViewModel(
        IMediator mediator,
        IViewModelFactory viewModels,
        ILogger<DashboardViewModel>? logger = null)
    {
        _mediator = mediator;
        _viewModels = viewModels;
        _logger = logger;
        InitializeWidgets();

        // No Register call here any more (TD-34): registration follows IsActive, which the
        // navigator flips as this page becomes and stops being the current one.
        //
        // And no load here any more either (TD-35). `_ = LoadDataAsync()` used to run from this
        // line, which meant a dashboard was never in a defined state after `new`: Accounts and
        // TotalBalance were filled at some later moment by a continuation nobody held. The load is
        // InitializeAsync now, started by PageActivation when this page becomes the current one.
    }

    /// <summary>
    /// Loads the accounts and starts every tile (TD-35).
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE TILES ARE STARTED, NOT AWAITED, AND THAT IS THE PRODUCT DECISION. Twelve widgets sit
    /// under the headline figures and each reads independently. Awaiting them all here would make
    /// this task complete only when the SLOWEST one had finished, and -- worse -- would let one
    /// failing tile decide what the whole page says. Each is therefore observed on its own through
    /// <c>ObservedTask</c>, under this page's logger, so a broken tile is a logged and blank tile
    /// rather than a blank dashboard.
    /// </para>
    /// <para>
    /// This method cannot throw: <see cref="LoadDataAsync"/> reports its own failure into
    /// <see cref="Greeting"/>. <see cref="InitializationFailurePolicy"/> is still answered honestly
    /// rather than left as a formality, because "it cannot throw today" is a property of the
    /// current body and not of the signature.
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">Threaded to the tiles; the account read is not cancellable.</param>
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await LoadDataAsync().ConfigureAwait(true);

        foreach (var widget in Widgets)
        {
            if (widget is Pine.Ui.Avalonia.ViewModels.Widgets.DashboardWidgetViewModelBase tile)
            {
                ObservedTask.Run(
                    () => tile.InitializeAsync(cancellationToken),
                    _logger,
                    $"dashboard-widget-initialise:{tile.GetType().Name}",
                    "One of the dashboard tiles could not be loaded.");
            }
        }
    }

    /// <inheritdoc />
    public InitializationFailurePolicy InitializationFailurePolicy => new(
        _logger,
        "dashboard-initialise",
        "Your dashboard could not be loaded, so the figures below may be incomplete. Try again.",
        message => Greeting = message);

    /// <summary>
    /// Registers this page AND the widgets it owns (TD-34).
    /// </summary>
    /// <remarks>
    /// THE WIDGETS ARE UNREACHABLE FROM ANYWHERE ELSE. <see cref="InitializeWidgets"/> builds all
    /// twelve with <c>new</c>, so the container never sees them and no navigator can drive their
    /// <c>IsActive</c> the way it drives this page's. Two of them consume
    /// <c>DataChangedMessage</c>; if their activation were not propagated from here they would
    /// simply never register, and the dashboard's balance and recent-transactions tiles would stop
    /// refreshing after an edit. The loop is unconditional over <c>Widgets</c> because every widget
    /// derives from <c>DashboardWidgetViewModelBase</c>, which is an <see cref="ObservableRecipient"/>;
    /// the ten that implement no <c>IRecipient&lt;T&gt;</c> register nothing and cost a virtual call.
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
    /// Re-reads the page when something elsewhere changed the data.
    /// </summary>
    /// <remarks>
    /// <c>IRecipient&lt;T&gt;</c> is <c>void</c>, so the reload has to be started rather than
    /// awaited. It goes through <c>ObservedTask</c> instead of a bare discard (TD-35) so that a
    /// reload which fails cannot vanish -- and it can: <see cref="LoadDataAsync"/> catches, but the
    /// property writes it performs afterwards are outside anybody's try.
    /// </remarks>
    /// <param name="message">The broadcast. It carries no payload.</param>
    public void Receive(DataChangedMessage message)
    {
        ObservedTask.Run(
            LoadDataAsync,
            _logger,
            "dashboard-reload",
            "Your dashboard could not be refreshed, so the figures below may be out of date.",
            reported => Greeting = reported);
    }

    /// <summary>
    /// Builds the twelve tiles. No I/O: each tile reads in its own <c>InitializeAsync</c> (TD-35).
    /// </summary>
    /// <remarks>
    /// The tiles are handed THIS page's logger. They are built with <c>new</c> rather than resolved,
    /// so the container never sees them and cannot inject one -- which is why the two tiles that
    /// actually read anything used to swallow every failure with a bare
    /// <c>catch (Exception) { /* ignore */ }</c>: there was nowhere for it to go. There is now.
    /// </remarks>
    private void InitializeWidgets()
    {
        Widgets.Add(new Pine.Ui.Avalonia.ViewModels.Widgets.BalanceWidgetViewModel(_mediator) { OrderIndex = 0 });
        Widgets.Add(new Pine.Ui.Avalonia.ViewModels.Widgets.NetWorthWidgetViewModel { OrderIndex = 1 });
        Widgets.Add(new Pine.Ui.Avalonia.ViewModels.Widgets.ExpensesWidgetViewModel { OrderIndex = 2 });
        Widgets.Add(new Pine.Ui.Avalonia.ViewModels.Widgets.IncomeWidgetViewModel { OrderIndex = 3 });
        Widgets.Add(new Pine.Ui.Avalonia.ViewModels.Widgets.BudgetWidgetViewModel { OrderIndex = 4 });
        Widgets.Add(new Pine.Ui.Avalonia.ViewModels.Widgets.SavingsWidgetViewModel { OrderIndex = 5 });
        Widgets.Add(new Pine.Ui.Avalonia.ViewModels.Widgets.CalendarWidgetViewModel { OrderIndex = 6 });
        Widgets.Add(new Pine.Ui.Avalonia.ViewModels.Widgets.GoalsWidgetViewModel { OrderIndex = 7 });
        Widgets.Add(new Pine.Ui.Avalonia.ViewModels.Widgets.InvestmentsWidgetViewModel { OrderIndex = 8 });
        Widgets.Add(new Pine.Ui.Avalonia.ViewModels.Widgets.ChartsWidgetViewModel { OrderIndex = 9 });
        Widgets.Add(new Pine.Ui.Avalonia.ViewModels.Widgets.RecentTransactionsWidgetViewModel(_mediator) { OrderIndex = 10 });
        Widgets.Add(new Pine.Ui.Avalonia.ViewModels.Widgets.AiSuggestionsWidgetViewModel { OrderIndex = 11 });

        foreach (var widget in Widgets)
        {
            if (widget is Pine.Ui.Avalonia.ViewModels.Widgets.DashboardWidgetViewModelBase tile)
            {
                tile.DiagnosticsLogger = _logger;
            }
        }
    }

    [RelayCommand]
    private void ShowCreateAccountDialog()
    {
        CreateAccountVm = _viewModels.CreateCreateAccount(() =>
        {
            IsCreateAccountDialogVisible = false;
            CreateAccountVm = null;
            // Observed rather than discarded (TD-35): the refresh that follows a create is the
            // one whose failure is most misleading, because the account IS there and the screen
            // would simply not show it.
            ObservedTask.Run(
                LoadDataAsync,
                _logger,
                "dashboard-reload",
                "The new account was created, but the dashboard could not be refreshed. Reopen it to see it.",
                reported => Greeting = reported);
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
            ObservedTask.Run(
                LoadDataAsync,
                _logger,
                "dashboard-reload",
                "The import finished, but the dashboard could not be refreshed. Reopen it to see the result.",
                reported => Greeting = reported);
        });
        IsImportWizardVisible = true;
    }

    [RelayCommand]
    private async Task LoadDataAsync()
    {
        try
        {
            var accounts = await _mediator.Send(new GetAccountsQuery());
            Accounts.Clear();
            
            foreach (var account in accounts)
            {
                Accounts.Add(account);
            }

            TotalBalance = SummariseBalances(accounts);
        }
        catch (Exception ex)
        {
            // WAS: "Error loading data: " + ex.Message (TD-24). This is a READ path, so there is
            // nothing to reassure the user about having been written; what they need is to know the
            // figures on screen are not theirs. The sentence still lands in Greeting because that is
            // the only bound text this screen has -- giving the dashboard a dedicated error surface
            // is a view change and is not this ticket.
            Greeting = UserFacingError.Report(
                _logger,
                ex,
                "dashboard-load",
                "Your dashboard could not be loaded, so the figures below may be incomplete. Try again.");
        }
    }
}
