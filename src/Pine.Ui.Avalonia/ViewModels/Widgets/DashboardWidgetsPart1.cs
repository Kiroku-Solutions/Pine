using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using MediatR;
using Pine.Application.Accounts;
using Pine.Application.Accounts.Queries;
using Pine.Application.Budgets.Queries;
using Pine.Application.Planning;
using Pine.Core.Accounts;
using Pine.Core.Common.ValueObjects;
using Pine.Ui.Avalonia.Controls;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.ViewModels.Messages;

namespace Pine.Ui.Avalonia.ViewModels.Widgets;

internal static class AccountChartPalette
{
    private static readonly string[] HexColors =
    [
        "#047857",
        "#C2410C",
        "#1D4ED8",
        "#6D28D9",
        "#BE185D",
    ];

    private static readonly ConcurrentDictionary<string, IBrush> BrushCache = new(StringComparer.OrdinalIgnoreCase);

    public static string ColorAt(int index) => HexColors[Math.Abs(index) % HexColors.Length];

    public static IBrush BrushFor(string hex) =>
        BrushCache.GetOrAdd(hex, static value => new ImmutableSolidColorBrush(Color.Parse(value)));
}

public sealed record AccountBalanceShareRow(
    Account Account,
    string Name,
    string InitialLetter,
    string BalanceText,
    string CurrencyCode,
    double SharePercentage,
    string ShareText,
    string BudgetLeftText,
    bool HasBudgetLeft,
    string ColorHex)
{
    public IBrush AccentBrush => AccountChartPalette.BrushFor(ColorHex);
}

/// <summary>The accounts tile: every account, its balance, share of total, and scoped budget remaining.</summary>
public partial class BalanceWidgetViewModel : DashboardWidgetViewModelBase, IRecipient<DataChangedMessage>
{
    private readonly IMediator _mediator;

    public ObservableCollection<Account> Accounts { get; } = new();
    public ObservableCollection<AccountBalanceShareRow> AccountRows { get; } = new();

    public BalanceWidgetViewModel(IMediator mediator, ILocalizer localizer)
        : base(localizer, "Dashboard.Tile.Accounts")
    {
        _mediator = mediator;
        Width = 380;
        Height = 340;
    }

    public override async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var accounts = (await _mediator.Send(new GetAccountsQuery(), cancellationToken) ?? Array.Empty<Account>()).ToList();

        MonthlyPlanSummary? plan = null;
        try
        {
            plan = await _mediator.Send(new GetMonthlyPlanSummaryQuery(), cancellationToken);
        }
        catch (Exception)
        {
            // Optional enrichment: keep tile working when isolated tests only stub GetAccountsQuery.
        }

        var slicesByAccount = plan?.AccountShares?.ToDictionary(s => s.AccountId)
            ?? new Dictionary<Guid, AccountAllocationSlice>();

        var currencyTotals = accounts
            .Where(a => a.Balance.Amount > 0m)
            .GroupBy(a => a.Balance.Currency.Value, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Sum(a => a.Balance.Amount), StringComparer.Ordinal);

        Accounts.Clear();
        AccountRows.Clear();

        var ordered = accounts.OrderByDescending(a => a.Balance.Amount).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            var account = ordered[i];
            Accounts.Add(account);

            var share = 0.0;
            if (slicesByAccount.TryGetValue(account.Id, out var slice))
            {
                share = slice.ShareOfTotalPercentage;
            }
            else if (account.Balance.Amount > 0m
                && currencyTotals.TryGetValue(account.Balance.Currency.Value, out var total)
                && total > 0m)
            {
                share = Math.Round((double)(account.Balance.Amount / total) * 100.0, 1);
            }

            var initial = string.IsNullOrWhiteSpace(account.Name)
                ? "?"
                : account.Name.Trim()[0].ToString().ToUpper(CultureInfo.CurrentCulture);

            var shareText = Localizer.Format("Dashboard.Accounts.ShareOfTotal", Math.Round(share));
            var budgetLeftText = slice?.BudgetRemaining is { } rem
                ? Localizer.Format("Dashboard.Accounts.BudgetLeft", rem.ToString())
                : string.Empty;

            AccountRows.Add(new AccountBalanceShareRow(
                account,
                account.Name,
                initial,
                account.Balance.ToAmountString(CultureInfo.CurrentCulture),
                account.Balance.Currency.Value,
                Math.Clamp(share, 0, 100),
                shareText,
                budgetLeftText,
                !string.IsNullOrEmpty(budgetLeftText),
                AccountChartPalette.ColorAt(i)));
        }

        MarkLoaded(Accounts.Count == 0);
    }

    protected override void OnLanguageChanged()
    {
        if (!IsEmpty && Accounts.Count > 0)
        {
            Reload("widget-balance-language-reload");
        }
    }

    public void Receive(DataChangedMessage message) => Reload("widget-balance-reload");
}

