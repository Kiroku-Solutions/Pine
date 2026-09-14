using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MediatR;
using Pine.Application.Common;
using Pine.Application.Transactions.Commands;
using Pine.Application.Transactions.Queries;
using System.IO;
using Avalonia.Media.Imaging;
using Pine.Application.Attachments;
using Pine.Core.Common;
using Pine.Core.Transactions;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.ViewModels.Messages;
using Microsoft.Extensions.Logging;
using Pine.Ui.Avalonia.Diagnostics;
using Pine.Ui.Avalonia.Services;
using Microsoft.Extensions.Options;
using Pine.Core.Configuration;

namespace Pine.Ui.Avalonia.ViewModels;

public partial class TransactionsViewModel : ObservableRecipient, IRecipient<DataChangedMessage>, IAsyncInitializable, IDisposable
{
    private readonly LocalizedMessageSlot _error;
    private readonly LocalizedMessageSlot _voidError;

    private readonly IMediator _mediator;

    /// <summary>
    /// Builds the add-transaction dialog this page opens (TD-28/TD-58).
    /// </summary>
    /// <remarks>
    /// It used to be built with <c>new</c> here, which handed it the mediator and nothing else --
    /// so its optional <c>ILogger&lt;T&gt;</c> defaulted to <see langword="null"/> and every error
    /// it showed the user was recorded nowhere. The container supplies the rest now.
    /// </remarks>
    private readonly IViewModelFactory _viewModels;

    private readonly ILogger<TransactionsViewModel>? _logger;

    [ObservableProperty]
    public partial bool IsAddTransactionDialogVisible { get; set; }

    [ObservableProperty]
    public partial AddTransactionViewModel? AddTransactionVm { get; set; }

    public ObservableCollection<Transaction> Transactions { get; } = new();

    /// <summary>
    /// The ids of the transactions that have a receipt attached, so the list can show a marker.
    /// </summary>
    /// <remarks>
    /// A SET OF IDS, NOT AN ATTACHMENT PER ROW, AND THE DIFFERENCE IS THE BLOB. The attachment
    /// table holds the file in the same row as its metadata, so anything that materialises an
    /// <c>Attachment</c> reads the photograph with it. Showing a paperclip on a page of
    /// transactions must never do that, so the page loads summaries -- which the repository
    /// projects without the content column -- and keeps only the ids.
    /// </remarks>
    public ObservableCollection<Guid> TransactionsWithReceipts { get; } = new();

    /// <summary>The receipt currently open in the viewer, if any.</summary>
    [ObservableProperty]
    public partial Bitmap? ReceiptImage { get; set; }

    /// <summary>Whether the receipt viewer is open.</summary>
    [ObservableProperty]
    public partial bool IsReceiptOpen { get; set; }

    /// <summary>The attachment the viewer is showing, so it can be removed from there.</summary>
    private Guid? _openAttachmentId;

    private int _currentPage = 1;

    /// <summary>
    /// How many transactions one page holds, from <c>PineUiOptions.TransactionsPageSize</c> (TD-27).
    /// </summary>
    /// <remarks>
    /// Was <c>const int PageSize = 30</c>, which quietly overrode the <c>PageSize = 50</c> default
    /// on <c>GetTransactionsQuery</c>. That default is now gone and this is the only page size.
    /// </remarks>
    private readonly int _pageSize;

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    /// <summary>
    /// Why the list is empty, when it is empty because something failed (TD-35).
    /// </summary>
    /// <remarks>
    /// THIS SCREEN HAD NO ERROR SURFACE AT ALL, and the page load ended in a bare
    /// <c>catch { /* Handle error silently or via status message */ }</c>. A locked vault, a
    /// corrupt database or a validation refusal all produced the same thing: an empty transaction
    /// list, which the user reads as "I have no transactions". That is a statement about their
    /// records that nothing had checked. The property is bound in <c>TransactionsView.axaml</c> and
    /// is cleared at the start of every load, so it never outlives the failure it describes.
    /// </remarks>
    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

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

    /// <summary>Creates the transaction list.</summary>
    /// <param name="mediator">The dispatcher.</param>
    /// <param name="uiOptions">Carries the page size. Optional, for tests that construct this by hand.</param>
    /// <param name="localizer">
    /// Words this page's error messages. The container injects the process-wide instance. Optional for
    /// tests that construct this page by hand, which get a private English localizer -- NOT
    /// <see cref="Localizer.Instance"/>, which a parallel test may be switching to Spanish at that moment.
    /// </param>
    /// <param name="logger">The category logger.</param>
    public TransactionsViewModel(
        IMediator mediator,
        IViewModelFactory viewModels,
        IOptions<PineUiOptions>? uiOptions = null,
        ILocalizer? localizer = null,
        ILogger<TransactionsViewModel>? logger = null)
    {
        localizer ??= new Localizer();
        _error = new LocalizedMessageSlot(localizer, text => ErrorMessage = text);
        _voidError = new LocalizedMessageSlot(localizer, text => VoidError = text);
        _mediator = mediator;
        _viewModels = viewModels;
        _pageSize = (uiOptions?.Value ?? new PineUiOptions()).TransactionsPageSize;
        _logger = logger;

        // No Register call here any more (TD-34): ObservableRecipient registers this page when the
        // navigator activates it and unregisters it on the way out, so a list the user left behind
        // no longer re-queries on every broadcast until the GC happens to collect it.
        //
        // And no `_ = LoadDataAsync()` here any more (TD-35). It made construction a race: the
        // first page of transactions arrived on a continuation nobody held, so a caller could not
        // tell a list that is empty from one that has not loaded yet. PageActivation calls
        // InitializeAsync when this page becomes the current one.
    }

