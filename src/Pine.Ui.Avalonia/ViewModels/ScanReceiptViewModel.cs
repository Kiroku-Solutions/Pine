using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MediatR;
using Pine.Application.Accounts.Queries;
using Pine.Application.Budgets.Queries;
using Pine.Application.Transactions.Commands;
using Pine.Application.Transactions.Queries;
using Pine.Core.Accounts;
using Pine.Core.Ai;
using Pine.Core.Common;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Importing;
using Pine.Core.Transactions;
using Pine.Ui.Avalonia.ViewModels.Messages;
using Pine.Ui.Avalonia.Services;
using Microsoft.Extensions.Logging;
using Pine.Ui.Avalonia.Diagnostics;
using Pine.Ui.Avalonia.Localization;

namespace Pine.Ui.Avalonia.ViewModels;

public partial class ScanReceiptViewModel : ObservableRecipient, IRecipient<ReceiptCapturedMessage>, IAsyncInitializable, IDisposable
{
    private readonly IMediator _mediator;
    private readonly LocalizedMessageSlot _status;
    private readonly LocalizedMessageSlot _error;

    /// <summary>
    /// Words <see cref="DuplicateWarning"/>, and words it again on a language change (TD-21). The figure
    /// and the date inside it are arguments, so they are written in the sentence's own culture.
    /// </summary>
    private readonly LocalizedMessageSlot _duplicate;

    /// <summary>Words <see cref="DateAmbiguityMessage"/>, which is computed on read.</summary>
    private readonly ILocalizer _localizer;
    /// <summary>
    /// The shell this page is shown in (TD-121). It was the window's navigator, so Save and Cancel
    /// replaced the whole shell with a bare transactions page -- which a vault lock then failed to
    /// clear, because the lock handler, at the time, cleared only a page inside the shell. (It now
    /// clears anything that is not a pre-vault screen.)
    /// </summary>
    private readonly IShellNavigator _navigationService;

    /// <summary>
    /// Builds the pages this screen navigates to (TD-28). Named methods rather than a generic
    /// resolve, so a page this head cannot serve is a compile-time surface a test can enumerate
    /// instead of a throw on the user's click.
    /// </summary>
    private readonly IViewModelFactory _viewModels;
    private readonly ILogger<ScanReceiptViewModel>? _logger;

    /// <summary>The platform camera, or null when this head has none (Windows uses the live scanner page instead).</summary>
    private readonly ICameraSource? _camera;
    private readonly object _cameraLock = new();
    private CancellationTokenSource? _cameraCts;
    private ICameraSession? _cameraSession;
    private ICameraFrame? _latestFrame;
    private readonly LocalizedMessageSlot _live;
    private byte[]? _bestJpeg;
    private int _bestScore = -1;
    private int _analysing;

    [ObservableProperty]
    public partial Account? SelectedAccount { get; set; }

    partial void OnSelectedAccountChanged(Account? value)
    {
        RecomputeReconciliation();
        ClearDuplicateAcknowledgement();
    }


    [ObservableProperty]
    public partial Bitmap? ReceiptImage { get; set; }

    /// <summary>
    /// The scanned image as stored bytes, kept until the transaction is saved.
    /// </summary>
    /// <remarks>
    /// A byte array rather than the <see cref="Stream"/> that arrived: the streams carried by
    /// <c>ReceiptCapturedMessage</c> are disposed as soon as the scan is done (TD-126), so none can be
    /// held here. Copying costs one buffer on a path that has already decoded a bitmap.
    /// </remarks>
    private byte[]? _receiptBytes;

