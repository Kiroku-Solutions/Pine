using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MediatR;
using Microsoft.Extensions.Logging;
using Pine.Application.Sync.Commands;
using Pine.Application.Sync.Queries;
using Pine.Core.Common;
using Pine.Core.Sync;
using Pine.Ui.Avalonia.Diagnostics;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.Services;
using Pine.Ui.Avalonia.ViewModels.Messages;

namespace Pine.Ui.Avalonia.ViewModels;

/// <summary>One field of a conflicting row, worded for the screen.</summary>
/// <param name="Name">The field's name in the user's language, or the stored column's when it has none.</param>
/// <param name="LocalText">This device's value.</param>
/// <param name="RemoteText">The other device's value.</param>
public sealed record SyncConflictFieldRow(string Name, string LocalText, string RemoteText);

/// <summary>One recorded sync conflict on the conflicts page (post-audit plan item 3.1c).</summary>
public partial class SyncConflictRowViewModel : ObservableObject
{
    /// <summary>The conflict.</summary>
    public Guid Id { get; private init; }

    /// <summary>Why the merge left it for a person.</summary>
    public SyncConflictKind Kind { get; private init; }

    /// <summary>What happened, in a few words.</summary>
    public string Title { get; private init; } = string.Empty;

    /// <summary>What happened, and what each choice does, in a sentence or two.</summary>
    public string Explanation { get; private init; } = string.Empty;

    /// <summary>What the item is: "Transaction · Grocer".</summary>
    public string EntityText { get; private init; } = string.Empty;

    /// <summary>Which device, and when the merge recorded it.</summary>
    public string MetaText { get; private init; } = string.Empty;

    /// <summary>The fields that differ, or every shown field when one side has no row.</summary>
    public IReadOnlyList<SyncConflictFieldRow> Fields { get; private init; } = [];

    /// <summary>Whether there are fields to show.</summary>
    public bool HasFields => Fields.Count > 0;

    /// <summary>The button that keeps this device's side.</summary>
    public string KeepLocalLabel { get; private init; } = string.Empty;

    /// <summary>The button that takes the other device's side.</summary>
    public string TakeRemoteLabel { get; private init; } = string.Empty;

    /// <summary>Whether taking the other device's side is offered.</summary>
    public bool CanTakeRemote { get; private init; }

    /// <summary>Why it is not, in the user's language; empty when it is.</summary>
    public string TakeRemoteBlockedText { get; private init; } = string.Empty;

    /// <summary>Whether <see cref="TakeRemoteBlockedText"/> is shown.</summary>
    public bool IsTakeRemoteBlocked => !CanTakeRemote;

    /// <summary>
    /// Taking the other device's side grants a consent, so the page asks for <see cref="ConsentConfirmed"/> first
    /// (`14-Synchronization` §7: consent is never granted implicitly).
    /// </summary>
    public bool RequiresConsentConfirmation { get; private init; }

    /// <summary>The person ticked "I mean to grant this consent".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPressTakeRemote))]
    public partial bool ConsentConfirmed { get; set; }

    /// <summary>
    /// Whether the take-remote button is enabled: offered, and — where it grants a consent — confirmed. The command
    /// re-checks it, and so does the store; this is only what the button looks like.
    /// </summary>
    public bool CanPressTakeRemote => CanTakeRemote && (!RequiresConsentConfirmation || ConsentConfirmed);

