using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediatR;
using Microsoft.Extensions.Logging;
using Pine.Application.Sync.Commands;
using Pine.Application.Sync.Queries;
using Pine.Core.Common;
using Pine.Core.Sync;
using Pine.Ui.Avalonia.Diagnostics;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.Services;

namespace Pine.Ui.Avalonia.ViewModels;

/// <summary>
/// The pairing page: one device shows a code, the other types it, and both end up holding the same sync key
/// (post-audit plan item 3.1e; TD-154, `14-Synchronization` §5).
/// </summary>
/// <remarks>
/// <para>
/// ONE PAGE, TWO HALVES, BECAUSE THE USER DOES NOT KNOW WHICH SIDE THEY ARE ON UNTIL THEY LOOK. "Show a code" is
/// for the device that has the key; "Enter a code" is for the one that has not. Either half works on either
/// device — a vault with no key can still offer one, because making the first key is what the first vault does —
/// and the page says which of the two this vault is by showing its fingerprint, or saying it has none.
/// </para>
/// <para>
/// THE FINGERPRINT IS ON BOTH HALVES ON PURPOSE. After pairing, the two devices are meant to read the same sixteen
/// characters. Nothing else in the product would ever tell the user that two vaults hold different keys: envelopes
/// would simply be refused, weeks later, with a message about a key id.
/// </para>
/// <para>
/// THE CODE IS NEVER PUT ANYWHERE BUT ON THE SCREEN. It is not logged, not copied to the clipboard by this page,
/// and not written to the offer; a refusal from below never quotes it.
/// </para>
/// </remarks>
public partial class SyncPairingViewModel : ObservableObject, IAsyncInitializable, IDisposable
{
    private static readonly UserFacingMessage LoadFailed = new("SyncPairing.Failure.Load");
    private static readonly UserFacingMessage OfferFailed = new("SyncPairing.Failure.Offer");
    private static readonly UserFacingMessage AcceptFailed = new("SyncPairing.Failure.Accept");

    private readonly IMediator _mediator;
    private readonly ILocalizer _localizer;
    private readonly ILogger<SyncPairingViewModel>? _logger;
    private readonly LocalizedMessageSlot _error;
    private readonly LocalizedMessageSlot _offerError;
    private readonly LocalizedMessageSlot _acceptError;

    /// <summary>What the page was last told, so a language change can word it again.</summary>
    private SyncPairingStatus? _status;
    private DateTimeOffset? _offerExpiresAtUtc;
    private string? _pairedFingerprint;

