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
using Pine.Core.Common;
using Pine.Ui.Avalonia.ViewModels.Messages;
using Microsoft.Extensions.Logging;
using Pine.Ui.Avalonia.Diagnostics;
using Pine.Ui.Avalonia.Localization;

namespace Pine.Ui.Avalonia.ViewModels;

public partial class CreateAccountViewModel : ObservableObject, IDisposable
{
    private readonly IMediator _mediator;
    private readonly LocalizedMessageSlot _message;
    
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
        ILocalizer localizer,
        Action onComplete,
        ILogger<CreateAccountViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        _mediator = mediator;
        _message = new LocalizedMessageSlot(localizer, text => ErrorMessage = text ?? string.Empty);
        _onComplete = onComplete;
        _logger = logger;
    }

    [RelayCommand]
    private async Task CreateAsync()
    {
        if (string.IsNullOrWhiteSpace(AccountName))
        {
            _message.Set("CreateAccount.Error.NameRequired");
            return;
        }

        IsBusy = true;
        _message.Clear();

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
            _message.Set(UserFacingError.Report(
                _logger,
                ex,
                "create-account",
                new UserFacingMessage("CreateAccount.Failure.Create")));
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