    /// <summary>
    /// The vault's base currency, whose scale the reconciliation uses while no account is selected
    /// (TD-100). Null until loaded, or in a vault with no account and no explicit setting.
    /// </summary>
    private CurrencyCode? _baseCurrency;

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
    /// <remarks>
    /// IT WAS AN ENGLISH SENTENCE WITH A FORMAT PATTERN IN IT, <c>d MMMM yyyy</c>, and the pattern was the
    /// harder half (TD-21, TD-150). Month names came from the operating system's culture, so an English
    /// screen on a Spanish machine read "This could also be 3 mayo 2026"; and the pattern's day-month-year
    /// order was fixed, where Spanish needs "3 de mayo de 2026" and American English "May 3, 2026". The
    /// sentence is a key now and the date a <c>{0:D}</c> argument: the culture's own long date, in the
    /// language of the sentence around it. For a warning whose whole job is to make a reader compare two
    /// dates, the unambiguous long form with the weekday is the right one.
    /// </remarks>
    public string? DateAmbiguityMessage =>
        IsDateAmbiguous && AlternativeDate.HasValue
            ? _localizer.Format("ScanReceipt.DateAmbiguity", AlternativeDate.Value)
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

    public ScanReceiptViewModel(
        IMediator mediator,
        IShellNavigator navigationService,
        IViewModelFactory viewModels,
        ILocalizer localizer,
        ILogger<ScanReceiptViewModel>? logger = null,
        Pine.Ui.Avalonia.Composition.PineHeadCapabilities? capabilities = null,
        ICameraSource? camera = null)
    {
        HasLiveScanner = capabilities?.HasReceiptVision ?? true;
        _camera = camera;
        HasCameraCapture = camera is not null && !HasLiveScanner;
        ArgumentNullException.ThrowIfNull(localizer);
        _mediator = mediator;
        _navigationService = navigationService;
        _viewModels = viewModels;
        _localizer = localizer;
        _status = new LocalizedMessageSlot(localizer, text => StatusMessage = text ?? string.Empty);
        _error = new LocalizedMessageSlot(localizer, text => ErrorMessage = text ?? string.Empty);
        _live = new LocalizedMessageSlot(localizer, text => LiveStatus = text ?? string.Empty);
        _duplicate = new LocalizedMessageSlot(localizer, text => DuplicateWarning = text ?? string.Empty);
        _localizer.LanguageChanged += OnLanguageChanged;
        _logger = logger;

        // Reconciliation has to track the grid as it is edited, not only as it is loaded: the user
        // fixes a mistyped price and the difference must move with it. CollectionChanged catches
        // rows appearing and disappearing; the per-row handler catches the amounts inside them.
        LineItems.CollectionChanged += OnLineItemsChanged;
    }

    /// <inheritdoc />
    public Task InitializeAsync(CancellationToken cancellationToken) => LoadAccountsAsync();

    /// <inheritdoc />
    public InitializationFailurePolicy InitializationFailurePolicy => new(
        _logger,
        "scan-accounts-load",
        AccountsUnavailable,
        _error.Set);

    /// <summary>What the user reads when the account list behind the Save button cannot load.</summary>
    /// <remarks>
    /// Loading the accounts used to be a discarded task in the constructor, and
    /// <c>LoadAccountsAsync</c> has no <c>catch</c> (TD-35). The failure was therefore invisible
    /// until the user had scanned a receipt, filled the form and pressed Save, at which point the
    /// screen said "Please select an account." about a list that had never loaded.
    /// </remarks>
    private static readonly UserFacingMessage AccountsUnavailable = new("ScanReceipt.Failure.AccountsLoad");

    /// <summary>
    /// The view model's logger, for the code-behind that reports a failure through it (TD-127). A view is built by the XAML loader with no container reach, so it borrows the logger of the view model it is the face of.
    /// </summary>
    internal Microsoft.Extensions.Logging.ILogger? Logger => _logger;

    /// <summary>
    /// Shows <paramref name="messages"/> as this page's error, in the active language, and keeps them
    /// in step with a language change. For the view's code-behind, which has no localizer (TD-139).
    /// </summary>
    /// <param name="messages">What <c>ObservedTask</c> produced.</param>
    public void ReportFailure(IReadOnlyList<UserFacingMessage> messages) => _error.Set(messages);