    /// <summary>Builds the row from a conflict, worded in the localizer's language.</summary>
    /// <param name="conflict">The conflict.</param>
    /// <param name="localizer">The string table.</param>
    /// <returns>The row.</returns>
    public static SyncConflictRowViewModel From(SyncConflictSummary conflict, ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(conflict);
        ArgumentNullException.ThrowIfNull(localizer);

        var (title, explanation, keep, take) = KindKeys(conflict.Kind);
        var entity = localizer[EntityKey(conflict.EntityTable)];
        var culture = localizer.CurrentCulture;

        return new SyncConflictRowViewModel
        {
            Id = conflict.Id,
            Kind = conflict.Kind,
            Title = localizer[title],
            Explanation = localizer[explanation],
            EntityText = string.IsNullOrWhiteSpace(conflict.EntityLabel) ? entity : $"{entity} · {conflict.EntityLabel}",
            MetaText = localizer.Format(
                "SyncConflicts.Meta",
                ShortDevice(conflict.RemoteDeviceId),
                new DateTimeOffset(conflict.DetectedAtUtc, TimeSpan.Zero).ToLocalTime().ToString("g", culture)),
            Fields = conflict.Fields.Select(field => new SyncConflictFieldRow(
                    FieldName(field.Name, localizer),
                    ValueText(field.LocalValue, conflict.LocalExists, isDeletion: false, localizer),
                    ValueText(field.RemoteValue, sideExists: !conflict.RemoteIsDeletion, isDeletion: conflict.RemoteIsDeletion, localizer)))
                .ToList(),
            KeepLocalLabel = localizer[keep],
            TakeRemoteLabel = localizer[take],
            CanTakeRemote = conflict.CanTakeRemote,
            TakeRemoteBlockedText = conflict.CanTakeRemote
                ? string.Empty
                : Word(SyncConflictRefusedException.DescribeBlock(conflict.TakeRemoteBlockedBy), localizer),
            RequiresConsentConfirmation = conflict.TakeRemoteGrantsConsent,
        };

        static string ShortDevice(Guid device) =>
            device == Guid.Empty ? "—" : device.ToString("N", CultureInfo.InvariantCulture)[..8];

        string ValueText(object? value, bool sideExists, bool isDeletion, ILocalizer l) => value switch
        {
            null when isDeletion => l["SyncConflicts.Value.Deleted"],
            null when !sideExists => l["SyncConflicts.Value.NotHere"],
            null => l["SyncConflicts.Value.Empty"],
            string text when text.Length == 0 => l["SyncConflicts.Value.Empty"],
            decimal amount => amount.ToString("#,##0.00##", culture),
            DateTimeOffset instant => instant.ToString("g", culture),
            bool flag => l[flag ? "SyncConflicts.Value.Yes" : "SyncConflicts.Value.No"],
            IFormattable formattable => formattable.ToString(null, culture),
            _ => value.ToString() ?? string.Empty,
        };
    }

    /// <summary>The string-table keys for a kind: title, explanation, keep label, take label.</summary>
    /// <remarks>A switch, so every key is a literal the catalog test finds, and a new kind fails to compile here.</remarks>
    public static (string Title, string Explanation, string KeepLocal, string TakeRemote) KindKeys(SyncConflictKind kind) => kind switch
    {
        SyncConflictKind.ConcurrentEdit => (
            "SyncConflicts.Kind.ConcurrentEdit.Title", "SyncConflicts.Kind.ConcurrentEdit.Explanation",
            "SyncConflicts.Action.KeepMine", "SyncConflicts.Action.UseTheirs"),
        SyncConflictKind.RemoteDeleteOfLocalEdit => (
            "SyncConflicts.Kind.RemoteDeleteOfLocalEdit.Title", "SyncConflicts.Kind.RemoteDeleteOfLocalEdit.Explanation",
            "SyncConflicts.Action.KeepIt", "SyncConflicts.Action.DeleteHereToo"),
        SyncConflictKind.RemoteEditOfLocalDelete => (
            "SyncConflicts.Kind.RemoteEditOfLocalDelete.Title", "SyncConflicts.Kind.RemoteEditOfLocalDelete.Explanation",
            "SyncConflicts.Action.KeepDeleted", "SyncConflicts.Action.Restore"),
        SyncConflictKind.DeleteBlockedByDependents => (
            "SyncConflicts.Kind.DeleteBlockedByDependents.Title", "SyncConflicts.Kind.DeleteBlockedByDependents.Explanation",
            "SyncConflicts.Action.KeepIt", "SyncConflicts.Action.DeleteHereToo"),
        SyncConflictKind.MissingParent => (
            "SyncConflicts.Kind.MissingParent.Title", "SyncConflicts.Kind.MissingParent.Explanation",
            "SyncConflicts.Action.KeepThisDevice", "SyncConflicts.Action.UseTheirs"),
        SyncConflictKind.AmbiguousNaturalKey => (
            "SyncConflicts.Kind.AmbiguousNaturalKey.Title", "SyncConflicts.Kind.AmbiguousNaturalKey.Explanation",
            "SyncConflicts.Action.KeepThisDevice", "SyncConflicts.Action.UseTheirs"),
        SyncConflictKind.ConsentNotImplicitlyGranted => (
            "SyncConflicts.Kind.ConsentNotImplicitlyGranted.Title", "SyncConflicts.Kind.ConsentNotImplicitlyGranted.Explanation",
            "SyncConflicts.Action.KeepMySetting", "SyncConflicts.Action.AcceptTheirSetting"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "A conflict kind with no words on screen."),
    };