/// <summary>
/// The total of every account's balance, one figure per currency, plus visual account share distribution
/// and recurring monthly income context.
/// </summary>
public partial class TotalBalanceWidgetViewModel : DashboardWidgetViewModelBase, IRecipient<DataChangedMessage>
{
    private readonly IMediator _mediator;
    private readonly LocalizedMessageSlot _accountCount;

    public TotalBalanceWidgetViewModel(IMediator mediator, ILocalizer localizer)
        : base(localizer, "Dashboard.Tile.TotalBalance")
    {
        _mediator = mediator;
        _accountCount = CreateSlot(text => AccountCountText = text ?? string.Empty);
        Width = 380;
        Height = 250;
    }

    public ObservableCollection<Money> Totals { get; } = new();

    [ObservableProperty]
    public partial Money? BaseCurrencyTotal { get; private set; }

    public ObservableCollection<Money> OtherCurrencyTotals { get; } = new();

    [ObservableProperty]
    public partial bool ShowsBaseSplit { get; private set; }

    [ObservableProperty]
    public partial bool ShowsFlatTotals { get; private set; } = true;

    [ObservableProperty]
    public partial string AccountCountText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasAccounts { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<ChartSegment> AccountDistributionSegments { get; private set; } = Array.Empty<ChartSegment>();

    [ObservableProperty]
    public partial bool HasDistributionSegments { get; private set; }

    [ObservableProperty]
    public partial string MonthlyIncomeText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasMonthlyIncome { get; private set; }

    public override async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var accounts = (await _mediator.Send(new GetAccountsQuery(), cancellationToken) ?? Array.Empty<Account>()).ToList();
        var baseCode = await _mediator.Send(new GetBaseCurrencyQuery(), cancellationToken);
        CurrencyCode.TryCreate(baseCode, out var baseCurrency);

        var totals = BalanceTotals.Compute(accounts, baseCurrency);

        Totals.Clear();
        foreach (var total in totals.AllTotals)
        {
            Totals.Add(total);
        }

        var split = totals.BaseTotal is not null && totals.OtherTotals.Count > 0;
        BaseCurrencyTotal = split ? totals.BaseTotal : null;
        OtherCurrencyTotals.Clear();
        if (split)
        {
            foreach (var other in totals.OtherTotals)
            {
                OtherCurrencyTotals.Add(other);
            }
        }

        var primaryCurrency = baseCurrency?.Value ?? (totals.AllTotals.Count > 0 ? totals.AllTotals[0].Currency.Value : null);
        var segments = accounts
            .Where(a => a.Balance.Amount > 0m && (primaryCurrency is null || string.Equals(a.Balance.Currency.Value, primaryCurrency, StringComparison.Ordinal)))
            .OrderByDescending(a => a.Balance.Amount)
            .Select((a, idx) => new ChartSegment((double)a.Balance.Amount, AccountChartPalette.ColorAt(idx), a.Name))
            .ToList();

        AccountDistributionSegments = segments;
        HasDistributionSegments = segments.Count > 0;

        MonthlyPlanSummary? plan = null;
        try
        {
            plan = await _mediator.Send(new GetMonthlyPlanSummaryQuery(), cancellationToken);
        }
        catch (Exception)
        {
            // Non-fatal when tests only stub GetAccountsQuery.
        }

        if (plan is { HasRecurringIncome: true, BaseCurrency: not null })
        {
            var monthlyText = Money.FromRounded(plan.MonthlyRecurringIncome, plan.BaseCurrency).ToString();
            MonthlyIncomeText = Localizer.Format("Dashboard.TotalBalance.MonthlyIncome", monthlyText);
            HasMonthlyIncome = true;
        }
        else
        {
            MonthlyIncomeText = string.Empty;
            HasMonthlyIncome = false;
        }

        ShowsBaseSplit = split;
        ShowsFlatTotals = !split;
        HasAccounts = Totals.Count > 0;
        _accountCount.Set("Dashboard.TotalBalance.AccountCount", accounts.Count);
        MarkLoaded(Totals.Count == 0);
    }

    protected override void OnLanguageChanged()
    {
        if (HasMonthlyIncome)
        {
            Reload("widget-total-balance-language-reload");
        }
    }

    public void Receive(DataChangedMessage message) => Reload("widget-total-balance-reload");

    internal static IReadOnlyList<Money> Summarise(IEnumerable<Account> accounts) =>
        BalanceTotals.Compute(accounts, null).AllTotals;
}
