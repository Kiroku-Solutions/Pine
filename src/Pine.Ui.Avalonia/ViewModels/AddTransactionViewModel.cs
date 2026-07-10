using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MediatR;
using Pine.Application.Accounts.Queries;
using Pine.Application.Transactions.Commands;
using Pine.Core.Accounts;
using Pine.Ui.Avalonia.ViewModels.Messages;

namespace Pine.Ui.Avalonia.ViewModels;

public partial class AddTransactionViewModel : ObservableObject
{
    private readonly IMediator _mediator;
    private readonly Action _onComplete;

    [ObservableProperty]
    public partial Account? SelectedAccount { get; set; }

    [ObservableProperty]
    public partial DateTimeOffset Date { get; set; } = DateTimeOffset.Now;

    [ObservableProperty]
    public partial string Payee { get; set; } = string.Empty;

    // Positive for income, negative for expense (we'll capture absolute amount and apply sign based on type)
    [ObservableProperty]
    public partial decimal Amount { get; set; } = 0m;

    [ObservableProperty]
    public partial bool IsIncome { get; set; } = false;

    [ObservableProperty]
    public partial string Notes { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Category { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;

    public ObservableCollection<Account> Accounts { get; } = new();

    public AddTransactionViewModel(IMediator mediator, Action onComplete)
    {
        _mediator = mediator;
        _onComplete = onComplete;
        _ = LoadAccountsAsync();
    }

    private async Task LoadAccountsAsync()
    {
        var accounts = await _mediator.Send(new GetAccountsQuery());
        foreach (var acc in accounts)
        {
            Accounts.Add(acc);
        }

        if (Accounts.Any())
            SelectedAccount = Accounts.First();
    }

    [RelayCommand]
    private async Task CreateAsync()
    {
        if (SelectedAccount == null)
        {
            ErrorMessage = "Please select an account.";
            return;
        }

        if (string.IsNullOrWhiteSpace(Payee))
        {
            ErrorMessage = "Payee is required.";
            return;
        }

        if (Amount <= 0)
        {
            ErrorMessage = "Amount must be greater than zero.";
            return;
        }

        IsBusy = true;
        ErrorMessage = string.Empty;

        try
        {
            decimal signedAmount = IsIncome ? Amount : -Amount;
            var currency = SelectedAccount.Balance.Currency.Value;

            await _mediator.Send(new AddTransactionCommand(
                SelectedAccount.Id,
                Date,
                Payee,
                signedAmount,
                currency,
                Notes,
                Category
            ));

            WeakReferenceMessenger.Default.Send(new DataChangedMessage());
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
