using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using MediatR;
using Pine.Application.Accounts.Queries;
using Pine.Core.Accounts;
using Pine.Ui.Avalonia.Diagnostics;
using Pine.Ui.Avalonia.ViewModels.Messages;

namespace Pine.Ui.Avalonia.ViewModels.Widgets;

public partial class BalanceWidgetViewModel : DashboardWidgetViewModelBase, IRecipient<DataChangedMessage>
{
    private readonly IMediator? _mediator;

    public ObservableCollection<Account> Accounts { get; } = new();

    public BalanceWidgetViewModel()
    {
        // Parameterless constructor for designer
        Title = "Accounts Overview";
    }

    public BalanceWidgetViewModel(IMediator mediator)
    {
        _mediator = mediator;
        Title = "Accounts Overview";
        ColumnSpan = 1;
        RowSpan = 1;

        // No Register call here any more (TD-34). ObservableRecipient registers this widget when
        // DashboardViewModel switches its IsActive on, and unregisters it when the dashboard is
        // navigated away from -- which is what stops a stale dashboard's twelve widgets from
        // re-querying on every DataChangedMessage for as long as the GC leaves them alive.
        //
        // And no load here any more either (TD-35): the dashboard calls InitializeAsync below when
        // it initialises itself, so a tile built by `new` is empty and says so until asked.
    }

    /// <inheritdoc />
    public override Task InitializeAsync(CancellationToken cancellationToken) => LoadDataAsync();

    /// <summary>Re-reads the accounts when something elsewhere changed them.</summary>
    /// <remarks>
    /// Observed rather than discarded (TD-35). <c>IRecipient&lt;T&gt;</c> is <c>void</c>, so the
    /// reload cannot be awaited; what changes is that its failure now reaches the dashboard's log
    /// instead of being dropped when the task is collected.
    /// </remarks>
    /// <param name="message">The broadcast. It carries no payload.</param>
    public void Receive(DataChangedMessage message)
    {
        ObservedTask.Run(
            LoadDataAsync,
            DiagnosticsLogger,
            "widget-balance-reload",
            "The accounts tile could not be refreshed.");
    }

    /// <summary>
    /// Reads the accounts.
    /// </summary>
    /// <remarks>
    /// THE <c>catch (Exception) { /* Ignore for widget */ }</c> THAT WAS HERE IS GONE (TD-35). It
    /// turned a failed account query into an empty tile with no record anywhere, which reads to the
    /// user as "you have no accounts" -- a statement about their money that nothing had checked.
    /// Every caller now routes this through <c>ObservedTask</c>, so the failure is logged under the
    /// dashboard's category; the tile is still empty, but the reason exists somewhere.
    /// </remarks>
    private async Task LoadDataAsync()
    {
        if (_mediator == null) return;

        var accounts = await _mediator.Send(new GetAccountsQuery());
        Accounts.Clear();
        foreach (var account in accounts)
        {
            Accounts.Add(account);
        }
    }
}

public partial class ExpensesWidgetViewModel : DashboardWidgetViewModelBase
{
    public ExpensesWidgetViewModel()
    {
        Title = "Expenses";
        ColumnSpan = 1;
        RowSpan = 1;
    }
}

public partial class IncomeWidgetViewModel : DashboardWidgetViewModelBase
{
    public IncomeWidgetViewModel()
    {
        Title = "Income";
        ColumnSpan = 1;
        RowSpan = 1;
    }
}

public partial class InvestmentsWidgetViewModel : DashboardWidgetViewModelBase
{
    public InvestmentsWidgetViewModel()
    {
        Title = "Investments";
        ColumnSpan = 2;
        RowSpan = 2;
    }
}
