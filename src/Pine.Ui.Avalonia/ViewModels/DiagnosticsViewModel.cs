using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pine.Core.Common;
using Pine.Core.Diagnostics;
using Microsoft.Extensions.Options;
using Pine.Core.Configuration;
using System.Threading;
using Microsoft.Extensions.Logging;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.Services;
using System.Collections.Generic;

namespace Pine.Ui.Avalonia.ViewModels;

/// <summary>
/// One log record as the diagnostics terminal renders it.
/// </summary>
/// <remarks>
/// A PROJECTION, NOT A FORMATTED STRING, because the two things a test needs to check are exactly
/// the two things a formatted string destroys: that the timestamp, level and message on screen are
/// the ones <see cref="ILogTailReader"/> handed over, and that nothing else was inserted alongside
/// them. The old panel held <c>ObservableCollection&lt;string&gt;</c>, which made a fabricated line
/// and a real line indistinguishable to every caller -- including the seven fabrications the
/// constructor added (TD-15).
/// <para>
/// <see cref="Message"/> is already redacted; see <see cref="LogTailEntry"/>'s remarks for why
/// there is deliberately no second redaction pass on this side.
/// </para>
/// </remarks>
public sealed class DiagnosticsLogEntryViewModel
{
    /// <summary>Projects one tail record into its display form.</summary>
    /// <param name="entry">The record read back from the log. Never <see langword="null"/>.</param>
    public DiagnosticsLogEntryViewModel(LogTailEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        Timestamp = entry.Timestamp;
        Level = entry.Level;
        Category = entry.Category;
        Message = entry.Message;
    }

    /// <summary>When the record was produced, exactly as the reader reported it (UTC).</summary>
    public DateTimeOffset Timestamp { get; }

    /// <summary>The level in <c>30-Observability</c> §4.2's vocabulary, verbatim from the file.</summary>
    public string Level { get; }

    /// <summary>The logger category the record came from, verbatim from the file.</summary>
    public string Category { get; }

    /// <summary>The already-redacted message, verbatim from the file.</summary>
    public string Message { get; }

    /// <summary>
    /// The timestamp rendered in the reader's local time, to the second.
    /// </summary>
    /// <remarks>
    /// LOCAL, because the only question this column answers is "was this before or after the thing
    /// I just did", and the user's own clock is the one they compare against. The UTC instant is
    /// still on <see cref="Timestamp"/> for anyone correlating with the file itself, which keeps
    /// UTC per <c>30-Observability</c> §4.1.
    /// </remarks>
    public string TimestampText =>
        Timestamp.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture);

    /// <summary>The level in upper case, for the fixed-width column in the terminal card.</summary>
    public string LevelText => Level.ToUpperInvariant();

    /// <summary>
    /// The whole record as one line, used by the terminal's Copy button.
    /// </summary>
    /// <remarks>
    /// Copy deliberately carries the <see cref="Category"/> that the terminal column does not show:
    /// text pasted into an issue is read away from the screen that produced it, where the category
    /// is the only clue to which subsystem spoke.
    /// </remarks>
    public string Text =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{Timestamp.UtcDateTime:yyyy-MM-dd HH:mm:ss}Z [{LevelText}] {Category}: {Message}");
}

