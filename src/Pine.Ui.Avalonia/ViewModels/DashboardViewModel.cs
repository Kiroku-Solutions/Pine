using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MediatR;
using Pine.Application.Accounts.Queries;
using Pine.Core.Accounts;
using Pine.Ui.Avalonia.ViewModels.Messages;

namespace Pine.Ui.Avalonia.ViewModels;

public partial class DashboardViewModel : ObservableObject, IRecipient<DataChangedMessage>
{
    private readonly IMediator _mediator;

    [ObservableProperty]
    public partial string Greeting { get; set; } = "Welcome to Pine";

    [ObservableProperty]
    public partial string TotalBalance { get; set; } = "$0.00 USD";

    public ObservableCollection<Account> Accounts { get; } = new();

    public ObservableCollection<Pine.Ui.Avalonia.ViewModels.Widgets.IDashboardWidgetViewModel> Widgets { get; } = new();

    [ObservableProperty]
    public partial bool IsCreateAccountDialogVisible { get; set; }

    [ObservableProperty]
    public partial CreateAccountViewModel? CreateAccountVm { get; set; }

    public DashboardViewModel(IMediator mediator)
    {
        _mediator = mediator;
        InitializeWidgets();
        WeakReferenceMessenger.Default.Register<DataChangedMessage>(this, (r, m) => ((DashboardViewModel)r).Receive(m));
        _ = LoadDataAsync();
    }

    public void Receive(DataChangedMessage message)
    {
        _ = LoadDataAsync();
    }

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
    }

    [RelayCommand]
    private void ShowCreateAccountDialog()
    {
        CreateAccountVm = new CreateAccountViewModel(_mediator, () =>
        {
            IsCreateAccountDialogVisible = false;
            CreateAccountVm = null;
            _ = LoadDataAsync(); // Refresh accounts after creation
        });
        IsCreateAccountDialogVisible = true;
    }

    [RelayCommand]
    private async Task LoadDataAsync()
    {
        try
        {
            var accounts = await _mediator.Send(new GetAccountsQuery());
            Accounts.Clear();
            
            decimal totalAmount = 0m;
            string currency = "USD";

            foreach (var account in accounts)
            {
                Accounts.Add(account);
                totalAmount += account.Balance.Amount;
                currency = account.Balance.Currency.Value;
            }

            TotalBalance = $"{totalAmount:C2} {currency}";
        }
        catch (Exception ex)
        {
            Greeting = "Error loading data: " + ex.Message;
        }
    }
}
