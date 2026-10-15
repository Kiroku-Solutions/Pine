using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MediatR;
using Microsoft.Extensions.Logging;
using Pine.Application.Settings.Commands;
using Pine.Application.Settings.Queries;
using Pine.Application.Sync.Commands;
using Pine.Core.Common;
using Pine.Core.Sync;
using Pine.Ui.Avalonia.Diagnostics;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.Services;
using Pine.Ui.Avalonia.ViewModels.Messages;

namespace Pine.Ui.Avalonia.ViewModels;

/// <summary>
/// The "Sync now" page: one exchange with every paired copy through a folder they share (post-audit plan item 3.1f;
/// `14-Synchronization` §8, §9, §11).
/// </summary>
/// <remarks>
/// <para>
/// THE FIRST SCREEN THAT RUNS A MERGE. Until now the Sync conflicts page could only ever be empty on a real vault;
/// what a run here records is what that page lists.
/// </para>
/// <para>
/// THE FOLDER IS ASKED FOR EACH TIME, AND NOT REMEMBERED. Where a shared folder is differs per machine (a drive
/// letter, a mount point), so it cannot be a setting in the vault — settings travel with a sync — and no
/// per-installation store for it exists yet. Said here rather than covered over.
/// </para>
/// <para>
/// WHAT THE PAGE SAYS is counts and, per copy that did not go through, a reason a person can act on. A copy is named
/// by the first eight characters of its vault id: the only name a vault has.
/// </para>
/// </remarks>
public partial class SyncRunViewModel : ObservableObject, IAsyncInitializable, IDisposable
{
    private static readonly UserFacingMessage RunFailed = new("SyncRun.Failure.Run");

    private readonly IMediator _mediator;
    private readonly ILocalizer _localizer;
    private readonly ILogger<SyncRunViewModel>? _logger;
    private readonly LocalizedMessageSlot _error;

    /// <summary>The last report, so a language change can word it again.</summary>
    private SyncRunReport? _report;

    /// <summary>Set while loading the enabled setting, so the loader itself does not persist a write (TD-35's pattern).</summary>
    private bool _isInitializing = true;

    /// <param name="mediator">The request pipeline.</param>
    /// <param name="localizer">The string table; defaults to the process's, as every page's does.</param>
    /// <param name="logger">Diagnostics.</param>
    public SyncRunViewModel(IMediator mediator, ILocalizer? localizer = null, ILogger<SyncRunViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(mediator);
        _mediator = mediator;
        _localizer = localizer ?? Localizer.Instance;
        _logger = logger;
        _error = new LocalizedMessageSlot(_localizer, text => ErrorMessage = text);
        _localizer.LanguageChanged += OnLanguageChanged;
    }

    /// <inheritdoc />
    public Task InitializeAsync(CancellationToken cancellationToken) => LoadEnabledSettingAsync();

    /// <inheritdoc />
    public InitializationFailurePolicy InitializationFailurePolicy => new(_logger, "sync-run-load", RunFailed, null);

    /// <summary>The shared folder, as the user typed it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRun))]
    public partial string FolderPath { get; set; } = string.Empty;

    /// <summary>Whether a run is in progress.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRun))]
    public partial bool IsRunning { get; set; }

    /// <summary>
    /// §11's "enable or disable sync" — the one control this vault offers, since there is only ever one provider
    /// to turn on or off (ADR-0021; post-audit plan item 3.1h). §11 also asks for "pause and resume", separately
    /// from this; see the remarks on <see cref="IsSyncEnabled"/>'s persistence for why that is not a second control.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRun))]
    public partial bool IsSyncEnabled { get; set; } = true;

    /// <summary>Whether the button is enabled: sync turned on, a folder typed, and nothing in flight. The validator and the command both check again.</summary>
    public bool CanRun => IsSyncEnabled && !IsRunning && !string.IsNullOrWhiteSpace(FolderPath);