/// <summary>
/// The diagnostics page: what this build is, what it is running on, whether the vault database
/// answers, and the tail of the log Pine actually wrote.
/// </summary>
/// <remarks>
/// <para>
/// HOLDS NO REPOSITORY (TD-01). It used to inject the Scoped <c>ICategoryRepository</c> and call
/// <c>SaveAsync</c> directly; once repositories stopped flushing, that write was staged on a
/// root-scoped <c>PineDbContext</c> and never committed, so the <c>CreateBudgetCommand</c> sent
/// straight afterwards pointed at a category that did not exist. That history is why the class
/// carried an <c>IMediator</c> at all -- and with the write path below now gone, so is the mediator.
/// </para>
/// <para>
/// EVERY VALUE ON THIS SCREEN IS NOW MEASURED (TD-15). What was here before was not a placeholder
/// waiting on a backend; it was a working screen whose content was invented:
/// </para>
/// <list type="bullet">
/// <item><description>
/// Seven log lines added in the CONSTRUCTOR, timestamped <c>DateTime.Now</c> so they looked freshly
/// emitted. Two of them asserted facts no code had checked -- <c>"Migrations are up to date."</c>
/// and <c>"No active internet connection detected."</c> -- and they were most confident precisely
/// when the database was missing. The tail now comes from <see cref="ILogTailReader"/>, and the
/// constructor performs no I/O at all, which is what makes "the panel cannot invent a record"
/// something a test can assert rather than something a reviewer has to re-read the file for.
/// </description></item>
/// <item><description>
/// <c>DatabaseStatus = "Connected (Healthy)"</c> as a literal, replaced by a mapping over
/// <see cref="IDatabaseHealthProbe"/>'s closed status set.
/// </description></item>
/// <item><description>
/// <c>AppVersion = "Pine Vault v1.0.0-beta.1"</c> as a literal, frozen at whatever the build had
/// been when someone typed it, replaced by <see cref="IRuntimeInfoProvider.AppVersion"/> -- the
/// same value the sink stamps into every record the terminal below is displaying
/// (<c>30-Observability</c> §4.1).
/// </description></item>
/// <item><description>
/// <c>ExportDib</c>, whose entire body was a log line and a <c>// To be implemented</c>. A button
/// that reports success and does nothing is worse than no button during an incident.
/// </description></item>
/// <item><description>
/// <c>TestFeatures</c>, which seeded fictional accounts, transactions, a transfer and a budget into
/// the user's REAL vault from a nav-reachable button. Same defect class as the seeded fictional
/// account removed under TD-47, and removed for the same reason: nothing in a personal-finance
/// vault should ever originate from the application.
/// </description></item>
/// </list>
/// <para>
/// NO EXCEPTION TEXT EVER REACHES THIS SCREEN (TD-23). The probe returns a status, never a message,
/// so <see cref="DatabaseStatus"/> can only ever be one of the fixed strings below; the reason a
/// probe failed goes to the log, where <c>LogRedactor</c> has already handled it. The obvious
/// alternative -- catching and showing <c>ex.Message</c> -- would trade a fabrication for a leak,
/// since SQLite and EF messages routinely carry the vault file path.
/// </para>
/// </remarks>
public partial class DiagnosticsViewModel : ObservableObject, IAsyncInitializable, IDisposable
{
    private readonly LocalizedMessageSlot _error;

    /// <summary>
    /// Renders <see cref="EmptyLogMessage"/> through the same mechanism <see cref="_error"/> uses
    /// for <see cref="ErrorMessage"/>, so the empty-log line also re-words itself on a language
    /// change instead of being frozen at whichever state was active when it was last assigned
    /// (TD-150).
    /// </summary>
    private readonly LocalizedMessageSlot _emptyLog;

    /// <summary>
    /// Renders <see cref="DatabaseStatus"/> through the same mechanism <see cref="_emptyLog"/> uses
    /// for <see cref="EmptyLogMessage"/>, so the database card's wording also re-words itself on a
    /// language change instead of freezing at whatever status string <see cref="DescribeHealth"/>
    /// returned the last time <see cref="RefreshAsync"/> ran (TD-150: "Connected"/"Vault locked"
    /// were literal English returned from that switch).
    /// </summary>
    private readonly LocalizedMessageSlot _databaseStatus;

    /// <summary>
    /// How many of the newest log records the terminal reads.
    /// </summary>
    /// <remarks>
    /// 200 IS A DELIBERATE CEILING, not a round number. It is far more than the handful of records
    /// a user scrolls back through after "it just did something odd", and small enough that the
    /// whole tail is a trivial allocation even at Debug level -- so the panel cannot become a way
    /// to pull a day's worth of records into memory, and cannot become slow enough that people stop
    /// opening it. Anyone who needs more than this is past what a screen can help with and wants
    /// the <c>.log.jsonl</c> files themselves.
    /// </remarks>
    public const int MaxLogEntries = 200;

