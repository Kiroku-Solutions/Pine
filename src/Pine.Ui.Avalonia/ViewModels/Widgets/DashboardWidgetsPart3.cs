using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using MediatR;
using Pine.Application.Transactions.Queries;
using Pine.Core.Transactions;
using Pine.Ui.Avalonia.Diagnostics;
using Pine.Ui.Avalonia.ViewModels.Messages;

namespace Pine.Ui.Avalonia.ViewModels.Widgets;

public partial class GoalsWidgetViewModel : DashboardWidgetViewModelBase
{
    public GoalsWidgetViewModel()
    {
        Title = "Goals";
        ColumnSpan = 1;
        RowSpan = 1;
    }
}

public partial class ChartsWidgetViewModel : DashboardWidgetViewModelBase
{
    public ChartsWidgetViewModel()
    {
        Title = "Charts";
        ColumnSpan = 2;
        RowSpan = 2;
    }
}

public partial class RecentTransactionsWidgetViewModel : DashboardWidgetViewModelBase, IRecipient<DataChangedMessage>
{
    private readonly IMediator? _mediator;

    public ObservableCollection<Transaction> Transactions { get; } = new();

    public RecentTransactionsWidgetViewModel()
    {
        // For designer
        Title = "Recent Transactions";
    }

    public RecentTransactionsWidgetViewModel(IMediator mediator)
    {
        _mediator = mediator;
        Title = "Recent Transactions";
        ColumnSpan = 2;
        RowSpan = 2;

        // TD-34: this widget had a Receive method and a manual Register but never declared
        // IRecipient<DataChangedMessage>. It does now, which is what ObservableRecipient's
        // activation looks for; the manual registration (and its lifetime bug) is gone.
        //
        // TD-35: and no `_ = LoadDataAsync()` here any more. The dashboard calls InitializeAsync
        // below, so this tile is empty and defined after construction rather than filling itself
        // from a task nobody was holding.
    }

    /// <inheritdoc />
    public override Task InitializeAsync(CancellationToken cancellationToken) => LoadDataAsync();

    /// <summary>Re-reads the latest transactions when something elsewhere changed them.</summary>
    /// <remarks>
    /// Observed rather than discarded (TD-35): <c>IRecipient&lt;T&gt;</c> is <c>void</c> so the
    /// reload still cannot be awaited, but its failure now reaches the dashboard's log.
    /// </remarks>
    /// <param name="message">The broadcast. It carries no payload.</param>
    public void Receive(DataChangedMessage message)
    {
        ObservedTask.Run(
            LoadDataAsync,
            DiagnosticsLogger,
            "widget-recent-transactions-reload",
            "The recent transactions tile could not be refreshed.");
    }

    /// <summary>
    /// Reads the ten most recent transactions.
    /// </summary>
    /// <remarks>
    /// THE BARE <c>catch (Exception) { /* Ignore */ }</c> THAT WAS HERE IS GONE (TD-35). A failed
    /// page query used to leave the tile showing nothing, with no record of why -- indistinguishable
    /// on screen from a vault that genuinely has no transactions. Callers route this through
    /// <c>ObservedTask</c> now, so the tile is still empty but the failure is written down.
    /// </remarks>
    private async Task LoadDataAsync()
    {
        if (_mediator == null) return;

        var pagedResult = await _mediator.Send(new GetTransactionsQuery(1, 10));
        Transactions.Clear();
        foreach (var tx in pagedResult.Items)
        {
            Transactions.Add(tx);
        }
    }
}

public partial class AiSuggestionsWidgetViewModel : DashboardWidgetViewModelBase
{
    public AiSuggestionsWidgetViewModel()
    {
        Title = "AI Suggestions";
        ColumnSpan = 1;
        RowSpan = 1;
    }
}