    /// <summary>The key naming a table's rows on screen.</summary>
    public static string EntityKey(string table) => table switch
    {
        "Accounts" => "SyncConflicts.Entity.Account",
        "Transactions" => "SyncConflicts.Entity.Transaction",
        "Budgets" => "SyncConflicts.Entity.Budget",
        "Settings" => "SyncConflicts.Entity.Setting",
        "Rules" => "SyncConflicts.Entity.Rule",
        "Attachments" => "SyncConflicts.Entity.Attachment",
        "ImportProfiles" => "SyncConflicts.Entity.ImportProfile",
        _ => "SyncConflicts.Entity.Other",
    };

    /// <summary>A stored column's name on screen; a column nobody named yet shows as itself rather than as nothing.</summary>
    public static string FieldName(string column, ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        var key = column switch
        {
            "Name" => "SyncConflicts.Field.Name",
            "Payee" => "SyncConflicts.Field.Payee",
            "Notes" => "SyncConflicts.Field.Notes",
            "Date" => "SyncConflicts.Field.Date",
            "TotalAmount_Amount" or "Amount_Amount" => "SyncConflicts.Field.Amount",
            "TotalAmount_Currency" or "Balance_Currency" or "LimitCurrency" => "SyncConflicts.Field.Currency",
            "Type" => "SyncConflicts.Field.Type",
            "VoidedAt" => "SyncConflicts.Field.VoidedAt",
            "VoidReason" => "SyncConflicts.Field.VoidReason",
            "Category" => "SyncConflicts.Field.Category",
            "LimitAmount" => "SyncConflicts.Field.Limit",
            "Period" => "SyncConflicts.Field.Period",
            "StartDate" => "SyncConflicts.Field.StartDate",
            "EndDate" => "SyncConflicts.Field.EndDate",
            "Key" => "SyncConflicts.Field.Key",
            "Value" => "SyncConflicts.Field.Value",
            "Title" => "SyncConflicts.Field.Title",
            "IsEnabled" => "SyncConflicts.Field.IsEnabled",
            "Priority" => "SyncConflicts.Field.Priority",
            "FileName" => "SyncConflicts.Field.FileName",
            "OpeningBalanceAmount" => "SyncConflicts.Field.OpeningBalance",
            "AccountId" => "SyncConflicts.Field.Account",
            "TransactionId" => "SyncConflicts.Field.Transaction",
            "ImportKey" => "SyncConflicts.Field.ImportKey",
            "TransactionSplits" => "SyncConflicts.Field.Splits",
            "TransactionLineItems" => "SyncConflicts.Field.LineItems",
            "RuleConditions" => "SyncConflicts.Field.Conditions",
            _ => null,
        };

        return key is null ? column : localizer[key];
    }

    private static string Word(UserFacingMessage message, ILocalizer localizer) =>
        localizer.Format(message.Key, [.. message.Arguments]);
}

/// <summary>
/// The sync conflicts page: every change a merge would not apply on its own, and a decision for each
/// (post-audit plan item 3.1c; `14-Synchronization` §4, "a person decides").
/// </summary>
/// <remarks>
/// <para>
/// THE PAGE OFFERS ONLY WHAT THE STORE WILL DO. A choice that is not safe — taking a row whose parent is not here,
/// deleting a row others still point at, guessing between two rows — is not a button; the row says why in a sentence.
/// The command's validator and the store check the same things again, so a stale page cannot do more than a fresh one.
/// </para>
/// <para>
/// A CONSENT IS NEVER ONE CLICK (§7). Where taking the other device's setting would grant a consent, its button stays
/// disabled until the person ticks a box that says so, and the command carries that tick to the store, which refuses
/// without it.
/// </para>
/// <para>
/// ON A REAL VAULT THIS PAGE IS EMPTY. Nothing in the application runs a merge yet; the empty sentence says what the
/// page is for rather than pretending there is nothing to sync.
/// </para>
/// </remarks>
public partial class SyncConflictsViewModel : ObservableObject, IAsyncInitializable, IDisposable
{
    private static readonly UserFacingMessage LoadFailed = new("SyncConflicts.Failure.Load");
    private static readonly UserFacingMessage ResolveFailed = new("SyncConflicts.Failure.Resolve");

    private readonly IMediator _mediator;
    private readonly ILocalizer _localizer;
    private readonly ILogger<SyncConflictsViewModel>? _logger;
    private readonly LocalizedMessageSlot _error;
    private readonly LocalizedMessageSlot _resolveMessage;