    /// <summary>
    /// The configured ceiling, from <c>PineUiOptions.DiagnosticsLogEntryLimit</c> (TD-27), which
    /// defaults to <see cref="MaxLogEntries"/>.
    /// </summary>
    private readonly int _maxLogEntries;

    /// <summary>
    /// The key for what every measured field reads before <see cref="RefreshAsync"/> has run
    /// (TD-150: was the public constant <c>NotMeasuredText</c>, compared by reference like
    /// <c>NoRecordsText</c>/<c>ViewClearedText</c> were before this same row's earlier pass).
    /// </summary>
    /// <remarks>
    /// Not "Healthy", not a version number, not an empty string that would render as a blank card
    /// the user reads as "fine". The screen states plainly that it has not looked yet, which is the
    /// honest description of a view model whose constructor does no I/O.
    /// </remarks>
    private const string NotMeasuredKey = "Diagnostics.NotMeasured";

    /// <summary>The database card's key for a locked vault -- a normal state, not a failure (TD-150).</summary>
    private const string DatabaseLockedKey = "Diagnostics.Database.Locked";

    /// <summary>The database card's key for an open database at the current schema (TD-150).</summary>
    private const string DatabaseHealthyKey = "Diagnostics.Database.Healthy";

    /// <summary>The database card's key for an open vault whose database will not answer (TD-150).</summary>
    private const string DatabaseUnreachableKey = "Diagnostics.Database.Unreachable";

    /// <summary>The database card's key for the pending-migrations count, carrying one argument (TD-150).</summary>
    private const string DatabasePendingMigrationsKey = "Diagnostics.Database.PendingMigrations";

    /// <summary>
    /// The database card's key for a probe that failed, and for any status this build does not
    /// recognise (TD-150).
    /// </summary>
    /// <remarks>
    /// FIXED AND NEUTRAL ON PURPOSE (TD-23). It names the log rather than the fault, because the
    /// fault detail is the part that carries paths. A future enum member falling here reads as
    /// "unavailable" rather than crashing a diagnostics screen, which is the one screen that must
    /// survive things being broken.
    /// </remarks>
    private const string DatabaseUnavailableKey = "Diagnostics.Database.Unavailable";

    /// <summary>The empty-state line's key, shown when the log genuinely holds nothing (TD-150).</summary>
    /// <remarks>
    /// AN EMPTY LOG IS A REAL STATE and is rendered as one. Padding it with a synthesised
    /// "initialised" record is the exact sin TD-15 removed, so the panel says nothing is there and
    /// lets the user draw the correct conclusion. Moved from a public English constant to a key
    /// (TD-150): the old constant was compared against by reference, which pinned this panel's
    /// empty state to English and was the very reason the string could not move to the string
    /// tables earlier.
    /// </remarks>
    private const string NoRecordsKey = "Diagnostics.Log.Empty";

    /// <summary>The empty-state line's key, shown after the user clears the view (TD-150).</summary>
    private const string ViewClearedKey = "Diagnostics.Log.ViewCleared";

    private readonly ILogTailReader _logTailReader;
    private readonly ILogger<DiagnosticsViewModel>? _logger;
    private readonly IDatabaseHealthProbe _databaseHealthProbe;
    private readonly IRuntimeInfoProvider _runtimeInfoProvider;

    /// <summary>The application's informational version, as reported by the runtime info provider.</summary>
    [ObservableProperty]
    public partial string AppVersion { get; set; } = string.Empty;

    /// <summary>The operating system and architecture description, as reported by the provider.</summary>
    [ObservableProperty]
    public partial string OsDescription { get; set; } = string.Empty;

    /// <summary>
    /// The vault database's health, in this view model's own wording.
    /// </summary>
    /// <remarks>
    /// Starts empty, not pre-filled: <see cref="_databaseStatus"/> assigns the real value from the
    /// constructor, through the localizer, the same way <see cref="_emptyLog"/> assigns
    /// <see cref="EmptyLogMessage"/>. Derived by <see cref="DescribeHealthKey"/> from a status;
    /// never an exception message.
    /// </remarks>
    [ObservableProperty]
    public partial string DatabaseStatus { get; set; } = string.Empty;