    /// <summary>
    /// Reads the first page of transactions. Started by <c>PageActivation</c> on navigation.
    /// </summary>
    /// <param name="cancellationToken">Cooperative cancellation; the query itself is not cancellable yet.</param>
    /// <returns>A task that completes when the first page is on screen, or its failure is.</returns>
    public Task InitializeAsync(CancellationToken cancellationToken) => LoadDataAsync();

    /// <inheritdoc />
    public InitializationFailurePolicy InitializationFailurePolicy => new(
        _logger,
        "transactions-initialise",
        TransactionsUnavailable,
        _error.Set);

    /// <summary>What the user reads when the list cannot be read. Written once; used by both paths.</summary>
    private static readonly UserFacingMessage TransactionsUnavailable = new("Transactions.Failure.Load");

    /// <summary>Reloads the list when something elsewhere changed the data.</summary>
    /// <remarks>
    /// Observed rather than discarded (TD-35): <c>IRecipient&lt;T&gt;</c> is <c>void</c>, so this
    /// still starts work it cannot await, but the outcome is now read.
    /// </remarks>
    /// <param name="message">The broadcast. It carries no payload.</param>
    public void Receive(DataChangedMessage message)
    {
        ObservedTask.Run(
            LoadDataAsync,
            _logger,
            "transactions-reload",
            TransactionsUnavailable,
            _error.Set);
    }

    [RelayCommand]
    private async Task LoadDataAsync()
    {
        _currentPage = 1;
        Transactions.Clear();
        HasMorePages = true;

        // Cleared here rather than in LoadNextPageAsync: paging forward must not erase the
        // explanation of a page that failed, but a fresh load from the top must.
        _error.Clear();

        await LoadNextPageAsync();
    }

