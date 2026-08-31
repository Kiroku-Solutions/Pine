using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MediatR;
using Pine.Application.Accounts.Queries;
using Pine.Application.Transactions.Commands;
using Pine.Application.Transactions.Queries;
using Pine.Core.Accounts;
using Pine.Core.Ai;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Importing;
using Pine.Core.Transactions;
using Pine.Ui.Avalonia.ViewModels.Messages;
using Pine.Ui.Avalonia.Services;

namespace Pine.Ui.Avalonia.ViewModels;

public partial class ScanReceiptViewModel : ObservableObject
{
    private readonly IMediator _mediator;
    private readonly INavigationService _navigationService;

    [ObservableProperty]
    public partial Account? SelectedAccount { get; set; }

    partial void OnSelectedAccountChanged(Account? value)
    {
        RecomputeReconciliation();
        ClearDuplicateAcknowledgement();
    }


    [ObservableProperty]
    public partial Bitmap? ReceiptImage { get; set; }

    [ObservableProperty]
    public partial string Merchant { get; set; } = string.Empty;

    partial void OnMerchantChanged(string value) => ClearDuplicateAcknowledgement();

    [ObservableProperty]
    public partial decimal Amount { get; set; }

    [ObservableProperty]
    public partial DateTimeOffset Date { get; set; } = DateTimeOffset.Now;

    partial void OnDateChanged(DateTimeOffset value) => ClearDuplicateAcknowledgement();