    /// <summary>
    /// Bytes on the managed heap, formatted for display.
    /// </summary>
    /// <remarks>
    /// The MANAGED HEAP, and the card says so. The old label read "Engine Memory" over a
    /// <c>GC.GetTotalMemory</c> reading, which is not the process working set and not what a user
    /// comparing against Task Manager would expect; the number was real and the label was the lie.
    /// </remarks>
    [ObservableProperty]
    public partial string ManagedMemory { get; set; } = string.Empty;

    /// <summary>Whether a read is in flight, so the view can disable Refresh.</summary>
    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    /// <summary>
    /// Whether the terminal has nothing to show, so the view can render its empty state.
    /// </summary>
    /// <remarks>
    /// Starts <see langword="false"/>: before the first <see cref="RefreshAsync"/> the panel has
    /// not looked, and claiming "no records" would be as much an invention as claiming seven.
    /// </remarks>
    [ObservableProperty]
    public partial bool HasNoLogRecords { get; set; }

    /// <summary>Which empty state applies -- nothing in the log, or a view the user cleared.</summary>
    /// <remarks>
    /// Starts empty, not pre-filled: <see cref="_emptyLog"/> assigns the real value from the
    /// constructor, through the localizer, exactly as every other rendered value here is produced
    /// rather than literal-initialised.
    /// </remarks>
    [ObservableProperty]
    public partial string EmptyLogMessage { get; set; } = string.Empty;

    /// <summary>
    /// The tail of the Pine log, oldest first.
    /// </summary>
    /// <remarks>
    /// Every element here came out of <see cref="ILogTailReader"/>. Nothing in this class adds to
    /// this collection from any other source -- not a status line, not a "cleared by user" marker,
    /// not a constructor greeting -- which is what makes the terminal's content quotable in a bug
    /// report.
    /// </remarks>
    public ObservableCollection<DiagnosticsLogEntryViewModel> LogEntries { get; } = new();

    /// <summary>
    /// Captures the diagnostic seams. Performs no I/O and measures nothing.
    /// </summary>
    /// <remarks>
    /// THE CONSTRUCTOR DOING NOTHING IS THE FIX. The previous one read the clock, read the GC and
    /// wrote seven records into the view before the page had been shown, which meant the panel's
    /// content was decided at construction time and could never be wrong-but-refreshable -- it was
    /// simply wrong. Loading happens in <see cref="RefreshAsync"/>, invoked on navigation by
    /// <see cref="ShellViewModel"/> exactly as <c>BudgetsViewModel.InitializeAsync</c> is.
    /// </remarks>
    /// <param name="logTailReader">Reads back the records the log sink wrote.</param>
    /// <param name="databaseHealthProbe">Answers whether the vault database is there and migrated.</param>
    /// <param name="runtimeInfoProvider">Reports build, host and managed memory.</param>
    /// <param name="uiOptions">Carries the record ceiling. Optional, for tests without a container.</param>
    public DiagnosticsViewModel(
        ILogTailReader logTailReader,
        IDatabaseHealthProbe databaseHealthProbe,
        IRuntimeInfoProvider runtimeInfoProvider,
        IOptions<PineUiOptions>? uiOptions = null,
        ILogger<DiagnosticsViewModel>? logger = null,
        ILocalizer? localizer = null)
    {
        ArgumentNullException.ThrowIfNull(logTailReader);
        ArgumentNullException.ThrowIfNull(databaseHealthProbe);
        ArgumentNullException.ThrowIfNull(runtimeInfoProvider);

        _maxLogEntries = (uiOptions?.Value ?? new PineUiOptions()).DiagnosticsLogEntryLimit;

        _logTailReader = logTailReader;
        _logger = logger;
        _databaseHealthProbe = databaseHealthProbe;
        _runtimeInfoProvider = runtimeInfoProvider;

        var effectiveLocalizer = localizer ?? new Localizer();
        _error = new LocalizedMessageSlot(effectiveLocalizer, text => ErrorMessage = text);
        _emptyLog = new LocalizedMessageSlot(effectiveLocalizer, text => EmptyLogMessage = text ?? string.Empty);
        _emptyLog.Set(NoRecordsKey);
        _databaseStatus = new LocalizedMessageSlot(effectiveLocalizer, text => DatabaseStatus = text ?? string.Empty);
        _databaseStatus.Set(NotMeasuredKey);

        AppVersion = effectiveLocalizer[NotMeasuredKey];
        OsDescription = effectiveLocalizer[NotMeasuredKey];
        ManagedMemory = effectiveLocalizer[NotMeasuredKey];
    }

