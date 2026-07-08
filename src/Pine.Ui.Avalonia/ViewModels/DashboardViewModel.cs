using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediatR;
using Pine.Application.Accounts.Queries;
using Pine.Core.Accounts;

namespace Pine.Ui.Avalonia.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly IMediator _mediator;

    [ObservableProperty]
    public partial string Greeting { get; set; } = "Welcome to Pine";

    [ObservableProperty]
    public partial string TotalBalance { get; set; } = "$0.00 USD";

    public ObservableCollection<Account> Accounts { get; } = new();

    public DashboardViewModel(IMediator mediator)
    {
        _mediator = mediator;
        _ = LoadDataAsync();
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