    /// <summary>Why the run did not happen, in the user's language; null when it did.</summary>
    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    /// <summary>Whether there is a report on screen.</summary>
    [ObservableProperty]
    public partial bool HasReport { get; set; }

    /// <summary>Whether the last run found no other copy in the folder.</summary>
    [ObservableProperty]
    public partial bool FoundNoPeers { get; set; }

    /// <summary>The report's lines, in the user's language.</summary>
    public ObservableCollection<string> Summary { get; } = [];

    /// <summary>One line per envelope that did not go through, in the user's language.</summary>
    public ObservableCollection<string> Problems { get; } = [];

    /// <summary>Whether anything did not go through.</summary>
    [ObservableProperty]
    public partial bool HasProblems { get; set; }

    /// <summary>
    /// Reads §11's enable/disable setting. Enabled is the default: a vault that has never touched the control has
    /// never been told sync is off (mirrors <see cref="RunSyncCommandHandler"/>'s own default, independently, since
    /// a UI-only guard is not the one that matters — see that handler's remarks).
    /// </summary>
    private async Task LoadEnabledSettingAsync()
    {
        try
        {
            var setting = await _mediator.Send(new GetSettingQuery(SyncSettingKeys.Enabled)).ConfigureAwait(false);
            IsSyncEnabled = !string.Equals(setting, bool.FalseString, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            // Cleared in a finally, as SettingsViewModel's own load does (TD-35): a failed read must not leave
            // every later toggle silently un-persisted for the rest of the session.
            _isInitializing = false;
        }
    }

    /// <summary>
    /// Persists the toggle through the same transactional setting command every other appearance control uses
    /// (TD-01: never <c>ISettingRepository</c> directly from a view model).
    /// </summary>
    /// <remarks>
    /// WHY THIS IS THE ONLY CONTROL, AND THERE IS NO SEPARATE "PAUSE". §11 asks for both "enable or disable" and
    /// "pause and resume" as two controls. With one provider and no background sync — nothing here runs on a
    /// schedule; a run only ever happens because a person pressed "Sync now" — a "pause" would do exactly what
    /// this toggle already does: refuse the next run until turned back on. Two controls that behave identically
    /// are one control and a second label, so this vault offers one durable setting rather than inventing a
    /// shorter-lived twin with no distinct behaviour to justify it.
    /// </remarks>
    partial void OnIsSyncEnabledChanged(bool value)
    {
        if (_isInitializing)
        {
            return;
        }

        ObservedTask.Run(
            () => _mediator.Send(new SaveSettingCommand(SyncSettingKeys.Enabled, value ? bool.TrueString : bool.FalseString)),
            _logger,
            "sync-enabled-save",
            new UserFacingMessage("SyncRun.Enabled.SaveFailed"),
            _error.Set);
    }

    /// <summary>Runs one ordinary exchange through the folder.</summary>
    [RelayCommand]
    private Task RunAsync() => RunCoreAsync(forceFullResync: false);

    // ----------------------------------------------------------------------------------------
    // §11's "force a full re-sync": destructive only in the sense of bandwidth and one-sided
    // redundancy, never of data loss (`ISyncRunner`'s remarks explain why) — but it is still a
    // deliberate, unusual action, so it gets the same scrim-and-panel confirmation every other
    // destructive control in this app uses, naming what will actually happen rather than asking
    // a generic "are you sure?" (post-audit plan item 3.1h).
    // ----------------------------------------------------------------------------------------

    /// <summary>Whether the force-resync confirmation panel is up.</summary>
    [ObservableProperty]
    public partial bool IsForceResyncDialogVisible { get; set; }

    /// <summary>Puts the confirmation panel up. Does nothing while a run is already in flight.</summary>
    [RelayCommand]
    private void BeginForceResync()
    {
        if (!CanRun)
        {
            return;
        }

        IsForceResyncDialogVisible = true;
    }

    /// <summary>Closes the confirmation without sending anything.</summary>
    [RelayCommand]
    private void CancelForceResync() => IsForceResyncDialogVisible = false;

    /// <summary>Runs the forced exchange.</summary>
    [RelayCommand]
    private Task ConfirmForceResync()
    {
        IsForceResyncDialogVisible = false;
        return RunCoreAsync(forceFullResync: true);
    }

    private async Task RunCoreAsync(bool forceFullResync)
    {
        if (!CanRun)
        {
            return;
        }

        IsRunning = true;
        _error.Clear();
        try
        {
            _report = await _mediator.Send(new RunSyncCommand(FolderPath.Trim(), forceFullResync));
            Word();

            // Rows written and conflicts recorded are things the other pages show.
            if (_report.RowsChanged > 0 || _report.ConflictsRecorded > 0)
            {
                WeakReferenceMessenger.Default.Send(new DataChangedMessage());
            }
        }
        catch (Exception ex)
        {
            // A refused or failed run leaves no report on screen: an old report beside a failure reads as this run's.
            _report = null;
            Word();
            _error.Set(UserFacingError.Report(_logger, ex, "sync-run", RunFailed));
        }
        finally
        {
            IsRunning = false;
        }
    }

    /// <summary>The key of the sentence that says what to do about <paramref name="kind"/>.</summary>
    public static string ProblemKey(SyncRunProblemKind kind) => kind switch
    {
        SyncRunProblemKind.Unreadable => "SyncRun.Problem.Unreadable",
        SyncRunProblemKind.WrongKey => "SyncRun.Problem.WrongKey",
        SyncRunProblemKind.NotAuthentic => "SyncRun.Problem.NotAuthentic",
        SyncRunProblemKind.Gap => "SyncRun.Problem.Gap",
        SyncRunProblemKind.MergeRefused => "SyncRun.Problem.MergeRefused",
        SyncRunProblemKind.TooLarge => "SyncRun.Problem.TooLarge",
        SyncRunProblemKind.TransferFailed => "SyncRun.Problem.TransferFailed",
        _ => "SyncRun.Problem.Unreadable",
    };

    /// <summary>How a copy is named on screen: the first eight characters of its vault id.</summary>
    public static string CopyName(Guid vaultId) =>
        vaultId.ToString("N", CultureInfo.InvariantCulture)[..8].ToUpperInvariant();

    private void Word()
    {
        Summary.Clear();
        Problems.Clear();

        if (_report is not { } report)
        {
            HasReport = false;
            FoundNoPeers = false;
            HasProblems = false;
            return;
        }

        HasReport = true;
        FoundNoPeers = report.PeersFound == 0;

        foreach (var line in Lines(report))
        {
            Summary.Add(line);
        }

        foreach (var problem in report.Problems)
        {
            Problems.Add(_localizer.Format("SyncRun.Problem.Line", CopyName(problem.PeerVaultId), _localizer[ProblemKey(problem.Kind)]));
        }

        HasProblems = Problems.Count > 0;
    }

    private IEnumerable<string> Lines(SyncRunReport report)
    {
        yield return _localizer.Format("SyncRun.Result.Peers", report.PeersFound.ToString(_localizer.CurrentCulture));
        yield return _localizer.Format("SyncRun.Result.Applied", report.EnvelopesApplied.ToString(_localizer.CurrentCulture));
        yield return _localizer.Format("SyncRun.Result.Rows", report.RowsChanged.ToString(_localizer.CurrentCulture));
        yield return _localizer.Format("SyncRun.Result.Left", report.EnvelopesLeft.ToString(_localizer.CurrentCulture));
        if (report.ConflictsRecorded > 0)
        {
            yield return _localizer.Format("SyncRun.Result.Conflicts", report.ConflictsRecorded.ToString(_localizer.CurrentCulture));
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => Word();

    /// <summary>Stops listening to the process-lifetime localizer.</summary>
    public void Dispose()
    {
        _localizer.LanguageChanged -= OnLanguageChanged;
        _error.Dispose();
        GC.SuppressFinalize(this);
    }
}