    [ObservableProperty]
    public partial string Category { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Notes { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>
    /// What the scan is doing right now, shown beside the spinner while <see cref="IsBusy"/> holds.
    /// </summary>
    /// <remarks>
    /// THE SPINNER ALONE WAS NEVER THE PROBLEM, AND IS NOT THE FIX. A busy indicator already existed
    /// on this screen and was already bound to <see cref="IsBusy"/>; what it could not do was
    /// animate, because the whole scan ran on the UI thread and a blocked UI thread paints nothing.
    /// Windows drew its "not responding" overlay over the top. The pipeline now does its CPU work on
    /// the thread pool, which is what makes any of this visible -- this property only decides what
    /// the now-working indicator SAYS.
    /// </remarks>
    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;

    /// <summary>
    /// Set when the receipt about to be saved matches one already on the account (TD-20). Non-empty
    /// while unacknowledged; the user can still press save again to write it anyway -- two identical
    /// coffees on one day are two legitimate transactions, so this is a warning, never a block.
    /// </summary>
    [ObservableProperty]
    public partial string DuplicateWarning { get; set; } = string.Empty;

    /// <summary>
    /// The import key of the last duplicate the user chose to save past. Compared against the key of
    /// the transaction about to be saved: a match means "the user already saw this warning and pressed
    /// save again", so the second press proceeds. Cleared by any edit to the fields the key is derived
    /// from -- see the <c>On&lt;Property&gt;Changed</c> partial methods above -- because those edits
    /// mean the warning the user acknowledged was about a different transaction.
    /// </summary>
    private string? _acknowledgedDuplicateKey;

    private void ClearDuplicateAcknowledgement() => _acknowledgedDuplicateKey = null;

    /// <summary>True while the full-size receipt lightbox is open.</summary>
    [ObservableProperty]
    public partial bool IsImagePreviewOpen { get; set; }

    /// <summary>How much the OCR engine stands behind the extracted merchant name (TD-22).</summary>
    [ObservableProperty]
    public partial ConfidenceBand MerchantConfidence { get; private set; }

    /// <summary>How much the OCR engine stands behind the extracted total (TD-22).</summary>
    [ObservableProperty]
    public partial ConfidenceBand TotalConfidence { get; private set; }

    /// <summary>How much the OCR engine stands behind the extracted date (TD-22).</summary>
    [ObservableProperty]
    public partial ConfidenceBand DateConfidence { get; private set; }

    /// <summary>
    /// True when the extracted date has an equally plausible alternative reading (TD-21), e.g. a
    /// receipt dated 05/03/2026 could be 5 March or 3 May depending on locale.
    /// </summary>
    [ObservableProperty]
    public partial bool IsDateAmbiguous { get; private set; }

    /// <summary>
    /// The other plausible reading of <see cref="Date"/> when <see cref="IsDateAmbiguous"/> holds.
    /// </summary>
    [ObservableProperty]
    public partial DateTimeOffset? AlternativeDate { get; private set; }

    /// <summary>
    /// The sentence shown next to the date field while it is ambiguous, or null otherwise. Notified
    /// from both halves of the ambiguity state so the binding tracks either one changing.
    /// </summary>
    public string? DateAmbiguityMessage =>
        IsDateAmbiguous && AlternativeDate.HasValue
            ? $"This could also be {AlternativeDate:d MMMM yyyy}. Check the receipt."
            : null;

    partial void OnIsDateAmbiguousChanged(bool value) => OnPropertyChanged(nameof(DateAmbiguityMessage));

    partial void OnAlternativeDateChanged(DateTimeOffset? value) => OnPropertyChanged(nameof(DateAmbiguityMessage));

    public ObservableCollection<Account> Accounts { get; } = new();

    public ObservableCollection<ReceiptLineItemViewModel> LineItems { get; } = new();

    /// <summary>The sum of every line item, in receipt sign.</summary>
    [ObservableProperty]
    public partial decimal LineItemsTotal { get; private set; }

    /// <summary>
    /// What the itemisation is missing: <see cref="Amount"/> minus <see cref="LineItemsTotal"/>.
    /// Positive means the lines do not yet account for the whole bill -- the usual case, and usually
    /// tax.
    /// </summary>
    [ObservableProperty]
    public partial decimal UnreconciledDifference { get; private set; }

    /// <summary>
    /// Whether the itemisation may be saved: either there is none at all, or it adds up exactly.
    /// </summary>
    [ObservableProperty]
    public partial bool IsReconciled { get; private set; } = true;

    /// <summary>True once there is at least one line item, i.e. once reconciliation applies.</summary>
    [ObservableProperty]
    public partial bool HasLineItems { get; private set; }

    /// <summary>
    /// The kinds a user can attribute an unexplained difference to. Products are absent on purpose:
    /// the difference between what was bought and what was paid is by definition not a product.
    /// </summary>
    public IReadOnlyList<LineItemKind> AdjustmentKinds { get; } = new[]
    {
        LineItemKind.Tax,
        LineItemKind.Tip,
        LineItemKind.Discount,
        LineItemKind.Deposit,
        LineItemKind.Rounding,
        LineItemKind.Unattributed,
    };

    /// <summary>Which kind the difference will be recorded as. Tax is the overwhelmingly common one.</summary>
    [ObservableProperty]
    public partial LineItemKind SelectedAdjustmentKind { get; set; } = LineItemKind.Tax;

    public ScanReceiptViewModel(IMediator mediator, INavigationService navigationService)
    {
        _mediator = mediator;
        _navigationService = navigationService;

        // Reconciliation has to track the grid as it is edited, not only as it is loaded: the user
        // fixes a mistyped price and the difference must move with it. CollectionChanged catches
        // rows appearing and disappearing; the per-row handler catches the amounts inside them.
        LineItems.CollectionChanged += OnLineItemsChanged;

        _ = LoadAccountsAsync();

        WeakReferenceMessenger.Default.Register<ReceiptCapturedMessage>(this, async (r, m) =>
        {
            await ProcessReceiptImageAsync(m.ImageStream, m.OcrImageStream);
        });
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

        // The account decides the currency, and therefore the scale everything is rounded to.
        RecomputeReconciliation();
    }

    /// <summary>
    /// Runs the full scan over a chosen image and fills the form from what it finds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE ORDER OF THE FIRST TWO STEPS IS THE WHOLE USER EXPERIENCE. The preview bitmap is decoded
    /// and shown BEFORE the scan starts, so the instant the file dialog closes the user sees their
    /// photo. Previously nothing appeared until the entire pipeline had finished, so a five-second
    /// scan looked like a five-second hang followed by everything arriving at once.
    /// </para>
    /// <para>
    /// DECODING IS ITSELF OFF-THREAD. A photo from a modern phone is tens of megapixels, and
    /// <c>new Bitmap(stream)</c> decodes it synchronously -- easily long enough to drop frames on
    /// its own, quite apart from the neural networks afterwards. The decode happens on the thread
    /// pool and only the ASSIGNMENT happens here, back on the UI thread where the await resumes.
    /// </para>
    /// </remarks>
    public async Task ProcessReceiptImageAsync(Stream imageStream, Stream? ocrImageStream = null)
    {
        IsBusy = true;
        ErrorMessage = string.Empty;
        StatusMessage = "Loading the image...";

        // A second scan must not inherit the first scan's confidence bands or ambiguity: without
        // this reset, a clean re-scan after a weak one would keep showing the old warnings until
        // new ones happened to overwrite every field.
        MerchantConfidence = ConfidenceBand.NotFound;
        TotalConfidence = ConfidenceBand.NotFound;
        DateConfidence = ConfidenceBand.NotFound;
        IsDateAmbiguous = false;
        AlternativeDate = null;

        try
        {
            // Show the picked image immediately, decoded off the UI thread.
            imageStream.Position = 0;
            ReceiptImage = await Task.Run(() => new Bitmap(imageStream));

            // Use the OCR optimized image if available, else fallback to standard image
            var streamForOcr = ocrImageStream ?? imageStream;
            streamForOcr.Position = 0;

            // Progress arrives on the UI thread: MediatR reports it from whichever thread the
            // pipeline happens to be on, and Progress<T> captures the SynchronizationContext at
            // construction -- which is this one, because the view model is built on the UI thread.
            var progress = new Progress<ScanReceiptStage>(stage => StatusMessage = Describe(stage));

            var result = await _mediator.Send(new ScanReceiptCommand(streamForOcr, progress));

            StatusMessage = "Filling in the details...";

            Merchant = result.Merchant ?? string.Empty;
            Amount = result.TotalAmount ?? 0m;
            Date = result.Date ?? DateTimeOffset.Now;

            MerchantConfidence = result.Confidence.Merchant.Band;
            TotalConfidence = result.Confidence.Total.Band;
            DateConfidence = result.Confidence.Date.Band;
            IsDateAmbiguous = result.DateIsAmbiguous;
            AlternativeDate = result.AlternativeDate;

            LineItems.Clear();
            if (result.LineItems != null)
            {
                foreach (var li in result.LineItems)
                {
                    LineItems.Add(new ReceiptLineItemViewModel
                    {
                        // Kind first: the setters below are watched, and a Quantity or UnitPrice
                        // assignment on a row still marked Product would recompute an adjustment
                        // amount out of existence.
                        Kind = li.Kind,
                        Description = li.Description,
                        Quantity = li.Quantity,
                        UnitPrice = li.UnitPrice,
                        TotalPrice = li.TotalPrice
                    });
                }
            }

            // Suggest category if merchant is known
            if (!string.IsNullOrWhiteSpace(Merchant))
            {
                StatusMessage = "Suggesting a category...";
                var categoryName = await _mediator.Send(new Pine.Application.Transactions.Queries.CategorizeTransactionQuery(Merchant, Amount));
                if (!string.IsNullOrWhiteSpace(categoryName))
                {
                    Category = categoryName;
                }
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to process receipt: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            StatusMessage = string.Empty;
        }
    }

    /// <summary>
    /// Turns a pipeline stage into the sentence shown under the spinner.
    /// </summary>
    /// <remarks>
    /// THE WORDS LIVE HERE, NOT IN THE HANDLER. <see cref="ScanReceiptStage"/> deliberately carries
    /// no text, so that the same stage can be worded for this screen and worded differently for the
    /// live camera view, and so that translating them later (TD-45) touches presentation only.
    /// Each is phrased as something still happening, because it is: the stage is reported as it
    /// BEGINS.
    /// </remarks>
    private static string Describe(ScanReceiptStage stage) => stage switch
    {
        ScanReceiptStage.DetectingReceipt => "Looking for the receipt...",
        ScanReceiptStage.ExtractingText => "Reading the text...",
        _ => "Working...",
    };

    [RelayCommand]
    private async Task SaveTransactionAsync()
    {
        if (SelectedAccount == null)
        {
            ErrorMessage = "Please select an account.";
            return;
        }

        if (string.IsNullOrWhiteSpace(Merchant))
        {
            ErrorMessage = "Merchant is required.";
            return;
        }

        if (Amount <= 0)
        {
            ErrorMessage = "Amount must be greater than zero.";
            return;
        }

        // TD-19 at the point of intent. The aggregate and the command validator both enforce this,
        // but neither can offer the user a way out; refusing here, with the figure and the button
        // that fixes it already on screen, is the difference between an invariant and an obstacle.
        if (!IsReconciled)
        {
            ErrorMessage =
                $"The line items add up to {LineItemsTotal}, which is {UnreconciledDifference} short of the " +
                $"{Amount} total. Record the difference as an adjustment, or correct the items.";
            return;
        }

        IsBusy = true;
        ErrorMessage = string.Empty;
        DuplicateWarning = string.Empty;
        // The overlay is shared with the scan, so the save has to name itself too -- otherwise the
        // user gets a scrim with no sentence under it.
        StatusMessage = "Saving the transaction...";

        try
        {
            // For a receipt, it's typically an expense (negative amount in the DB)
            decimal finalAmount = -Amount;
            var currency = SelectedAccount.Balance.Currency.Value;

            // THE SIGNED LEDGER AMOUNT, NOT THE PRINTED ONE (TD-20). This has to be finalAmount, not
            // Amount: the key stored on the saved transaction is derived from the value actually
            // written below, and a key derived from the positive printed figure would never match it
            // on a later scan.
            var importKey = ImportKeyFactory.FromReceipt(SelectedAccount.Id, Date, finalAmount, currency, Merchant);

            // Re-checked here rather than trusted from the extraction-time check (TD-20): the user may
            // have edited the date, amount, merchant or account since then, and this is the check that
            // actually gates the write. A key equal to the one the user already saved past means this
            // is a second press with unchanged inputs, so the check is skipped and the save proceeds --
            // duplicate detection is a warning, never a silent drop, and two identical purchases on one
            // day are two legitimate transactions.
            if (!string.Equals(importKey, _acknowledgedDuplicateKey, StringComparison.Ordinal))
            {
                var duplicate = await _mediator.Send(
                    new CheckReceiptDuplicateQuery(SelectedAccount.Id, Date, finalAmount, currency, Merchant));

                if (duplicate is not null)
                {
                    // Invariant culture: the figure has to contain "24.50", not a locale-dependent
                    // "24,50", both because that is what tests (and support conversations) can rely
                    // on, and because the receipt total shown elsewhere on this screen is never
                    // localised either.
                    DuplicateWarning = string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        "This looks like a duplicate of {0:0.00} to {1} on {2:yyyy-MM-dd}. Save again to add it anyway.",
                        Math.Abs(duplicate.Amount), duplicate.Payee, duplicate.Date);
                    _acknowledgedDuplicateKey = importKey;
                    return;
                }
            }

            // NEGATED, LIKE THE TOTAL. Line items are stored in the transaction sign so that they
            // sum to it (TD-19); a receipt is an expense, so every line goes in negative. Before
            // this ticket they were sent positive against a negative total, a set that could never
            // have reconciled -- which is precisely why nothing had ever checked.
            var lineItemsCommands = LineItems.Select(li => new AddTransactionLineItemCommand(
                li.Description,
                li.Quantity,
                -li.UnitPrice,
                -li.TotalPrice,
                li.Kind
            )).ToList();

            await _mediator.Send(new AddTransactionCommand(
                SelectedAccount.Id,
                Date,
                Merchant,
                finalAmount,
                currency,
                Notes,
                Category,
                lineItemsCommands,
                importKey
            ));

            WeakReferenceMessenger.Default.Send(new DataChangedMessage());
            _navigationService.NavigateTo<TransactionsViewModel>();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            StatusMessage = string.Empty;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _navigationService.NavigateTo<TransactionsViewModel>();
    }

    [RelayCommand]
    private void Retake()
    {
        _navigationService.NavigateTo<LiveScannerViewModel>();
        WeakReferenceMessenger.Default.Send(new RetakeRequestedMessage());
    }

    [RelayCommand]
    private void ShowImagePreview()
    {
        if (ReceiptImage != null)
            IsImagePreviewOpen = true;
    }

    [RelayCommand]
    private void CloseImagePreview() => IsImagePreviewOpen = false;

    /// <summary>
    /// Swaps <see cref="Date"/> and <see cref="AlternativeDate"/> (TD-21). The extraction cannot
    /// tell which reading is correct, so this never resolves the ambiguity -- it only lets the user
    /// pick the other one, and leaves <see cref="IsDateAmbiguous"/> set so they can swap back.
    /// </summary>
    [RelayCommand]
    private void UseAlternativeDate()
    {
        if (!AlternativeDate.HasValue)
        {
            return;
        }

        (Date, AlternativeDate) = (AlternativeDate.Value, Date);
    }

    [RelayCommand]
    private void AddLineItem()
    {
        LineItems.Add(new ReceiptLineItemViewModel { Quantity = 1, UnitPrice = 0, TotalPrice = 0 });
    }

    [RelayCommand]
    private void RemoveLineItem(ReceiptLineItemViewModel item)
    {
        if (item != null)
        {
            LineItems.Remove(item);
        }
    }

    /// <summary>
    /// Records the outstanding difference as a single adjustment line of the selected kind (TD-19).
    /// </summary>
    /// <remarks>
    /// This is the affordance that makes the invariant liveable. A real receipt almost never has
    /// products that add up to the total -- tax, tip, discounts, container deposits and cash
    /// rounding all sit in between -- so demanding an exact sum without offering a one-click way to
    /// name the difference would turn a working scanner into a dead end. The amount inserted is
    /// exactly the difference, so the set reconciles the instant the row appears; the user chooses
    /// what to CALL it, never how much it is.
    /// </remarks>
    [RelayCommand]
    private void RecordDifferenceAsAdjustment()
    {
        if (UnreconciledDifference == 0m)
        {
            return;
        }

        var difference = UnreconciledDifference;
        LineItems.Add(new ReceiptLineItemViewModel
        {
            Kind = SelectedAdjustmentKind,
            Description = SelectedAdjustmentKind.ToString(),
            Quantity = 1m,
            UnitPrice = difference,
            TotalPrice = difference,
        });
    }

    partial void OnAmountChanged(decimal value)
    {
        RecomputeReconciliation();
        ClearDuplicateAcknowledgement();
    }

    private void OnLineItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var removed in e.OldItems?.OfType<ReceiptLineItemViewModel>() ?? Enumerable.Empty<ReceiptLineItemViewModel>())
        {
            removed.PropertyChanged -= OnLineItemPropertyChanged;
        }

        foreach (var added in e.NewItems?.OfType<ReceiptLineItemViewModel>() ?? Enumerable.Empty<ReceiptLineItemViewModel>())
        {
            added.PropertyChanged += OnLineItemPropertyChanged;
        }

        RecomputeReconciliation();
    }

    private void OnLineItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ReceiptLineItemViewModel.TotalPrice))
        {
            RecomputeReconciliation();
        }
    }

    /// <summary>
    /// Recomputes the itemisation total and the outstanding difference.
    /// </summary>
    /// <remarks>
    /// THE ARITHMETIC GOES THROUGH <see cref="Money"/> WHEN AN ACCOUNT IS SELECTED. The domain and
    /// the command validator both compare figures that have been normalised to the minor units of
    /// the account currency (TD-18), so comparing raw decimals here could show the user a
    /// reconciled receipt that the validator then refuses -- or, worse, an unreconciled one they
    /// cannot clear. Falling back to plain decimals before an account is chosen is safe because
    /// nothing can be saved in that state anyway.
    /// </remarks>
    private void RecomputeReconciliation()
    {
        HasLineItems = LineItems.Count > 0;

        var currency = SelectedAccount?.Balance.Currency;
        if (currency is null)
        {
            // NO ACCOUNT YET, SO NO SCALE TO ROUND TO — and this branch is deliberately NOT unified
            // with the one below. Rounding needs a currency, and the only way to have one here would
            // be to guess, which would show the user a difference computed against a scale their
            // account may not have.
            //
            // It is safe because the verdict is not load-bearing until an account exists:
            // SaveTransactionAsync refuses with "Please select an account" before it ever consults
            // IsReconciled, so every save is judged by the Money path below. What this branch feeds
            // is the figure on screen while the user is still filling the form, and computing that
            // exactly — no rounding at all — is the most honest thing to show without a currency.
            LineItemsTotal = LineItems.Sum(li => li.TotalPrice);
            UnreconciledDifference = Amount - LineItemsTotal;
        }
        else
        {
            var sum = LineItems.Aggregate(
                Money.Zero(currency),
                (running, li) => running + Money.FromRounded(li.TotalPrice, currency));
            var total = Money.FromRounded(Amount, currency);

            LineItemsTotal = sum.Amount;
            UnreconciledDifference = (total - sum).Amount;
        }

        IsReconciled = !HasLineItems || UnreconciledDifference == 0m;
    }
}