    /// <summary>
    /// Starts the scan for a receipt captured elsewhere (the live scanner, or the crop screen).
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE HANDLER THIS REPLACED WAS <c>async void</c> IN EVERYTHING BUT NAME (TD-34). It was
    /// registered as <c>async (r, m) =&gt; await ProcessReceiptImageAsync(...)</c>; the messenger
    /// invokes handlers synchronously and discards whatever they return, so the <c>Task</c> was
    /// never observed and any exception inside it reached the runtime as an unobserved fault rather
    /// than the user as a message. It also captured <c>this</c>, which kept every instance the user
    /// ever navigated to alive and subscribed -- three visits to the scanner meant one captured
    /// receipt was processed three times.
    /// </para>
    /// <para>
    /// <see cref="Receive"/> must stay <c>void</c> -- that is the <c>IRecipient&lt;T&gt;</c>
    /// contract -- so the work still has to be started rather than awaited. It is started through
    /// <see cref="ProcessCapturedReceiptAsync"/> instead of a bare discard because
    /// <see cref="ProcessReceiptImageAsync"/>'s own try/catch does not open until after several
    /// property assignments; a throw from those (a disposed stream, say) would otherwise escape
    /// into the same nowhere the old handler sent it.
    /// </para>
    /// </remarks>
    /// <param name="message">The captured image, and optionally an OCR-enhanced copy of it.</param>
    public void Receive(ReceiptCapturedMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        _ = ProcessCapturedReceiptAsync(message);
    }

    /// <summary>
    /// The try/catch wrapper that makes the discard in <see cref="Receive"/> survivable.
    /// </summary>
    /// <remarks>
    /// Deliberately small and local. A general "start this and never lose the exception" helper is
    /// TD-35's subject and would be the wrong thing to invent here; this only has to guarantee that
    /// nothing thrown while handling a message leaves the view model, and that whatever went wrong
    /// reaches the user through the same <see cref="ErrorMessage"/> the rest of the screen uses.
    /// </remarks>
    private async Task ProcessCapturedReceiptAsync(ReceiptCapturedMessage message)
    {
        try
        {
            await ProcessReceiptImageAsync(message.ImageStream, message.OcrImageStream);
        }
        catch (Exception ex)
        {
            _error.Set(UserFacingError.Report(
                _logger,
                ex,
                "receipt-process",
                new UserFacingMessage("ScanReceipt.Failure.CapturedImage")));
            IsBusy = false;
        }
        finally
        {
            // THE STREAMS ARE OURS (TD-126): ownership travelled with the message. Nothing below this
            // point keeps either one -- the bitmap is decoded and the bytes are copied -- so this is the
            // one place they are released, on success and on failure alike.
            message.ImageStream.Dispose();
            message.OcrImageStream?.Dispose();
        }
    }

