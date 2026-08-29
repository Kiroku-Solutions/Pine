using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MediatR;
using Pine.Application.Common;
using Pine.Application.Transactions.Commands;
using Pine.Application.Transactions.Queries;
using Pine.Core.Transactions;
using Pine.Ui.Avalonia.ViewModels.Messages;

namespace Pine.Ui.Avalonia.ViewModels;

public partial class TransactionsViewModel : ObservableObject, IRecipient<DataChangedMessage>
{
    private readonly IMediator _mediator;

    [ObservableProperty]
    public partial bool IsAddTransactionDialogVisible { get; set; }

    [ObservableProperty]
    public partial AddTransactionViewModel? AddTransactionVm { get; set; }

    public ObservableCollection<Transaction> Transactions { get; } = new();

    private int _currentPage = 1;
    private const int PageSize = 30;

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial bool HasMorePages { get; set; } = true;

    /// <summary>Whether the void confirmation is on screen.</summary>
    [ObservableProperty]
    public partial bool IsVoidDialogVisible { get; set; }

    /// <summary>The transaction the confirmation is about, or <see langword="null"/> when it is closed.</summary>
    [ObservableProperty]
    public partial Transaction? VoidTarget { get; set; }

    /// <summary>The reason typed by the user. Required — the command refuses a blank one.</summary>
    [ObservableProperty]
    public partial string VoidReason { get; set; } = string.Empty;

    /// <summary>The refusal to show inside the confirmation, or <see langword="null"/>.</summary>
    [ObservableProperty]
    public partial string? VoidError { get; set; }

    /// <summary>Whether a void is in flight, so the buttons can be disabled.</summary>
    [ObservableProperty]
    public partial bool IsVoiding { get; set; }

    public TransactionsViewModel(IMediator mediator)
    {
        _mediator = mediator;
        WeakReferenceMessenger.Default.Register<DataChangedMessage>(this, (r, m) => ((TransactionsViewModel)r).Receive(m));
        _ = LoadDataAsync();
    }

    public void Receive(DataChangedMessage message)
    {
        _ = LoadDataAsync();
    }

    [RelayCommand]
    private async Task LoadDataAsync()
    {
        _currentPage = 1;
        Transactions.Clear();
        HasMorePages = true;
        await LoadNextPageAsync();
    }

    [RelayCommand]
    private async Task LoadNextPageAsync()
    {
        if (IsLoading || !HasMorePages) return;

        IsLoading = true;
        try
        {
            var pagedResult = await _mediator.Send(new GetTransactionsQuery(_currentPage, PageSize));

            foreach (var t in pagedResult.Items)
            {
                Transactions.Add(t);
            }

            if (_currentPage * PageSize >= pagedResult.TotalCount)
            {
                HasMorePages = false;
            }
            else
            {
                _currentPage++;
            }
        }
        catch
        {
            // Handle error silently or via status message
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void ShowAddTransactionDialog()
    {
        AddTransactionVm = new AddTransactionViewModel(_mediator, () =>
        {
            IsAddTransactionDialogVisible = false;
            AddTransactionVm = null;
            _ = LoadDataAsync(); // Refresh
        });
        IsAddTransactionDialogVisible = true;
    }

    /// <summary>
    /// Opens the void confirmation for one transaction.
    /// </summary>
    /// <remarks>
    /// Voiding is confirmed rather than immediate because it moves a balance, and because the reason
    /// is not optional — it is the audit trail the whole feature exists to preserve. An already-void
    /// transaction is ignored here as well as refused by the validator; the row's button is hidden in
    /// that state, so reaching this method with one means something is out of sync and silently doing
    /// nothing is better than opening a dialog that cannot succeed.
    /// </remarks>
    /// <param name="transaction">The row the user acted on.</param>
    [RelayCommand]
    private void BeginVoid(Transaction? transaction)
    {
        if (transaction is null || transaction.IsVoided) return;

        VoidTarget = transaction;
        VoidReason = string.Empty;
        VoidError = null;
        IsVoidDialogVisible = true;
    }

    /// <summary>Closes the confirmation without voiding anything.</summary>
    [RelayCommand]
    private void CancelVoid()
    {
        IsVoidDialogVisible = false;
        VoidTarget = null;
        VoidReason = string.Empty;
        VoidError = null;
    }

    /// <summary>
    /// Sends the void and refreshes every screen that shows money.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The broadcast is not optional. Voiding a transfer voids BOTH legs and moves TWO balances, so a
    /// refresh limited to this list would leave the dashboard and the balance widget showing figures
    /// that are now wrong. <see cref="DataChangedMessage"/> is the existing mechanism for exactly
    /// that, and every money-showing view model already listens for it.
    /// </para>
    /// <para>
    /// <see cref="RequestValidationException"/> is caught separately and its message shown verbatim,
    /// because its messages are written as complete user-facing sentences and are the whole point of
    /// the validator. Every other exception gets a fixed sentence instead of
    /// <c>ex.Message</c> — this path deliberately does not add to TD-24, whose defect is precisely
    /// that view models render internal exception text at the user.
    /// </para>
    /// </remarks>
    [RelayCommand]
    private async Task ConfirmVoidAsync()
    {
        if (VoidTarget is null || IsVoiding) return;

        IsVoiding = true;
        VoidError = null;
        try
        {
            await _mediator.Send(new VoidTransactionCommand(VoidTarget.Id, VoidReason));

            IsVoidDialogVisible = false;
            VoidTarget = null;
            VoidReason = string.Empty;

            WeakReferenceMessenger.Default.Send(new DataChangedMessage());
            await LoadDataAsync();
        }
        catch (RequestValidationException ex)
        {
            VoidError = ex.Message;
        }
        catch (Exception)
        {
            VoidError = "The transaction could not be voided. Nothing was changed.";
        }
        finally
        {
            IsVoiding = false;
        }
    }
}
