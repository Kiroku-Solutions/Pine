using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MediatR;
using Microsoft.Extensions.Logging;
using Pine.Application.Sync.Commands;
using Pine.Core.Common;
using Pine.Core.Sync;
using Pine.Ui.Avalonia.Diagnostics;
using Pine.Ui.Avalonia.Localization;
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
public partial class SyncRunViewModel : ObservableObject, IDisposable
{
    private static readonly UserFacingMessage RunFailed = new("SyncRun.Failure.Run");

    private readonly IMediator _mediator;
    private readonly ILocalizer _localizer;
    private readonly ILogger<SyncRunViewModel>? _logger;
    private readonly LocalizedMessageSlot _error;

    /// <summary>The last report, so a language change can word it again.</summary>
    private SyncRunReport? _report;

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

    /// <summary>The shared folder, as the user typed it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRun))]
    public partial string FolderPath { get; set; } = string.Empty;

    /// <summary>Whether a run is in progress.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRun))]
    public partial bool IsRunning { get; set; }

    /// <summary>Whether the button is enabled: a folder typed, and nothing in flight. The validator checks again.</summary>
    public bool CanRun => !IsRunning && !string.IsNullOrWhiteSpace(FolderPath);

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

    /// <summary>Runs one exchange through the folder.</summary>
    [RelayCommand]
    private async Task RunAsync()
    {
        if (!CanRun)
        {
            return;
        }

        IsRunning = true;
        _error.Clear();
        try
        {
            _report = await _mediator.Send(new RunSyncCommand(FolderPath.Trim()));
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
