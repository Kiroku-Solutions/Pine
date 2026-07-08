using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediatR;
using Pine.Application.Accounts.Commands;
using Pine.Core.Accounts;

namespace Pine.Ui.Avalonia.ViewModels;

public partial class CreateAccountViewModel : ObservableObject
{
    private readonly IMediator _mediator;
    
    // Action to call when creation is complete (to close the dialog/return)
    private readonly Action _onComplete;

    [ObservableProperty]
    public partial string AccountName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial AccountType SelectedType { get; set; } = AccountType.Bank;

    [ObservableProperty]
    public partial string SelectedCurrency { get; set; } = "USD";

    [ObservableProperty]
    public partial decimal InitialBalance { get; set; } = 0m;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;

    public ObservableCollection<AccountType> AccountTypes { get; } = new(Enum.GetValues<AccountType>());
    public ObservableCollection<string> Currencies { get; } = new(new[] { "USD", "EUR", "GBP", "JPY", "BTC" });

    public CreateAccountViewModel(IMediator mediator, Action onComplete)
    {
        _mediator = mediator;
        _onComplete = onComplete;
    }

    [RelayCommand]
    private async Task CreateAsync()
    {
        if (string.IsNullOrWhiteSpace(AccountName))
        {
            ErrorMessage = "Account name is required.";
            return;
        }

        IsBusy = true;
        ErrorMessage = string.Empty;

        try
        {
            await _mediator.Send(new CreateAccountCommand(
                AccountName, 
                SelectedCurrency, 
                SelectedType, 
                InitialBalance));

            _onComplete?.Invoke();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _onComplete?.Invoke();
    }
}
