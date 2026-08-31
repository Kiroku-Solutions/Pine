using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MediatR;
using Pine.Application.Accounts.Commands;
using Pine.Core.Accounts;
using Pine.Ui.Avalonia.ViewModels.Messages;
using Microsoft.Extensions.Logging;
using Pine.Ui.Avalonia.Diagnostics;

namespace Pine.Ui.Avalonia.ViewModels;

public partial class CreateAccountViewModel : ObservableObject
{
    private readonly IMediator _mediator;
    
    // Action to call when creation is complete (to close the dialog/return)
    private readonly Action _onComplete;

    private readonly ILogger<CreateAccountViewModel>? _logger;

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
    /// <summary>
    /// The currencies offered when creating an account.
    /// </summary>
    /// <remarks>
    /// "BTC" was in this list and has been removed. It is not an ISO 4217 code, and since TD-18 gave
    /// <c>CurrencyCode</c> membership validation, picking it would have thrown out of the handler —
    /// a shipped UI affordance that cannot succeed. Pine has no crypto-asset model (no divisibility,
    /// no rate source), so offering one was always a promise the domain could not keep. Widening
    /// this to the full ISO table with a searchable picker is TD-76.
    /// </remarks>
    public ObservableCollection<string> Currencies { get; } = new(new[] { "USD", "EUR", "GBP", "JPY", "CHF", "CAD", "AUD" });

    public CreateAccountViewModel(
        IMediator mediator,
        Action onComplete,
        ILogger<CreateAccountViewModel>? logger = null)
    {
        _mediator = mediator;
        _onComplete = onComplete;
        _logger = logger;
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

            WeakReferenceMessenger.Default.Send(new DataChangedMessage());
            _onComplete?.Invoke();
        }
        catch (Exception ex)
        {
            // WAS: $"Error: {ex.Message}" (TD-24). CreateAccountCommand still has no validator
            // (TD-78), so an unknown currency arrives here as a raw ArgumentException from inside
            // CurrencyCode naming its own parameter. That is exactly the class of text this stops
            // showing; giving the command a validator is TD-78 and would turn it into a sentence.
            ErrorMessage = UserFacingError.Report(
                _logger,
                ex,
                "create-account",
                "The account could not be created. Nothing was changed.");
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