    private async Task LoadAccountsAsync()
    {
        var accounts = await _mediator.Send(new GetAccountsQuery());
        foreach (var acc in accounts)
        {
            Accounts.Add(acc);
        }

        // THE SCALE FOR THE NO-ACCOUNT CASE (TD-100, decided 2026-10-07): the vault's base currency.
        var baseCode = await _mediator.Send(new GetBaseCurrencyQuery());
        _baseCurrency = !string.IsNullOrWhiteSpace(baseCode) && CurrencyCode.TryCreate(baseCode, out var parsed)
            ? parsed
            : null;

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
    /// <summary>
    /// The scanned receipt as an attachment for the save command, or <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// The file name carries the merchant and the date because that is what makes an exported or
    /// restored file identifiable without the database beside it. It is sanitised: a merchant name
    /// is OCR output and can contain anything.
    /// </remarks>
    private AddTransactionAttachmentCommand? BuildAttachment()
    {
        if (_receiptBytes is not { Length: > 0 } bytes)
        {
            return null;
        }

        var merchant = string.Concat((Merchant ?? string.Empty)
            .Where(c => char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_'))
            .Trim();

        if (merchant.Length == 0)
        {
            merchant = "receipt";
        }

        var name = string.Create(
            CultureInfo.InvariantCulture,
            $"{merchant}-{Date:yyyy-MM-dd}.png");

        return new AddTransactionAttachmentCommand(name, "image/png", bytes);
    }

    public async Task ProcessReceiptImageAsync(Stream imageStream, Stream? ocrImageStream = null)
    {
        IsBusy = true;
        _error.Clear();
        _status.Set("ScanReceipt.Status.LoadingImage");

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

            // KEEP THE BYTES, WHICH IS THE HALF OF THIS FEATURE THAT NEVER EXISTED. Until now the
            // image was decoded into a Bitmap for the screen and then dropped: scanning a receipt
            // produced a transaction and threw the document away, which is what §10 Phase 3 meant
            // by "the missing half of the OCR feature's audit value". The DISPLAY image is kept
            // rather than the OCR one, because the OCR image is a deskewed, thresholded derivative
            // produced for a machine -- what belongs in an audit trail is the document a person
            // would recognise.
            imageStream.Position = 0;
            _receiptBytes = await Task.Run(() =>
            {
                using var buffer = new MemoryStream();
                imageStream.CopyTo(buffer);
                return buffer.ToArray();
            });

            // Use the OCR optimized image if available, else fallback to standard image
            var streamForOcr = ocrImageStream ?? imageStream;
            streamForOcr.Position = 0;

            // Progress arrives on the UI thread: MediatR reports it from whichever thread the
            // pipeline happens to be on, and Progress<T> captures the SynchronizationContext at
            // construction -- which is this one, because the view model is built on the UI thread.
            var progress = new Progress<ScanReceiptStage>(stage => _status.Set(Describe(stage)));

            var result = await _mediator.Send(new ScanReceiptCommand(streamForOcr, progress));

            _status.Set("ScanReceipt.Status.FillingDetails");

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
                        // OCR input is ROUNDED to the store's scale; a person's own entry is rejected by the
                        // validator instead (LineQuantity, TD-95).
                        Quantity = Pine.Core.Transactions.LineQuantity.RoundRecognised(li.Quantity),
                        UnitPrice = li.UnitPrice,
                        TotalPrice = li.TotalPrice
                    });
                }
            }

