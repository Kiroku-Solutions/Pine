using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Pine.Ui.Avalonia.Diagnostics;
using Pine.Ui.Avalonia.Localization;

namespace Pine.Ui.Avalonia.ViewModels.Widgets;

/// <summary>
/// The shared surface of every dashboard tile.
/// </summary>
/// <remarks>
/// <para>
/// EVERY TILE READS SOMETHING, AND THAT IS NOW THE DEFINITION OF A TILE (post-audit plan item 1.3).
/// Ten of the twelve tiles this dashboard used to build rendered "No data yet" for ever -- honest, no
/// fabricated numbers, but furniture. The ones the ledger can feed were built and the rest deleted.
/// <see cref="InitializeAsync"/> is therefore ABSTRACT now; it used to be a virtual that did nothing,
/// which is exactly the default that let ten inert tiles compile. A tile that has nothing to read has
/// no business being a tile, and <c>DashboardTilesTests.EveryTileTheDashboardBuilds_ReadsTheLedger</c>
/// pins that every tile on the dashboard actually reads something -- abstract only forces a body.
/// </para>
/// <para>
/// THE BASE IS <see cref="ObservableRecipient"/>, NOT <c>ObservableObject</c> (TD-34): the dashboard
/// propagates its own <c>IsActive</c> to every tile with one uniform loop, and a tile that implements
/// no <c>IRecipient&lt;T&gt;</c> registers nothing.
/// </para>
/// <para>
/// THE TITLE IS A KEY, RENDERED LIVE. It was an English literal assigned in each constructor, outside
/// the reach of <c>NoHardcodedUiStringsTests</c> because it lived in C# rather than markup, so a
/// Spanish dashboard had English tile headings. It goes through a <see cref="LocalizedMessageSlot"/>,
/// which re-renders on a language switch -- and which subscribes to a process-lifetime localizer, so
/// the tile is <see cref="IDisposable"/> and the dashboard disposes it.
/// </para>
/// </remarks>
public abstract partial class DashboardWidgetViewModelBase : ObservableRecipient, IDashboardWidgetViewModel, IDisposable
{
    private readonly LocalizedMessageSlot _title;
    private readonly LocalizedMessageSlot _failure;
    private readonly System.Collections.Generic.List<LocalizedMessageSlot> _slots = new();
    private bool _disposed;

    /// <summary>Creates a tile titled by <paramref name="titleKey"/>.</summary>
    /// <param name="localizer">The string table the title and the failure sentence come from.</param>
    /// <param name="titleKey">The string ID of this tile's heading.</param>
    protected DashboardWidgetViewModelBase(ILocalizer localizer, string titleKey)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentException.ThrowIfNullOrWhiteSpace(titleKey);