    /// <param name="mediator">The request pipeline.</param>
    /// <param name="localizer">The string table; defaults to the process's, as every page's does.</param>
    /// <param name="logger">Diagnostics.</param>
    public SyncPairingViewModel(IMediator mediator, ILocalizer? localizer = null, ILogger<SyncPairingViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(mediator);
        _mediator = mediator;
        _localizer = localizer ?? Localizer.Instance;
        _logger = logger;
        _error = new LocalizedMessageSlot(_localizer, text => ErrorMessage = text);
        _offerError = new LocalizedMessageSlot(_localizer, text => OfferMessage = text ?? string.Empty);
        _acceptError = new LocalizedMessageSlot(_localizer, text => AcceptMessage = text ?? string.Empty);
        _localizer.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>Whether the page is reading this vault's pairing state.</summary>
    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    /// <summary>Why the page could not read this vault's state, in the user's language; null when it could.</summary>
    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    /// <summary>Whether this vault already holds a sync key.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoKey))]
    public partial bool HasKey { get; set; }

    /// <summary>Whether this vault holds no sync key, which is the ordinary state of a second vault.</summary>
    public bool HasNoKey => !HasKey;

    /// <summary>This vault's key fingerprint, grouped for reading aloud; empty when it has no key.</summary>
    [ObservableProperty]
    public partial string Fingerprint { get; set; } = string.Empty;

    /// <summary>Where the offer this vault writes goes. Pre-filled beside the vault; the user may change it.</summary>
    [ObservableProperty]
    public partial string OfferFilePath { get; set; } = string.Empty;

    /// <summary>Whether an offer is being written.</summary>
    [ObservableProperty]
    public partial bool IsCreatingOffer { get; set; }

    /// <summary>The one-time code, grouped; empty until one is made, and never stored anywhere else.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCode))]
    public partial string Code { get; set; } = string.Empty;

    /// <summary>Whether there is a code on screen.</summary>
    public bool HasCode => Code.Length > 0;

    /// <summary>When the offer stops being accepted, in the user's language and time zone; empty when there is none.</summary>
    [ObservableProperty]
    public partial string OfferExpiresText { get; set; } = string.Empty;

    /// <summary>Why an offer was not made, in the user's language; empty when one was.</summary>
    [ObservableProperty]
    public partial string OfferMessage { get; set; } = string.Empty;

    /// <summary>The offer file this vault is being paired from.</summary>
    [ObservableProperty]
    public partial string AcceptOfferFilePath { get; set; } = string.Empty;

    /// <summary>The code the user is typing. Any spacing, case or hyphens.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPressAccept))]
    public partial string TypedCode { get; set; } = string.Empty;

    /// <summary>Whether a pairing is being attempted.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPressAccept))]
    public partial bool IsAccepting { get; set; }

    /// <summary>
    /// Whether the pair button is enabled: a code of the right shape, and nothing in flight. The validator and the
    /// offer itself check everything again; this is only what the button looks like.
    /// </summary>
    public bool CanPressAccept => !IsAccepting && SyncPairingCode.IsWellFormed(TypedCode);

    /// <summary>Why a pairing was refused, in the user's language; empty when none was.</summary>
    [ObservableProperty]
    public partial string AcceptMessage { get; set; } = string.Empty;

    /// <summary>What to show after a pairing succeeded: the fingerprint to compare. Empty until one does.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPaired))]
    public partial string PairedMessage { get; set; } = string.Empty;

    /// <summary>Whether a pairing succeeded on this page.</summary>
    public bool HasPaired => PairedMessage.Length > 0;

    /// <inheritdoc />
    public InitializationFailurePolicy InitializationFailurePolicy => new(_logger, "sync-pairing-load", LoadFailed, _error.Set);

    /// <inheritdoc />
    public Task InitializeAsync(CancellationToken cancellationToken) => LoadAsync();

    /// <summary>Reads this vault's pairing state again.</summary>
    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        _error.Clear();
        try
        {
            _status = await _mediator.Send(new GetSyncPairingStatusQuery());
            HasKey = _status.HasKey;
            Fingerprint = _status.Fingerprint;

            // Only pre-filled: a path the user has edited is the user's, and a reload must not take it back.
            if (OfferFilePath.Length == 0)
            {
                OfferFilePath = _status.SuggestedOfferPath;
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Writes an offer beside this vault and shows the code that opens it.</summary>
    [RelayCommand]
    private async Task CreateOfferAsync()
    {
        if (IsCreatingOffer)
        {
            return;
        }

        IsCreatingOffer = true;
        _offerError.Clear();
        try
        {
            var issued = await _mediator.Send(new CreateSyncPairingOfferCommand(OfferFilePath, SyncPairingLimits.DefaultLifetime));
            Code = issued.Code;
            Fingerprint = issued.Fingerprint;
            HasKey = true;
            _offerExpiresAtUtc = issued.ExpiresAtUtc;
            OfferFilePath = issued.OfferFilePath;
            WordExpiry();
        }
        catch (Exception ex)
        {
            // A failed offer leaves no code on screen: a stale code beside a failure reads as success.
            Code = string.Empty;
            _offerExpiresAtUtc = null;
            OfferExpiresText = string.Empty;
            _offerError.Set(UserFacingError.Report(_logger, ex, "sync-pairing-offer", OfferFailed));
        }
        finally
        {
            IsCreatingOffer = false;
        }
    }

    /// <summary>Opens the other device's offer with the code typed here, and keeps the key.</summary>
    [RelayCommand]
    private async Task AcceptOfferAsync()
    {
        if (IsAccepting || !SyncPairingCode.IsWellFormed(TypedCode))
        {
            return;
        }

        IsAccepting = true;
        _acceptError.Clear();
        try
        {
            var completed = await _mediator.Send(new AcceptSyncPairingOfferCommand(AcceptOfferFilePath, TypedCode));
            _pairedFingerprint = completed.Fingerprint;

            // The typed code is done with the moment it has been used, and leaving it on screen leaves a live
            // credential on a screen.
            TypedCode = string.Empty;
            WordPaired();
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _acceptError.Set(UserFacingError.Report(_logger, ex, "sync-pairing-accept", AcceptFailed));
        }
        finally
        {
            IsAccepting = false;
        }
    }

    private void WordExpiry() =>
        OfferExpiresText = _offerExpiresAtUtc is { } expires
            ? _localizer.Format("SyncPairing.Offer.Expires", expires.ToLocalTime().ToString("g", _localizer.CurrentCulture))
            : string.Empty;

    private void WordPaired() =>
        PairedMessage = _pairedFingerprint is { } fingerprint
            ? _localizer.Format("SyncPairing.Accept.Paired", fingerprint)
            : string.Empty;

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        WordExpiry();
        WordPaired();
    }

    /// <summary>Stops listening to the process-lifetime localizer.</summary>
    public void Dispose()
    {
        _localizer.LanguageChanged -= OnLanguageChanged;
        _error.Dispose();
        _offerError.Dispose();
        _acceptError.Dispose();
        GC.SuppressFinalize(this);
    }
}
