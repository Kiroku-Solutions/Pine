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
using Pine.Core.Common;
using Pine.Ui.Avalonia.ViewModels.Messages;
using Microsoft.Extensions.Logging;
using Pine.Ui.Avalonia.Diagnostics;
using Pine.Ui.Avalonia.Localization;

namespace Pine.Ui.Avalonia.ViewModels;

public partial class AddTransactionViewModel : ObservableObject, IDisposable
{
    private readonly IMediator _mediator;
    private readonly LocalizedMessageSlot _message;
    private readonly Action _onComplete;
    private readonly ILogger<AddTransactionViewModel>? _logger;

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

    public AddTransactionViewModel(
        IMediator mediator,
        ILocalizer localizer,
        Action onComplete,
        ILogger<AddTransactionViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        _mediator = mediator;
        _message = new LocalizedMessageSlot(localizer, text => ErrorMessage = text ?? string.Empty);
        _onComplete = onComplete;
        // NOT MOVED ONTO IAsyncInitializable, AND THE REASON IS STRUCTURAL (TD-35). That interface
        // is driven by PageActivation, which only ever sees a page assigned to a navigator's
        // CurrentPage. This view model is an overlay: TransactionsViewModel builds it through
        // IViewModelFactory and shows it in place, so navigation never sees it and would never
        // start its initialisation. The load therefore stays here -- what changes is that its
        // failure is now read instead of discarded.
        _logger = logger;
        ObservedTask.Run(
            LoadAccountsAsync,
            _logger,
            "add-transaction-accounts-load",
            new UserFacingMessage("AddTransaction.Failure.AccountsLoad"),
            _message.Set);
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
            _message.Set("AddTransaction.Error.NoAccount");
            return;
        }

        if (string.IsNullOrWhiteSpace(Payee))
        {
            _message.Set("AddTransaction.Error.PayeeRequired");
            return;
        }

        if (Amount <= 0)
        {
            _message.Set("AddTransaction.Error.AmountPositive");
            return;
        }

        IsBusy = true;
        _message.Clear();

        try
        {
            var currency = SelectedAccount.Balance.Currency.Value;

            if (IsTransfer)
            {
                if (SelectedDestinationAccount == null)
                {
                    _message.Set("AddTransaction.Error.NoDestinationAccount");
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
            // WAS: $"Error: {ex.Message}" (TD-24). On this exact path that rendered Money's
            // "Cannot add money of different currencies" and, once the store was reached, SQLite
            // error text carrying the vault file path. A refused write also reaches here as a
            // ConcurrencyConflictException, which nothing used to catch (TD-98) and which now
            // arrives with its own sentence and its "Reload and try again." instruction intact.
            _message.Set(UserFacingError.Report(
                _logger,
                ex,
                "add-transaction",
                new UserFacingMessage("AddTransaction.Failure.Save")));
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

    /// <summary>
    /// Releases the localized message slot.
    /// </summary>
    /// <remarks>
    /// THE ANALYZER ASKED FOR THIS AND IT WAS RIGHT (CA1001). A
    /// <see cref="LocalizedMessageSlot"/> subscribes to <c>ILocalizer.LanguageChanged</c>, and the
    /// localizer is a singleton for the life of the process -- so a slot that is never released
    /// keeps this view model reachable forever. That is precisely the leak shape TD-34 removed
    /// from the messenger, and it would have come back through a different event.
    /// </remarks>
    public void Dispose()
    {
        _message.Dispose();
        GC.SuppressFinalize(this);
    }

}