        Localizer = localizer;
        _title = CreateSlot(text => Title = text ?? string.Empty);
        _failure = CreateSlot(text => FailureMessage = text);
        _title.Set(titleKey);
        localizer.LanguageChanged += OnLocalizerLanguageChanged;
    }

    private void OnLocalizerLanguageChanged(object? sender, EventArgs e) => OnLanguageChanged();

    /// <summary>
    /// Called after a language switch, once every slot has re-rendered.
    /// </summary>
    /// <remarks>
    /// For text a tile builds from the localizer OUTSIDE a slot -- the budget rows' "Not known" and
    /// "Unknown category" are rendered into row objects shared with the budgets page. Without this a
    /// Spanish switch left the heading in Spanish and the rows under it in English.
    /// </remarks>
    protected virtual void OnLanguageChanged()
    {
    }

    /// <summary>The string table, for tiles that render text of their own.</summary>
    protected ILocalizer Localizer { get; }

    /// <summary>
    /// Where this tile's failures are recorded, assigned by the dashboard that built it (TD-35).
    /// </summary>
    /// <remarks>
    /// Settable, and internal, because the tiles are built with <c>new</c> by the dashboard and the
    /// container never sees them, so constructor injection is not available.
    /// </remarks>
    internal ILogger? DiagnosticsLogger { get; set; }

    /// <summary>
    /// What the tile says when its read failed, or <see langword="null"/> when it did not.
    /// </summary>
    /// <remarks>
    /// A TILE THAT FAILED MUST NOT LOOK LIKE A TILE WITH NOTHING IN IT (TD-35). An empty budgets tile
    /// reads "you have no budgets" -- a statement about the user's money. A failure clears
    /// <see cref="IsEmpty"/>, so the two can never be on screen together.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFailed))]
    public partial string? FailureMessage { get; private set; }

    /// <summary>Whether <see cref="FailureMessage"/> is showing.</summary>
    public bool HasFailed => !string.IsNullOrEmpty(FailureMessage);

    /// <summary>
    /// Reads whatever this tile shows. Called by the dashboard when the dashboard is initialised.
    /// </summary>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>A task that completes when the tile has its data.</returns>
    public abstract Task InitializeAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Starts a re-read and routes its failure to this tile's own message (TD-35).
    /// </summary>
    /// <param name="operation">A stable name for the log.</param>
    protected void Reload(string operation) => ObservedTask.Run(
        () => InitializeAsync(CancellationToken.None),
        DiagnosticsLogger,
        operation,
        Localizer["Dashboard.Tile.LoadFailed"],
        ReportFailure);

    /// <summary>Shows <paramref name="message"/> as this tile's failure, in place of its empty state.</summary>
    /// <param name="message">An already-rendered sentence, typically from <c>UserFacingError.Report</c>.</param>
    /// <remarks>
    /// IT CLEARS <see cref="IsEmpty"/>, AND THE FIRST VERSION DID NOT. <c>IsEmpty</c> is only written by
    /// a successful read, so a reload that failed after an empty success showed the failure AND "No
    /// budgets yet" at once -- the independent review caught it with a probe. A failed read knows
    /// nothing about emptiness, so it withdraws the claim.
    /// </remarks>
    internal void ReportFailure(string message)
    {
        IsEmpty = false;
        _failure.SetRendered(message);
    }

    /// <summary>
    /// A localized text slot that this tile disposes with itself, for any text that must follow a
    /// language switch.
    /// </summary>
    /// <param name="assign">The tile's own property setter.</param>
    /// <returns>The slot; call <c>Set</c> on it with a key.</returns>
    protected LocalizedMessageSlot CreateSlot(Action<string?> assign)
    {
        var slot = new LocalizedMessageSlot(Localizer, assign);
        _slots.Add(slot);
        return slot;
    }

    /// <summary>
    /// Whether a SUCCESSFUL read found nothing to show -- and only then.
    /// </summary>
    /// <remarks>
    /// FALSE UNTIL A READ HAS SUCCEEDED, DELIBERATELY. Every tile's empty state is a sentence about the
    /// user's money ("No budgets yet", "Add an account"). Shown before the first read lands, it is a
    /// guess; shown after a failed read, it is false. Views bind their empty state to this, never to a
    /// collection's count, so the sentence appears only when the ledger was asked and answered.
    /// </remarks>
    [ObservableProperty]
    public partial bool IsEmpty { get; private set; }

    /// <summary>Records a successful read: clears any failure and says whether it found anything.</summary>
    /// <param name="isEmpty">Whether the read returned nothing to show.</param>
    protected void MarkLoaded(bool isEmpty)
    {
        IsEmpty = isEmpty;
        _failure.Clear();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Localizer.LanguageChanged -= OnLocalizerLanguageChanged;
        foreach (var slot in _slots)
        {
            slot.Dispose();
        }
        GC.SuppressFinalize(this);
    }

    [ObservableProperty]
    public partial string Title { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial int ColumnSpan { get; set; } = 1;

    [ObservableProperty]
    public partial int RowSpan { get; set; } = 1;

    [ObservableProperty]
    public partial int OrderIndex { get; set; } = 0;

    [ObservableProperty]
    public partial double Width { get; set; } = 300;

    [ObservableProperty]
    public partial double Height { get; set; } = 200;
}
