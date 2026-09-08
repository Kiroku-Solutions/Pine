using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace Pine.Ui.Avalonia.ViewModels.Widgets;

/// <summary>
/// The shared surface of every dashboard tile.
/// </summary>
/// <remarks>
/// THE BASE IS <see cref="ObservableRecipient"/>, NOT <c>ObservableObject</c>, AND THAT IS
/// DELIBERATE (TD-34). Only two of the twelve widgets consume messages
/// (<see cref="BalanceWidgetViewModel"/> and <see cref="RecentTransactionsWidgetViewModel"/>), so
/// the alternative was a second base class, or those two deriving from <c>ObservableRecipient</c>
/// separately and losing the six layout properties below. Moving the base costs the other ten
/// widgets nothing measurable: <c>ObservableRecipient.OnActivated</c> registers every
/// <c>IRecipient&lt;T&gt;</c> the instance implements, and a widget that implements none registers
/// nothing. What it buys is that <see cref="DashboardViewModel"/> can propagate its own
/// <c>IsActive</c> to <c>Widgets</c> with a single uniform loop instead of a type test per widget.
/// </remarks>
public abstract partial class DashboardWidgetViewModelBase : ObservableRecipient, IDashboardWidgetViewModel
{
    /// <summary>
    /// Where this tile's failures are recorded, assigned by the dashboard that built it (TD-35).
    /// </summary>
    /// <remarks>
    /// Settable, and internal, because the tiles are the one family of view models the container
    /// never sees: <c>DashboardViewModel.InitializeWidgets</c> builds all twelve with <c>new</c>,
    /// so constructor injection is not available and there is no factory to add one to. Before
    /// TD-35 the consequence was concrete rather than theoretical -- the two tiles that read
    /// anything ended their loads with <c>catch (Exception) { /* ignore */ }</c>, so an account
    /// query that failed produced an empty tile, no log record, and a user who believes they have
    /// no accounts. The dashboard assigns its own logger here so those catches could be removed.
    /// </remarks>
    internal ILogger? DiagnosticsLogger { get; set; }

    /// <summary>
    /// Reads whatever this tile shows. Called by the dashboard when the dashboard is initialised.
    /// </summary>
    /// <remarks>
    /// <para>
    /// VIRTUAL AND DOING NOTHING BY DEFAULT, WHICH IS THE HONEST DEFAULT. Ten of the twelve tiles
    /// display constants and have nothing to read; a base that threw <c>NotImplementedException</c>
    /// would force ten empty overrides, and an abstract member would force the same. The two that
    /// do read override it.
    /// </para>
    /// <para>
    /// This replaces <c>_ = LoadDataAsync();</c> in those two constructors (TD-35). The discard was
    /// the same trap as everywhere else -- a faulted load collected in silence -- with the extra
    /// twist that a tile built by the designer's parameterless constructor started no load at all,
    /// so the two paths through the same class disagreed about whether the object had data.
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">Cooperative cancellation, currently unused by the tiles.</param>
    /// <returns>A task that completes when the tile has its data.</returns>
    public virtual Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

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
