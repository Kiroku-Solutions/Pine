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
    public partial Account? SelectedDestinationAccount { get; set; }

    [ObservableProperty]
    public partial DateTimeOffset Date { get; set; } = DateTimeOffset.Now;

    [ObservableProperty]
    public partial string Payee { get; set; } = string.Empty;

    // Positive for income, negative for expense (we'll capture absolute amount and apply sign based on type)
    [ObservableProperty]
    public partial decimal Amount { get; set; } = 0m;

    private bool _isIncome;
    public bool IsIncome 
    { 
        get => _isIncome; 
        set 
        { 
            if (SetProperty(ref _isIncome, value) && value) 
            { 
                IsTransfer = false; 
                IsExpense = false; 
            } 
        } 
    }

    private bool _isTransfer;
    public bool IsTransfer 
    { 
        get => _isTransfer; 
        set 
        { 
            if (SetProperty(ref _isTransfer, value) && value) 
            { 
                IsIncome = false; 
                IsExpense = false; 
            } 
        } 
    }

    private bool _isExpense = true;
    public bool IsExpense 
    { 
        get => _isExpense; 
        set 
        { 
            if (SetProperty(ref _isExpense, value) && value) 
            { 
                IsIncome = false; 
                IsTransfer = false; 
            } 
        } 
    }

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
            var currency = SelectedAccount.Balance.Currency.Value;

            if (IsTransfer)
            {
                if (SelectedDestinationAccount == null)
                {
                    ErrorMessage = "Please select a destination account.";
                    IsBusy = false;
                    return;
                }
                
                await _mediator.Send(new TransferFundsCommand(
                    SelectedAccount.Id,
                    SelectedDestinationAccount.Id,
                    Amount,
                    currency,
                    Date,
                    Notes
                ));
            }
            else
            {
                decimal signedAmount = IsIncome ? Amount : -Amount;

                await _mediator.Send(new AddTransactionCommand(
                    SelectedAccount.Id,
                    Date,
                    Payee,
                    signedAmount,
                    currency,
                    Notes,
                    Category
                ));
            }

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
