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
    /// The searchable currency picker (TD-76): every ISO 4217 code, filtered as the user types.
    /// </summary>
    /// <remarks>
    /// Replaces the seven-item hard-coded list ("BTC" was removed from it earlier: not an ISO 4217 code).
    /// The picker's <see cref="CurrencyPickerViewModel.SelectedCode"/> and <see cref="SelectedCurrency"/>
    /// are kept in step both ways, so the command and everything else keep using the plain string.
    /// </remarks>
    public CurrencyPickerViewModel CurrencyPicker { get; }

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

        CurrencyPicker = new CurrencyPickerViewModel(SelectedCurrency);
        CurrencyPicker.PropertyChanged += (_, e) =>
        {
            // Only a real pick moves the string: half-typed text never reaches the command.
            if (e.PropertyName == nameof(CurrencyPickerViewModel.SelectedCode) &&
                !string.IsNullOrEmpty(CurrencyPicker.SelectedCode))
            {
                SelectedCurrency = CurrencyPicker.SelectedCode;
            }
        };
    }

    partial void OnSelectedCurrencyChanged(string value) => CurrencyPicker.SelectedCode = value;

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
            // WAS: $"Error: {ex.Message}" (TD-24). CreateAccountCommand validates the currency and
            // name itself (TD-78), so an expected refusal never reaches here; this is the generic
            // fallback for the unexpected, shown as a localised sentence and never the exception text.
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