            // Suggest category if merchant is known
            if (!string.IsNullOrWhiteSpace(Merchant))
            {
                _status.Set("ScanReceipt.Status.SuggestingCategory");
                var categoryName = await _mediator.Send(new Pine.Application.Transactions.Queries.CategorizeTransactionQuery(Pine.Application.Services.CategorisationText.Build(Merchant, null), Amount));
                if (!string.IsNullOrWhiteSpace(categoryName))
                {
                    Category = categoryName;
                }
            }
        }
        catch (Exception ex)
        {
            // WAS: $"Failed to process receipt: {ex.Message}" (TD-24). The OCR stack throws through
            // PaddleInference and Skia, whose messages name native library paths and model files.
            _error.Set(UserFacingError.Report(
                _logger,
                ex,
                "receipt-process",
                new UserFacingMessage("ScanReceipt.Failure.Process")));
        }
        finally
        {
            IsBusy = false;
            _status.Clear();
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
        ScanReceiptStage.DetectingReceipt => "ScanReceipt.Status.DetectingReceipt",
        ScanReceiptStage.ExtractingText => "ScanReceipt.Status.ExtractingText",
        _ => "ScanReceipt.Status.Working",
    };

    [RelayCommand]
    private async Task SaveTransactionAsync()
    {
        if (SelectedAccount == null)
        {
            _error.Set("ScanReceipt.Error.NoAccount");
            return;
        }

        if (string.IsNullOrWhiteSpace(Merchant))
        {
            _error.Set("ScanReceipt.Error.MerchantRequired");
            return;
        }

        if (Amount <= 0)
        {
            _error.Set("ScanReceipt.Error.AmountPositive");
            return;
        }

        // TD-19 at the point of intent. The aggregate and the command validator both enforce this,
        // but neither can offer the user a way out; refusing here, with the figure and the button
        // that fixes it already on screen, is the difference between an invariant and an obstacle.
        if (!IsReconciled)
        {
            _error.Set("ScanReceipt.Error.Unreconciled", LineItemsTotal, UnreconciledDifference, Amount);
            return;
        }

        IsBusy = true;
        _error.Clear();
        _duplicate.Clear();
        // The overlay is shared with the scan, so the save has to name itself too -- otherwise the
        // user gets a scrim with no sentence under it.
        _status.Set("ScanReceipt.Status.Saving");

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
                    // IN THE SCREEN'S CULTURE, AND IT WAS DELIBERATELY NOT (TD-21). This used invariant
                    // culture on the grounds that the rest of the screen was not localised either --
                    // true then, and the reason this row existed. With the whole screen following the
                    // language, an invariant "24.50" and "2026-05-03" beside a total the NumericUpDown
                    // writes as "24,50" would be the one figure on the screen in a different notation.
                    // The amount carries its currency, so its scale is the currency's rather than an
                    // assumed two decimals. The non-throwing factories on purpose: `new Money` and
                    // `new CurrencyCode` refuse an out-of-scale amount or a malformed stored code, and a
                    // WARNING must not turn a save into a failure.
                    _duplicate.Set(
                        "ScanReceipt.DuplicateWarning",
                        Money.FromRounded(Math.Abs(duplicate.Amount), CurrencyCode.FromStoredValue(currency)),
                        duplicate.Payee,
                        duplicate.Date);
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
                importKey,
                BuildAttachment()
            ));

            WeakReferenceMessenger.Default.Send(new DataChangedMessage());

            // Only if the user is still here (TD-121). The save is awaited, and the sidebar is beside
            // this page, so the user can have gone elsewhere by the time it lands; pulling them onto
            // Transactions then would be a navigation they never asked for. The broadcast above still
            // goes out either way -- the transaction WAS written.
            if (IsActive)
            {
                _navigationService.NavigateTo(_viewModels.CreateTransactions());
            }
        }
        catch (Exception ex)
        {
            // WAS: $"Error: {ex.Message}" (TD-24). Same write path as AddTransactionViewModel, so
            // the same guarantee: ScanReceiptCommand runs inside TransactionBehavior's transaction,
            // and a ConcurrencyConflictException now arrives with its "Reload and try again."
            // instruction intact instead of being flattened (TD-98).
            _error.Set(UserFacingError.Report(
                _logger,
                ex,
                "receipt-save-transaction",
                new UserFacingMessage("ScanReceipt.Failure.Save")));
        }
        finally
        {
            IsBusy = false;
            _status.Clear();
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _navigationService.NavigateTo(_viewModels.CreateTransactions());
    }

    /// <summary>
    /// Whether the Retake button exists. It opens the live scanner, so a head with no live camera (an
    /// OCR-only head such as Android) does not show it; choosing another file is the retake there.
    /// </summary>
    public bool HasLiveScanner { get; }

    /// <summary>
    /// Whether this page offers to take the receipt photo with the device camera itself (Android, docs/65 phase D):
    /// the head registers an <see cref="ICameraSource"/> but has no full live scanner. False on Windows, where the
    /// Retake button opens the live scanner page, and on any head without a camera.
    /// </summary>
    public bool HasCameraCapture { get; }

    /// <summary>True while the camera is open and the viewfinder is showing.</summary>
    [ObservableProperty]
    public partial bool IsCameraOpen { get; private set; }

    /// <summary>The most recent camera frame, for the viewfinder. Never saved: frames live in memory only.</summary>
    [ObservableProperty]
    public partial WriteableBitmap? LiveFeed { get; private set; }

    /// <summary>What the live scan has read so far (merchant, date, total), or that it is still looking.</summary>
    [ObservableProperty]
    public partial string LiveStatus { get; private set; } = string.Empty;

    /// <summary>Starts the live scan (opens the camera). The sidebar and dashboard entries call this after navigating here.</summary>
    public void StartLiveScan() => OpenCamera();

    /// <summary>One analysed frame every ~1.5 s: slow enough for a mid-range tablet, and never two at once.</summary>
    private static readonly TimeSpan LiveAnalysisInterval = TimeSpan.FromMilliseconds(1500);

    private async Task AnalyseLiveFrameAsync(byte[] jpeg, CancellationToken token)
    {
        try
        {
            using var stream = new MemoryStream(jpeg, writable: false);
            var result = await _mediator.Send(new ScanReceiptCommand(stream), token).ConfigureAwait(false);
            if (token.IsCancellationRequested)
            {
                return;
            }

            var score = (string.IsNullOrWhiteSpace(result.Merchant) ? 0 : 1)
                + (result.TotalAmount is > 0 ? 2 : 0)
                + (result.Date is not null ? 1 : 0);
            lock (_cameraLock)
            {
                // Only the best frame is kept (one JPEG in memory); a later frame with an equal score replaces it.
                if (score >= _bestScore && score > 0)
                {
                    _bestScore = score;
                    _bestJpeg = jpeg;
                }
            }

            var merchant = string.IsNullOrWhiteSpace(result.Merchant) ? "-" : result.Merchant;
            var date = result.Date is { } d ? d.ToString("d", CultureInfo.CurrentCulture) : "-";
            var total = result.TotalAmount is > 0 ? result.TotalAmount.Value.ToString("N2", CultureInfo.CurrentCulture) : "-";
            Post(() =>
            {
                if (IsCameraOpen)
                {
                    _live.Set(score == 0 ? "ScanReceipt.Live.Looking" : "ScanReceipt.Live.FoundFormat", merchant, date, total);
                }
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            // A frame that cannot be read is not an error worth showing; the next one gets another go.
            _ = UserFacingError.Report(_logger, ex, "live-frame", new UserFacingMessage("ScanReceipt.Failure.CapturedImage"));
        }
        finally
        {
            Interlocked.Exchange(ref _analysing, 0);
        }
    }

    [RelayCommand(CanExecute = nameof(HasCameraCapture))]
    private void OpenCamera()
    {
        if (_camera is null || IsCameraOpen || IsBusy)
        {
            return;
        }

        _error.Clear();
        _bestJpeg = null;
        _bestScore = -1;
        _live.Set("ScanReceipt.Live.Looking");
        IsCameraOpen = true;
        _status.Set("LiveScanner.Status.OpeningCamera");
        var cts = new CancellationTokenSource();
        _cameraCts = cts;
        _ = Task.Run(() => CameraLoopAsync(_camera, cts.Token));
    }

    /// <summary>Opens the camera (the Android head asks for the CAMERA permission here) and feeds the viewfinder until closed.</summary>
    private async Task CameraLoopAsync(ICameraSource camera, CancellationToken token)
    {
        try
        {
            var session = camera.OpenDefault();
            if (session is null)
            {
                Post(() =>
                {
                    CloseCamera();
                    _error.Set("LiveScanner.Status.CameraFailed");
                });
                return;
            }

            lock (_cameraLock)
            {
                if (token.IsCancellationRequested)
                {
                    session.Dispose();
                    return;
                }

                _cameraSession = session;
            }

            Post(() => _status.Clear());
            var sinceAnalysis = System.Diagnostics.Stopwatch.StartNew();

            while (!token.IsCancellationRequested)
            {
                ICameraFrame? frame;
                lock (_cameraLock)
                {
                    frame = _cameraSession?.ReadFrame();
                    if (_cameraSession is null)
                    {
                        break;
                    }
                }

                if (frame is null)
                {
                    await Task.Delay(40, token).ConfigureAwait(false);
                    continue;
                }

                if (sinceAnalysis.Elapsed >= LiveAnalysisInterval && Interlocked.CompareExchange(ref _analysing, 1, 0) == 0)
                {
                    sinceAnalysis.Restart();
                    var jpeg = frame.EncodeJpeg();
                    _ = Task.Run(() => AnalyseLiveFrameAsync(jpeg, token), token);
                }

                var bitmap = ToBitmap(frame);
                lock (_cameraLock)
                {
                    _latestFrame?.Dispose();
                    _latestFrame = frame;
                }

                Post(() => LiveFeed = bitmap);

                // About 6 frames a second is plenty for a viewfinder and keeps the CPU free for the scan itself.
                await Task.Delay(150, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            var failure = UserFacingError.Report(_logger, ex, "camera-loop", new UserFacingMessage("ScanReceipt.Failure.CameraReadFormat", ex.GetType().Name));
            Post(() =>
            {
                CloseCamera();
                _error.Set(failure);
            });
        }
    }

    private static WriteableBitmap ToBitmap(ICameraFrame frame)
    {
        var bitmap = new WriteableBitmap(
            new global::Avalonia.PixelSize(frame.Width, frame.Height),
            new global::Avalonia.Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);

        using var buffer = bitmap.Lock();
        unsafe
        {
            frame.CopyBgraTo(new Span<byte>((void*)buffer.Address, buffer.RowBytes * frame.Height), buffer.RowBytes);
        }

        return bitmap;
    }

    private static void Post(Action action) => global::Avalonia.Threading.Dispatcher.UIThread.Post(action);

    /// <summary>Takes the frame on screen, closes the camera and runs it through the same pipeline as a picked file.</summary>
    [RelayCommand]
    private async Task CapturePhotoAsync()
    {
        byte[]? jpeg;
        lock (_cameraLock)
        {
            jpeg = _bestJpeg ?? _latestFrame?.EncodeJpeg();
        }

        CloseCamera();
        if (jpeg is null)
        {
            _error.Set("ScanReceipt.Failure.CapturedImage");
            return;
        }

        try
        {
            using var stream = new MemoryStream(jpeg, writable: false);
            await ProcessReceiptImageAsync(stream);
        }
        catch (Exception ex)
        {
            _error.Set(UserFacingError.Report(
                _logger,
                ex,
                "receipt-process",
                new UserFacingMessage("ScanReceipt.Failure.CapturedImage")));
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CloseCamera()
    {
        _cameraCts?.Cancel();
        _cameraCts?.Dispose();
        _cameraCts = null;
        lock (_cameraLock)
        {
            _cameraSession?.Dispose();
            _cameraSession = null;
            _latestFrame?.Dispose();
            _latestFrame = null;
            _bestJpeg = null;
            _bestScore = -1;
        }

        IsCameraOpen = false;
        _live.Clear();
        LiveFeed = null;
        _status.Clear();
    }

    [RelayCommand(CanExecute = nameof(HasLiveScanner))]
    private void Retake()
    {
        _navigationService.NavigateTo(_viewModels.CreateLiveScanner(_navigationService));
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
            // Was SelectedAdjustmentKind.ToString(), the raw enum member name ("Tax", "Rounding",
            // ...) written straight into the row the user then saves (TD-150). Now the same
            // LineItemKind.* keys Converters.LineItemKindDisplay uses for the picker, so the saved
            // row reads in the active language instead of always in English.
            Description = _localizer[$"LineItemKind.{SelectedAdjustmentKind}"],
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
    /// cannot clear. Before an account is chosen the vault's base currency supplies the scale (TD-100);
    /// plain decimals remain only for a vault with neither an account nor a base currency.
    /// </remarks>
    private void RecomputeReconciliation()
    {
        HasLineItems = LineItems.Count > 0;

        // The selected account's currency, else the vault's base currency (TD-100): so the figure shown
        // before an account is chosen is rounded to a real scale, the one an account in the base
        // currency would use, rather than summed exactly.
        var currency = SelectedAccount?.Balance.Currency ?? _baseCurrency;
        if (currency is null)
        {
            // NO ACCOUNT AND NO BASE CURRENCY (an empty vault): there is genuinely no scale to round to,
            // and nothing can be saved in that state anyway, so the exact figure is the honest one.
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
        CloseCamera();
        _localizer.LanguageChanged -= OnLanguageChanged;
        _status.Dispose();
        _error.Dispose();
        _live.Dispose();
        _duplicate.Dispose();
        GC.SuppressFinalize(this);
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => OnPropertyChanged(nameof(DateAmbiguityMessage));

}
