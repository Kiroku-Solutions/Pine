using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MediatR;
using Pine.Application.Accounts.Commands;
using Pine.Application.Accounts.Queries;
using Pine.Core.Accounts;
using Pine.Core.Common;
using Pine.Ui.Avalonia.ViewModels.Messages;
using Microsoft.Extensions.Logging;
using Pine.Ui.Avalonia.Diagnostics;
using Pine.Ui.Avalonia.Localization;

namespace Pine.Ui.Avalonia.ViewModels;

public sealed record ParentAccountOption(Guid? AccountId, string DisplayName, string? CurrencyCode);

public partial class CreateAccountViewModel : ObservableObject, IDisposable
{
    private readonly IMediator _mediator;
    private readonly ILocalizer _localizer;
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
    [NotifyPropertyChangedFor(nameof(IsSubAccountSelected))]
    [NotifyPropertyChangedFor(nameof(CanEditCurrency))]
    public partial ParentAccountOption? SelectedParentAccount { get; set; }

    [ObservableProperty]
    public partial decimal MonthlyAllocation { get; set; } = 0m;

    public bool IsSubAccountSelected => SelectedParentAccount?.AccountId is not null;

    public bool CanEditCurrency => !IsBusy && !IsSubAccountSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditCurrency))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;

    public ObservableCollection<AccountType> AccountTypes { get; } = new(Enum.GetValues<AccountType>());

    public ObservableCollection<ParentAccountOption> ParentAccountOptions { get; } = new();

    /// <summary>
    /// The searchable currency picker (TD-76): every ISO 4217 code, filtered as the user types.
    /// </summary>
    public CurrencyPickerViewModel CurrencyPicker { get; }

    public CreateAccountViewModel(
        IMediator mediator,
        ILocalizer localizer,
        Action onComplete,
        ILogger<CreateAccountViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        _mediator = mediator;
        _localizer = localizer;
        _message = new LocalizedMessageSlot(localizer, text => ErrorMessage = text ?? string.Empty);
        _onComplete = onComplete;
        _logger = logger;

        var noneOption = new ParentAccountOption(null, _localizer["CreateAccount.Parent.None"], null);
        ParentAccountOptions.Add(noneOption);
        SelectedParentAccount = noneOption;

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

        _ = LoadParentAccountsAsync();
    }

    private async Task LoadParentAccountsAsync()
    {
        try
        {
            var accounts = await _mediator.Send(new GetAccountsQuery());
            if (accounts is null)
            {
                return;
            }

            foreach (var account in accounts.Where(a => !a.IsSubAccount).OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                ParentAccountOptions.Add(new ParentAccountOption(
                    account.Id,
                    $"{account.Name} ({account.Balance.Currency.Value})",
                    account.Balance.Currency.Value));
            }
        }
        catch (Exception)
        {
            // Isolated unit tests that only stub CreateAccountCommand still work with the default "None" option.
        }
    }

    partial void OnSelectedParentAccountChanged(ParentAccountOption? value)
    {
        if (value?.AccountId is not null)
        {
            if (!string.IsNullOrWhiteSpace(value.CurrencyCode))
            {
                SelectedCurrency = value.CurrencyCode;
            }

            // Sub-accounts draw from their parent's shared pool, so default initial balance to 0.
            InitialBalance = 0m;
        }
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
            var parentId = SelectedParentAccount?.AccountId;
            decimal? monthlyAllocation = parentId.HasValue && MonthlyAllocation > 0m ? MonthlyAllocation : null;

            await _mediator.Send(new CreateAccountCommand(
                AccountName,
                SelectedCurrency,
                SelectedType,
                parentId.HasValue ? 0m : InitialBalance,
                parentId,
                monthlyAllocation));

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