    /// <summary>
    /// The view model's logger, for the code-behind that reports a failure through it (TD-127). A view is built by the XAML loader with no container reach, so it borrows the logger of the view model it is the face of.
    /// </summary>
    internal Microsoft.Extensions.Logging.ILogger? Logger => _logger;

    /// <summary>
    /// Shows <paramref name="messages"/> as this page's error, in the active language, and keeps them
    /// in step with a language change. For the view's code-behind, which has no localizer (TD-139).
    /// </summary>
    /// <param name="messages">What <c>UserFacingError.Report</c> or <c>ObservedTask</c> produced.</param>
    public void ReportFailure(IReadOnlyList<UserFacingMessage> messages) => _error.Set(messages);

    /// <summary>Releases the localized message slot.</summary>
    /// <remarks>
    /// CA1001, for the reason every page that owns a slot gives: it subscribes to the process-lifetime
    /// localizer. <c>PageActivation.Deactivate</c> disposes a page that implements this.
    /// </remarks>
    public void Dispose()
    {
        _error.Dispose();
        _emptyLog.Dispose();
        _databaseStatus.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Re-reads every value on the page: runtime facts, database health and the log tail.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PUBLIC AND ALSO A COMMAND. <see cref="ShellViewModel"/> calls it on navigation, and the
    /// generated <c>RefreshCommand</c> lets the user re-read without leaving and re-entering the
    /// page -- which matters because the interesting case is "reproduce the problem, then look",
    /// and a panel that only loads once forces a navigation round trip in the middle of that.
    /// </para>
    /// <para>
    /// IT REPLACES, IT DOES NOT APPEND. <see cref="LogEntries"/> is cleared first, so calling this
    /// twice shows the tail twice over rather than each record twice -- the failure mode of an
    /// append-only refresh being that it manufactures duplicate records, which is fabrication by a
    /// different route.
    /// </para>
    /// <para>
    /// NEITHER SEAM THROWS, by contract, so there is no catch here. A <c>try/catch</c> would be
    /// dead code that reviewers would later "improve" into surfacing <c>ex.Message</c>; the
    /// contracts push failure into <see cref="DatabaseHealthStatus.Faulted"/> and an empty tail
    /// precisely so this method has no exception to be tempted by.
    /// </para>
    /// </remarks>
    /// <inheritdoc />
    public Task InitializeAsync(CancellationToken cancellationToken) => RefreshAsync();

    /// <inheritdoc />
    /// <remarks>
    /// REPORTS TO THE LOG AND NOWHERE ELSE, DELIBERATELY. This view model's own remarks state that
    /// neither of its two seams throws by contract, which is why <see cref="RefreshAsync"/> has no
    /// <c>catch</c>. That contract is still the design; what changed (TD-35) is what happens if it
    /// is ever broken. It used to be nothing at all -- the shell fired
    /// <c>_ = diagnostics.RefreshAsync()</c> and a fault vanished. Now it is an Error record naming
    /// the operation. There is no screen text because this page has no error surface, and inventing
    /// one to display a failure that cannot occur would be worse than the log line -- but the
    /// clipboard copy on this same page CAN fail, routinely, so <see cref="ErrorMessage"/> exists
    /// for that and this policy reports to it too rather than keeping two conventions.
    /// </remarks>
    public InitializationFailurePolicy InitializationFailurePolicy => new(
        _logger,
        "diagnostics-refresh",
        new UserFacingMessage("Diagnostics.Failure.Refresh"),
        _error.Set);

    /// <summary>What the user reads when something on this page fails -- in practice, the clipboard.</summary>
    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsLoading = true;
        try
        {
            AppVersion = _runtimeInfoProvider.AppVersion;
            OsDescription = _runtimeInfoProvider.OsDescription;
            ManagedMemory = FormatBytes(_runtimeInfoProvider.ManagedMemoryBytes);

            var health = await _databaseHealthProbe.CheckAsync(CancellationToken.None).ConfigureAwait(true);
            var (key, args) = DescribeHealthKey(health);
            _databaseStatus.Set(key, args);

            var tail = await _logTailReader.ReadTailAsync(_maxLogEntries, CancellationToken.None).ConfigureAwait(true);

            LogEntries.Clear();
            foreach (var entry in tail)
            {
                LogEntries.Add(new DiagnosticsLogEntryViewModel(entry));
            }

            _emptyLog.Set(NoRecordsKey);
            HasNoLogRecords = LogEntries.Count == 0;
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Empties the terminal view. Touches no file.
    /// </summary>
    /// <remarks>
    /// KEPT, BUT RENAMED. The old command was "Clear" and its last act was to append
    /// <c>"Logs cleared by user."</c> -- wording that told the user Pine had deleted their log, when
    /// in truth it had deleted a screen buffer and then fabricated one more record to say so. The
    /// honest version is worth keeping next to Refresh rather than deleting: the actual workflow on
    /// this page is "clear the view, reproduce the problem, refresh, and read only what the
    /// reproduction produced", and without a clear the user has to find the boundary in 200 lines
    /// by eye. Deleting the file is deliberately NOT offered -- destroying the evidence from a
    /// diagnostics screen is not a feature -- and the empty state says "Refresh to read the log
    /// again" so nobody concludes the records are gone.
    /// </remarks>
    [RelayCommand]
    private void ClearView()
    {
        LogEntries.Clear();
        _emptyLog.Set(ViewClearedKey);
        HasNoLogRecords = true;
    }

    /// <summary>
    /// Maps a health reading onto the card's wording.
    /// </summary>
    /// <remarks>
    /// The mapping lives HERE and not on the probe: wording is a UI concern, and the infrastructure
    /// side must not be in a position where the natural way to explain a failure is to pass its
    /// message along (TD-23).
    /// </remarks>
    /// <param name="health">The reading from <see cref="IDatabaseHealthProbe"/>.</param>
    /// <returns>
    /// A key into the string tables, plus the arguments it needs, carrying no exception detail
    /// (TD-150: was a hard-coded English phrase; the switch now names a key, the way
    /// <c>SyncConflictRowViewModel.KindKeys</c> maps a kind onto its own string-table keys).
    /// </returns>
    private static (string Key, object?[] Args) DescribeHealthKey(DatabaseHealth health)
    {
        if (health is null)
        {
            return (DatabaseUnavailableKey, []);
        }

        return health.Status switch
        {
            DatabaseHealthStatus.Locked => (DatabaseLockedKey, []),
            DatabaseHealthStatus.Healthy => (DatabaseHealthyKey, []),

            // The COUNT is the whole point of this status: "pending migrations" alone does not tell
            // a user whether they are one release behind or looking at a vault from a future build.
            DatabaseHealthStatus.PendingMigrations => (DatabasePendingMigrationsKey, [health.PendingMigrationCount]),

            DatabaseHealthStatus.Unreachable => (DatabaseUnreachableKey, []),
            DatabaseHealthStatus.Faulted => (DatabaseUnavailableKey, []),
            _ => (DatabaseUnavailableKey, []),
        };
    }

    /// <summary>
    /// Renders a managed-heap byte count for the card.
    /// </summary>
    /// <remarks>
    /// MB with one decimal, because the managed heap of an idle Avalonia process sits in the low
    /// tens of megabytes and the old integer-MB division rendered every reading under a megabyte as
    /// a flat "0 MB" -- a real number displayed as a fake one.
    /// </remarks>
    /// <param name="bytes">Bytes on the managed heap.</param>
    /// <returns>The formatted reading, e.g. <c>"42.3 MB"</c>.</returns>
    private static string FormatBytes(long bytes)
    {
        var megabytes = bytes / 1024d / 1024d;
        return string.Create(CultureInfo.CurrentCulture, $"{megabytes:0.0} MB");
    }
}
