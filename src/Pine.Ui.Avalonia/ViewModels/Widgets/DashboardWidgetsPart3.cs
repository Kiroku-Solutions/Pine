using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using MediatR;
using Pine.Application.Transactions.Queries;
using Pine.Core.Transactions;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.ViewModels.Messages;

namespace Pine.Ui.Avalonia.ViewModels.Widgets;

/// <summary>The ten most recent transactions.</summary>
public partial class RecentTransactionsWidgetViewModel : DashboardWidgetViewModelBase, IRecipient<DataChangedMessage>
{
    private readonly IMediator _mediator;

    public ObservableCollection<Transaction> Transactions { get; } = new();

    public RecentTransactionsWidgetViewModel(IMediator mediator, ILocalizer localizer)
        : base(localizer, "Dashboard.Tile.RecentTransactions")
    {
        _mediator = mediator;
        Width = 560;
        Height = 320;

        // TD-34: this tile declares IRecipient<DataChangedMessage>, which is what ObservableRecipient's
        // activation looks for. TD-35: no load here; the dashboard calls InitializeAsync.
    }

    /// <summary>Reads the ten most recent transactions.</summary>
    /// <remarks>
    /// The bare <c>catch (Exception) { /* Ignore */ }</c> that was here is gone (TD-35): a failed read
    /// now reaches the log and the tile's own failure message, instead of looking exactly like a vault
    /// with no transactions.
    /// </remarks>
    public override async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var pagedResult = await _mediator.Send(new GetTransactionsQuery(1, 10, IncludeVoided: true, AccountIds: AccountScope), cancellationToken);
        Transactions.Clear();
        foreach (var tx in pagedResult.Items)
        {
            Transactions.Add(tx);
        }

        MarkLoaded(Transactions.Count == 0);
    }

    /// <summary>Re-reads the latest transactions when something elsewhere changed them.</summary>
    /// <param name="message">The broadcast. It carries no payload.</param>
    public void Receive(DataChangedMessage message) => Reload("widget-recent-transactions-reload");
}