    [RelayCommand]
    private async Task LoadNextPageAsync()
    {
        if (IsLoading || !HasMorePages) return;

        IsLoading = true;
        try
        {
            var pagedResult = await _mediator.Send(new GetTransactionsQuery(_currentPage, _pageSize));

            foreach (var t in pagedResult.Items)
            {
                Transactions.Add(t);
            }

            await RefreshReceiptMarkersAsync();

            if (_currentPage * _pageSize >= pagedResult.TotalCount)
            {
                HasMorePages = false;
            }
            else
            {
                _currentPage++;
            }
        }
        catch (Exception ex)
        {
            // WAS: `catch { }` with the comment "Handle error silently or via status message"
            // (TD-35). It did the first and never the second, so every failure of the only query
            // this page makes produced an empty list and no record anywhere.
            _error.Set(UserFacingError.Report(
                _logger,
                ex,
                "transactions-load-page",
                TransactionsUnavailable));
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void ShowAddTransactionDialog()
    {
        AddTransactionVm = _viewModels.CreateAddTransaction(() =>
        {
            IsAddTransactionDialogVisible = false;
            AddTransactionVm = null;

            // Observed rather than discarded (TD-35). This is the refresh whose silent failure is
            // most alarming: the transaction was saved, and the list would simply not show it.
            ObservedTask.Run(
                LoadDataAsync,
                _logger,
                "transactions-reload",
                new UserFacingMessage("Transactions.Failure.ReloadAfterSave"),
                _error.Set);
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
        _voidError.Clear();
        IsVoidDialogVisible = true;
    }

    /// <summary>Closes the confirmation without voiding anything.</summary>
    [RelayCommand]
    private void CancelVoid()
    {
        IsVoidDialogVisible = false;
        VoidTarget = null;
        VoidReason = string.Empty;
        _voidError.Clear();
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
    /// Failures go through <see cref="UserFacingError"/>, which shows an exception's own words only
    /// when it implements <see cref="Pine.Core.Common.IUserFacingFailure"/> and otherwise shows the
    /// sentence written here. <see cref="RequestValidationException"/> does implement it — its
    /// messages are complete user-facing sentences and are the whole point of the validator — and so
    /// does <see cref="Pine.Core.Common.ConcurrencyConflictException"/>, which this screen can
    /// genuinely provoke: voiding a transfer moves two balances, and a concurrent write to either of
    /// them now tells the user to reload rather than disappearing into "could not be voided" (TD-98).
    /// </para>
    /// </remarks>
    [RelayCommand]
    private async Task ConfirmVoidAsync()
    {
        if (VoidTarget is null || IsVoiding) return;

        IsVoiding = true;
        _voidError.Clear();
        try
        {
            await _mediator.Send(new VoidTransactionCommand(VoidTarget.Id, VoidReason));

            IsVoidDialogVisible = false;
            VoidTarget = null;
            VoidReason = string.Empty;

            // THE RELOAD USED TO BE HERE TOO, AND IT RAN TWICE (TD-34). This view model is itself
            // an IRecipient<DataChangedMessage>, and the broadcast below is synchronous, so the
            // line that followed it -- `await LoadDataAsync();` -- was the SECOND full reload of
            // the same list: two round trips, two Transactions.Clear() calls, a visible flicker.
            // The broadcast stays, because the dashboard and its widgets have no other way to
            // learn a transaction was voided; only the duplicate self-refresh is gone.
            WeakReferenceMessenger.Default.Send(new DataChangedMessage());
        }
        catch (Exception ex)
        {
            _voidError.Set(UserFacingError.Report(
                _logger,
                ex,
                "void-transaction",
                new UserFacingMessage("Transactions.Failure.Void")));
        }
        finally
        {
            IsVoiding = false;
        }
    }

    /// <summary>
    /// Opens the receipt attached to a transaction.
    /// </summary>
    /// <param name="transaction">The row whose receipt to show.</param>
    /// <remarks>
    /// <para>
    /// THE CONTENT IS READ HERE AND NOWHERE EARLIER. Loading the page reads summaries only; the
    /// bytes are fetched when a user asks to see one, which is the whole reason
    /// <c>IAttachmentRepository</c> separates the two operations.
    /// </para>
    /// <para>
    /// The query verifies the stored hash and throws <c>AttachmentCorruptedException</c> when it
    /// does not match. That exception implements <c>IUserFacingFailure</c>, so
    /// <c>UserFacingError.Report</c> shows its sentence verbatim rather than the generic fallback --
    /// the user is told the image does not match its fingerprint and that the transaction is fine.
    /// </para>
    /// </remarks>
    [RelayCommand]
    private async Task ViewReceiptAsync(Transaction? transaction)
    {
        if (transaction is null)
        {
            return;
        }

        try
        {
            var summaries = await _mediator.Send(new GetTransactionAttachmentsQuery(transaction.Id));

            if (summaries.Count == 0)
            {
                return;
            }

            var content = await _mediator.Send(new GetAttachmentContentQuery(summaries[0].Id));

            if (content is null)
            {
                return;
            }

            using var buffer = new MemoryStream(content.Content);
            ReceiptImage = new Bitmap(buffer);
            _openAttachmentId = summaries[0].Id;
            IsReceiptOpen = true;
        }
        catch (Exception ex)
        {
            _voidError.Set(UserFacingError.Report(
                _logger,
                ex,
                "attachment-view",
                new UserFacingMessage("Transactions.Failure.OpenReceipt")));
        }
    }

    /// <summary>Closes the receipt viewer.</summary>
    [RelayCommand]
    private void CloseReceipt()
    {
        IsReceiptOpen = false;
        ReceiptImage = null;
        _openAttachmentId = null;
    }

    /// <summary>
    /// Removes the receipt currently open in the viewer.
    /// </summary>
    /// <remarks>
    /// DELETING EVIDENCE IS A DELIBERATE ACT AND IS TREATED AS ONE: it is reachable only from the
    /// open viewer, so the user is looking at the image they are about to remove. The transaction
    /// is untouched -- Pine voids ledger rows rather than deleting them, and removing a receipt is
    /// not a correction to the figure.
    /// </remarks>
    [RelayCommand]
    private async Task RemoveReceiptAsync()
    {
        if (_openAttachmentId is not { } attachmentId)
        {
            return;
        }

        try
        {
            await _mediator.Send(new RemoveAttachmentCommand(attachmentId));
            CloseReceipt();
            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            _voidError.Set(UserFacingError.Report(
                _logger,
                ex,
                "attachment-remove",
                new UserFacingMessage("Transactions.Failure.RemoveReceipt")));
        }
    }

    /// <summary>
    /// Refreshes which of the loaded transactions carry a receipt.
    /// </summary>
    /// <remarks>
    /// One query per loaded page rather than one per row: the summaries are cheap because the
    /// projection leaves the blob behind, but a query per row on a fifty-row page would still be
    /// fifty round trips.
    /// </remarks>
    private async Task RefreshReceiptMarkersAsync()
    {
        TransactionsWithReceipts.Clear();

        foreach (var transaction in Transactions)
        {
            var summaries = await _mediator.Send(new GetTransactionAttachmentsQuery(transaction.Id));

            if (summaries.Count > 0)
            {
                TransactionsWithReceipts.Add(transaction.Id);
            }
        }
    }

    /// <summary>Releases the localized message slots.</summary>
    /// <remarks>
    /// CA1001, for the reason every page that owns a slot gives: each subscribes to the process-lifetime
    /// localizer. <c>PageActivation.Deactivate</c> disposes a page that implements this.
    /// </remarks>
    public void Dispose()
    {
        _error.Dispose();
        _voidError.Dispose();
        GC.SuppressFinalize(this);
    }
}
