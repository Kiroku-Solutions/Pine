using System;
using System.Collections.Generic;
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
using Pine.Core.Transactions;
using Pine.Ui.Avalonia.Diagnostics;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.ViewModels.Messages;
using Microsoft.Extensions.Logging;

namespace Pine.Ui.Avalonia.ViewModels;

/// <summary>
/// The edit-transaction dialog: <c>AddTransactionViewModel</c>'s form, pre-filled from an existing
/// row and sending <see cref="UpdateTransactionCommand"/> instead of
/// <see cref="AddTransactionCommand"/> (TD-68, closing the edit half TD-02 deferred).
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS IS NOT "AddTransactionViewModel WITH A FLAG". The two forms share almost every field, but
/// an edit cannot change the account (that would move a balance effect across two accounts, which is
/// a transfer's job, not an edit's) and cannot offer the Transfer type at all (see
/// <see cref="Transaction.Update"/>'s remarks for why a transfer leg is refused). A shared base class
/// for two view models that differ in exactly "which fields are even on the form" would have bought
/// less clarity than it cost.
/// </para>
/// <para>
/// SINGLE-SPLIT SHAPE, LIKE THE ADD FORM. This mirrors <c>AddTransactionCommandHandler</c>'s own
/// choice of "one split for the whole amount" exactly. <see cref="Utils.Converters.CanEditTransaction"/>
/// is what keeps this dialog from ever being opened on a transaction that already has more than one
/// split, so saving here can never silently collapse a multi-split row.
/// </para>
/// </remarks>
public partial class EditTransactionViewModel : ObservableObject, IDisposable
{
    private readonly IMediator _mediator;
    private readonly LocalizedMessageSlot _message;
    private readonly Action _onComplete;
    private readonly ILogger<EditTransactionViewModel>? _logger;
    private readonly Transaction _transaction;
    private readonly Guid? _existingSplitId;

    /// <summary>The transaction being edited. Exposed read-only so the view can show what it is editing.</summary>
    public Guid TransactionId => _transaction.Id;

    /// <summary>The account this transaction belongs to. Not editable -- see the type remarks.</summary>
    public Account? Account { get; private set; }

    [ObservableProperty]
    public partial DateTimeOffset Date { get; set; }

    [ObservableProperty]
    public partial string Payee { get; set; } = string.Empty;

    /// <summary>The absolute value of the amount; <see cref="IsIncome"/> supplies the sign.</summary>
    [ObservableProperty]
    public partial decimal Amount { get; set; }

    [ObservableProperty]
    public partial bool IsIncome { get; set; }

    [ObservableProperty]
    public partial string Notes { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Category { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;

    /// <summary>Creates the dialog, pre-filled from <paramref name="transaction"/>.</summary>
    /// <param name="transaction">The transaction to edit. Must not be voided or a transfer leg.</param>
    /// <param name="mediator">The dispatcher.</param>
    /// <param name="localizer">Words this dialog's error messages.</param>
    /// <param name="onComplete">What the parent page does when the dialog is finished with.</param>
    /// <param name="logger">The category logger.</param>
    public EditTransactionViewModel(
        Transaction transaction,
        IMediator mediator,
        ILocalizer localizer,
        Action onComplete,
        ILogger<EditTransactionViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(localizer);

        _transaction = transaction;
        _mediator = mediator;
        _message = new LocalizedMessageSlot(localizer, text => ErrorMessage = text ?? string.Empty);
        _onComplete = onComplete;
        _logger = logger;

        Date = transaction.Date;
        Payee = transaction.Payee;
        Amount = Math.Abs(transaction.TotalAmount.Amount);
        IsIncome = transaction.TotalAmount.Amount >= 0;
        Notes = transaction.Notes;

        var splits = transaction.Splits;
        Category = splits.Count == 1 ? splits.First().Category : string.Empty;
        _existingSplitId = splits.Count == 1 ? splits.First().Id : (Guid?)null;

        ObservedTask.Run(
            LoadAccountAsync,
            _logger,
            "edit-transaction-account-load",
            new UserFacingMessage("EditTransaction.Failure.AccountLoad"),
            _message.Set);
    }

    private async Task LoadAccountAsync()
    {
        var accounts = await _mediator.Send(new GetAccountsQuery());
        Account = accounts.FirstOrDefault(a => a.Id == _transaction.AccountId);
        OnPropertyChanged(nameof(Account));
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(Payee))
        {
            _message.Set("EditTransaction.Error.PayeeRequired");
            return;
        }

        if (Amount <= 0)
        {
            _message.Set("EditTransaction.Error.AmountPositive");
            return;
        }

        var account = Account;
        if (account is null)
        {
            _message.Set("EditTransaction.Error.AccountMissing");
            return;
        }

        IsBusy = true;
        _message.Clear();

        try
        {
            decimal signedAmount = IsIncome ? Amount : -Amount;
            var currency = account.Balance.Currency.Value;

            var splits = new List<UpdateTransactionSplitCommand>
            {
                new(_existingSplitId, signedAmount, Category, Notes ?? string.Empty),
            };

            await _mediator.Send(new UpdateTransactionCommand(
                _transaction.Id,
                Date,
                Payee,
                signedAmount,
                currency,
                Notes ?? string.Empty,
                splits));

            WeakReferenceMessenger.Default.Send(new DataChangedMessage());
            _onComplete?.Invoke();
        }
        catch (Exception ex)
        {
            _message.Set(UserFacingError.Report(
                _logger,
                ex,
                "edit-transaction",
                new UserFacingMessage("EditTransaction.Failure.Save")));
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

    /// <summary>Releases the localized message slot. See <c>AddTransactionViewModel.Dispose</c> for why (CA1001).</summary>
    public void Dispose()
    {
        _message.Dispose();
        GC.SuppressFinalize(this);
    }
}