    /// <summary>What the rows were built from, so a language change can word them again.</summary>
    private IReadOnlyList<SyncConflictSummary> _last = [];

    /// <param name="mediator">The request pipeline.</param>
    /// <param name="localizer">The string table; defaults to the process's, as every page's does.</param>
    /// <param name="logger">Diagnostics.</param>
    public SyncConflictsViewModel(IMediator mediator, ILocalizer? localizer = null, ILogger<SyncConflictsViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(mediator);
        _mediator = mediator;
        _localizer = localizer ?? Localizer.Instance;
        _logger = logger;
        _error = new LocalizedMessageSlot(_localizer, text => ErrorMessage = text);
        _resolveMessage = new LocalizedMessageSlot(_localizer, text => ResolveMessage = text ?? string.Empty);
        Conflicts.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
        _localizer.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>The open conflicts, oldest first.</summary>
    public ObservableCollection<SyncConflictRowViewModel> Conflicts { get; } = new();

    /// <summary>Whether the list is being read.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool IsLoading { get; set; }

    /// <summary>Whether a decision is being applied, which disables every button.</summary>
    [ObservableProperty]
    public partial bool IsResolving { get; set; }

    /// <summary>Why the list could not be read, in the user's language; null when it could.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial string? ErrorMessage { get; set; }

    /// <summary>Why a decision was not applied, in the user's language; empty when it was.</summary>
    [ObservableProperty]
    public partial string ResolveMessage { get; set; } = string.Empty;

    /// <summary>The list loaded and holds nothing: the page says what it is for.</summary>
    public bool IsEmpty => !IsLoading && Conflicts.Count == 0 && string.IsNullOrEmpty(ErrorMessage);

    /// <inheritdoc />
    public InitializationFailurePolicy InitializationFailurePolicy => new(_logger, "sync-conflicts-load", LoadFailed, _error.Set);

    /// <inheritdoc />
    public Task InitializeAsync(CancellationToken cancellationToken) => LoadAsync();

    /// <summary>Reads the open conflicts again.</summary>
    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        _error.Clear();
        Conflicts.Clear();
        try
        {
            _last = await _mediator.Send(new GetSyncConflictsQuery());
            Rebuild();
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Keeps this device's side of <paramref name="row"/>.</summary>
    [RelayCommand]
    private Task KeepLocalAsync(SyncConflictRowViewModel? row) => ResolveAsync(row, SyncConflictResolution.KeepLocal);

    /// <summary>Takes the other device's side of <paramref name="row"/>, where that is offered and, for a consent, confirmed.</summary>
    [RelayCommand]
    private Task TakeRemoteAsync(SyncConflictRowViewModel? row) =>
        row is { CanPressTakeRemote: true } ? ResolveAsync(row, SyncConflictResolution.TakeRemote) : Task.CompletedTask;

    private async Task ResolveAsync(SyncConflictRowViewModel? row, SyncConflictResolution resolution)
    {
        if (row is null || IsResolving)
        {
            return;
        }

        IsResolving = true;
        _resolveMessage.Clear();
        try
        {
            await _mediator.Send(new ResolveSyncConflictCommand(
                row.Id,
                resolution,
                ConfirmConsentGrant: resolution == SyncConflictResolution.TakeRemote && row.ConsentConfirmed));
        }
        catch (Exception ex)
        {
            _resolveMessage.Set(UserFacingError.Report(_logger, ex, "sync-conflict-resolve", ResolveFailed));
            return;
        }
        finally
        {
            IsResolving = false;
        }

        // Applied from here on. Other pages show the rows a resolution may have written or deleted, and balances it
        // re-derived; a failed reload is a load failure, reported as one, never as "not applied".
        WeakReferenceMessenger.Default.Send(new DataChangedMessage());
        try
        {
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _error.Set(UserFacingError.Report(_logger, ex, "sync-conflicts-reload", LoadFailed));
        }
    }

    private void Rebuild()
    {
        Conflicts.Clear();
        foreach (var conflict in _last)
        {
            Conflicts.Add(SyncConflictRowViewModel.From(conflict, _localizer));
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => Rebuild();

    /// <summary>Stops listening to the process-lifetime localizer.</summary>
    public void Dispose()
    {
        _localizer.LanguageChanged -= OnLanguageChanged;
        _error.Dispose();
        _resolveMessage.Dispose();
        GC.SuppressFinalize(this);
    }
}
