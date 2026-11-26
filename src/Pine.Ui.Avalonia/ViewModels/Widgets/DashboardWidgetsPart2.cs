using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using MediatR;
using Pine.Application.Budgets.Queries;
using Pine.Application.Planning;
using Pine.Application.Transactions.Queries;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.ViewModels.Messages;

namespace Pine.Ui.Avalonia.ViewModels.Widgets;

/// <summary>One currency's line on the "this month" tile, with visual comparison bars.</summary>
public sealed record CashFlowRow(
    string Currency,
    string Income,
    string Spending,
    string Net,
    bool IsNetNegative,
    double IncomeBarPercent = 0,
    double SpendingBarPercent = 0,
    string SpentRatioText = "");

/// <summary>This month's income, spending and net with visual comparison bars, one row per currency.</summary>
public partial class CashFlowWidgetViewModel : DashboardWidgetViewModelBase, IRecipient<DataChangedMessage>
{
    private readonly IMediator _mediator;

    public CashFlowWidgetViewModel(IMediator mediator, ILocalizer localizer)
        : base(localizer, "Dashboard.Tile.ThisMonth")
    {
        _mediator = mediator;
        Width = 560;
        Height = 250;
    }

    public ObservableCollection<CashFlowRow> Rows { get; } = new();

    [ObservableProperty]
    public partial bool HasActivity { get; private set; }

    public override async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var flow = await _mediator.Send(new GetMonthlyCashFlowQuery(), cancellationToken);

        Rows.Clear();
        foreach (var currency in flow.ByCurrency)
        {
            var maxVal = Math.Max(currency.Income.Amount, currency.Spending.Amount);
            var incomePct = maxVal > 0m ? (double)(currency.Income.Amount / maxVal) * 100.0 : 0.0;
            var spendingPct = maxVal > 0m ? (double)(currency.Spending.Amount / maxVal) * 100.0 : 0.0;
            var ratioText = currency.Income.Amount > 0m
                ? Localizer.Format("Dashboard.CashFlow.SpentRatio", (int)Math.Round((currency.Spending.Amount / currency.Income.Amount) * 100m))
                : string.Empty;

            Rows.Add(new CashFlowRow(
                currency.Currency.Value,
                currency.Income.ToString(),
                currency.Spending.ToString(),
                currency.Net.ToString(),
                currency.Net.Amount < 0,
                incomePct,
                spendingPct,
                ratioText));
        }

        HasActivity = flow.HasActivity;
        MarkLoaded(!flow.HasActivity);
    }

    protected override void OnLanguageChanged()
    {
        if (HasActivity)
        {
            Reload("widget-cash-flow-language-reload");
        }
    }

    public void Receive(DataChangedMessage message) => Reload("widget-cash-flow-reload");
}

/// <summary>The budgets, measured against the ledger, most urgent first.</summary>
public partial class BudgetWidgetViewModel : DashboardWidgetViewModelBase, IRecipient<DataChangedMessage>
{
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
        Width = 380;
        Height = 340;
    }

    public ObservableCollection<BudgetProgressViewModel> Rows { get; } = new();

    [ObservableProperty]
    public partial bool HasBudgets { get; private set; }

    [ObservableProperty]
    public partial string MoreText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string ExceededText { get; private set; } = string.Empty;

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

    protected override void OnLanguageChanged()
    {
        if (!IsEmpty && Rows.Count > 0)
        {
            Reload("widget-budgets-language-reload");
        }
    }

    public void Receive(DataChangedMessage message) => Reload("widget-budgets-reload");
}

/// <summary>The savings goals tile: circular progress rings and monthly targets at a glance.</summary>
public partial class SavingsGoalsWidgetViewModel : DashboardWidgetViewModelBase, IRecipient<DataChangedMessage>
{
    public const int MaxRows = 3;

    private readonly IMediator _mediator;
    private readonly LocalizedMessageSlot _more;

    public SavingsGoalsWidgetViewModel(IMediator mediator, ILocalizer localizer)
        : base(localizer, "Dashboard.Tile.SavingsGoals")
    {
        _mediator = mediator;
        _more = CreateSlot(text => MoreText = text ?? string.Empty);
        Width = 380;
        Height = 320;
    }

    public ObservableCollection<SavingsGoalRowViewModel> Rows { get; } = new();

    [ObservableProperty]
    public partial bool HasGoals { get; private set; }

    [ObservableProperty]
    public partial string MoreText { get; private set; } = string.Empty;

    public override async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var goals = await _mediator.Send(new GetSavingsGoalsQuery(), cancellationToken) ?? Array.Empty<SavingsGoalSummary>();

        var ordered = goals
            .Select(g => SavingsGoalRowViewModel.From(g, Localizer))
            .OrderBy(g => g.IsCompleted)
            .ThenByDescending(g => g.ProgressPercentage)
            .ToList();

        Rows.Clear();
        foreach (var row in ordered.Take(MaxRows))
        {
            Rows.Add(row);
        }

        HasGoals = ordered.Count > 0;

        var hidden = ordered.Count - Rows.Count;
        if (hidden > 0)
        {
            _more.Set("Dashboard.Goals.More", hidden);
        }
        else
        {
            _more.Clear();
        }

        MarkLoaded(ordered.Count == 0);
    }

    protected override void OnLanguageChanged()
    {
        if (!IsEmpty && Rows.Count > 0)
        {
            Reload("widget-goals-language-reload");
        }
    }

    public void Receive(DataChangedMessage message) => Reload("widget-goals-reload");
}
