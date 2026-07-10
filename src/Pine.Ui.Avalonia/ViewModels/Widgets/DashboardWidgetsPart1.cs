using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using MediatR;
using Pine.Application.Accounts.Queries;
using Pine.Core.Accounts;
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
        
        WeakReferenceMessenger.Default.Register<DataChangedMessage>(this, (r, m) => ((BalanceWidgetViewModel)r).Receive(m));
        
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
            var accounts = await _mediator.Send(new GetAccountsQuery());
            Accounts.Clear();
            foreach (var account in accounts)
            {
                Accounts.Add(account);
            }
        }
        catch (Exception)
        {
            // Ignore for widget
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
