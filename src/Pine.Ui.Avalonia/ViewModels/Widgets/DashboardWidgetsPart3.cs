using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using MediatR;
using Pine.Application.Transactions.Queries;
using Pine.Core.Transactions;
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

public partial class RecentTransactionsWidgetViewModel : DashboardWidgetViewModelBase
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
        
        WeakReferenceMessenger.Default.Register<DataChangedMessage>(this, (r, m) => ((RecentTransactionsWidgetViewModel)r).Receive(m));
        
        _ = LoadDataAsync();
    }

    public void Receive(DataChangedMessage message)
    {
        _ = LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        if (_mediator == null) return;
        
        try
        {
            var pagedResult = await _mediator.Send(new GetTransactionsQuery(1, 10));
            Transactions.Clear();
            foreach (var tx in pagedResult.Items)
            {
                Transactions.Add(tx);
            }
        }
        catch (Exception)
        {
            // Ignore
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
