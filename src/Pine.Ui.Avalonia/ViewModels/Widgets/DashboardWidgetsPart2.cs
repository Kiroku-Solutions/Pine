using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using MediatR;
using Pine.Application.Budgets.Queries;
using Pine.Application.Transactions.Queries;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.ViewModels.Messages;

namespace Pine.Ui.Avalonia.ViewModels.Widgets;

/// <summary>One currency's line on the "this month" tile, already written out.</summary>
/// <param name="Currency">The ISO code the three figures are in.</param>
/// <param name="Income">What came in, e.g. "3,000.00 USD".</param>
/// <param name="Spending">What went out, as a positive figure.</param>
/// <param name="Net">Income less spending.</param>
/// <param name="IsNetNegative">Whether the month spent more than it earned in this currency.</param>
public sealed record CashFlowRow(string Currency, string Income, string Spending, string Net, bool IsNetNegative);

/// <summary>
/// This month's income, spending and the difference, one line per currency (post-audit plan item 1.3).
/// </summary>
/// <remarks>
/// <para>
/// ONE TILE WHERE THE PLAN NAMED TWO, AND THE REASON. <c>62-Post-Audit-Plan</c> item 1.3 listed income
/// and expenses as separate derivable tiles, and there was a Savings tile beside them. All three are
/// the same read of the same month; as three tiles they would run the query three times and scatter
/// one comparison across the grid. As one tile the figure a user actually wants -- did I spend more
/// than I earned? -- is the third column of the same row.
/// </para>
/// <para>
/// Every rule of the figures is <see cref="GetMonthlyCashFlowQueryHandler"/>'s: the UTC calendar
/// month (the budgets' window), voids excluded, transfers neither earned nor spent, currencies never
/// added together.
/// </para>
/// </remarks>
public partial class CashFlowWidgetViewModel : DashboardWidgetViewModelBase, IRecipient<DataChangedMessage>
{
    private readonly IMediator _mediator;

    public CashFlowWidgetViewModel(IMediator mediator, ILocalizer localizer)
        : base(localizer, "Dashboard.Tile.ThisMonth")
    {
        _mediator = mediator;
        Width = 560;
        Height = 230;
    }

    /// <summary>One row per currency with activity this month.</summary>
    public ObservableCollection<CashFlowRow> Rows { get; } = new();

    /// <summary>Whether anything was earned or spent this month.</summary>
    [ObservableProperty]
    public partial bool HasActivity { get; private set; }

    /// <inheritdoc />
    public override async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var flow = await _mediator.Send(new GetMonthlyCashFlowQuery(), cancellationToken);

        Rows.Clear();
        foreach (var currency in flow.ByCurrency)
        {
            Rows.Add(new CashFlowRow(
                currency.Currency.Value,
                currency.Income.ToString(),
                currency.Spending.ToString(),
                currency.Net.ToString(),
                currency.Net.Amount < 0));
        }

        HasActivity = flow.HasActivity;
        MarkLoaded(!flow.HasActivity);
    }

    /// <summary>Re-reads the month when a transaction was written or voided.</summary>
    /// <param name="message">The broadcast. It carries no payload.</param>
    public void Receive(DataChangedMessage message) => Reload("widget-cash-flow-reload");
}

/// <summary>
/// The budgets, measured against the ledger, most urgent first (post-audit plan item 1.3).
/// </summary>
/// <remarks>
/// <para>
/// THIS TILE SAID "NO DATA YET" WHILE THE BUDGETS PAGE MEASURED EVERY BUDGET. TD-99 made the page
/// true; the dashboard's tile was never wired to it. It now shows the same rows the page shows, built
/// by the same <see cref="BudgetProgressViewModel.From"/>, so an unknown spend is "Not known" in both
/// places and never a zero in either.
/// </para>
/// <para>
/// MOST URGENT FIRST, AND ONLY A FEW. Exceeded budgets lead, then the rest by how much of the limit is
/// used; a budget whose spend is not known sorts last, because "unknown" is not "fine" but nor is it a
/// fraction to rank. The tile shows at most <see cref="MaxRows"/>; <see cref="MoreText"/> says how many
/// were left off, so a vault with twenty budgets is not presented as having four.
/// </para>
/// </remarks>
public partial class BudgetWidgetViewModel : DashboardWidgetViewModelBase, IRecipient<DataChangedMessage>
{
    /// <summary>How many budgets the tile lists before summarising the rest.</summary>
    public const int MaxRows = 4;

    private readonly IMediator _mediator;
    private readonly LocalizedMessageSlot _more;
    private readonly LocalizedMessageSlot _exceeded;

    public BudgetWidgetViewModel(IMediator mediator, ILocalizer localizer)
        : base(localizer, "Dashboard.Tile.Budgets")
    {
        _mediator = mediator;
        _more = CreateSlot(text => MoreText = text ?? string.Empty);
        _exceeded = CreateSlot(text => ExceededText = text ?? string.Empty);
        Width = 360;
        Height = 320;
    }

    /// <summary>The rows shown, most urgent first.</summary>
    public ObservableCollection<BudgetProgressViewModel> Rows { get; } = new();

    /// <summary>Whether there are any budgets at all.</summary>
    [ObservableProperty]
    public partial bool HasBudgets { get; private set; }

    /// <summary>"And N more", or empty when every budget is listed.</summary>
    [ObservableProperty]
    public partial string MoreText { get; private set; } = string.Empty;

    /// <summary>"N over budget", or empty when none is.</summary>
    [ObservableProperty]
    public partial string ExceededText { get; private set; } = string.Empty;

    /// <inheritdoc />
    public override async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var progress = await _mediator.Send(new GetBudgetProgressQuery(), cancellationToken);

        var ranked = progress
            .Select(row => BudgetProgressViewModel.From(row, Localizer))
            .OrderByDescending(row => row.IsExceeded)
            .ThenByDescending(row => row.IsSpendKnown)
            .ThenByDescending(row => row.ProgressPercentage)
            .ToList();

        Rows.Clear();
        foreach (var row in ranked.Take(MaxRows))
        {
            Rows.Add(row);
        }

        HasBudgets = ranked.Count > 0;

        var hidden = ranked.Count - Rows.Count;
        if (hidden > 0)
        {
            _more.Set("Dashboard.Budgets.More", hidden);
        }
        else
        {
            _more.Clear();
        }

        var exceeded = ranked.Count(row => row.IsExceeded);
        if (exceeded > 0)
        {
            _exceeded.Set("Dashboard.Budgets.Exceeded", exceeded);
        }
        else
        {
            _exceeded.Clear();
        }

        MarkLoaded(ranked.Count == 0);
    }

    /// <summary>
    /// Re-builds the rows after a language switch: "Not known" is rendered into the rows, which are
    /// shared with the budgets page, rather than bound live. The category is the user's own label and
    /// is not translated (TD-132, plan item 2.3).
    /// </summary>
    protected override void OnLanguageChanged()
    {
        if (!IsEmpty && Rows.Count > 0)
        {
            Reload("widget-budgets-language-reload");
        }
    }

    /// <summary>Re-reads the budgets when a transaction changed the spend.</summary>
    /// <param name="message">The broadcast. It carries no payload.</param>
    public void Receive(DataChangedMessage message) => Reload("widget-budgets-reload");
}
