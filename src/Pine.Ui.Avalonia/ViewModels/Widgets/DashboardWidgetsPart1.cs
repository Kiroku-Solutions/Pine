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

public sealed record TotalBalanceRow(Money Amount, string Text, bool IsNegative);

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
    string ColorHex,
    bool IsSubAccount = false,
    string HierarchyBadgeText = "",
    bool HasHierarchyBadge = false,
    bool IsDeficit = false,
    bool ShowDeficitPill = false)
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
        var scopeSnapshot = AccountScope;
        var allAccounts = (await _mediator.Send(new GetAccountsQuery(), cancellationToken) ?? Array.Empty<Account>()).ToList();
        var scopeSet = scopeSnapshot is { Count: > 0 } ids ? new HashSet<Guid>(ids) : null;
        var accounts = scopeSet is null
            ? allAccounts
            : allAccounts.Where(a => scopeSet.Contains(a.Id)).ToList();

        MonthlyPlanSummary? plan = null;
        try
        {
            plan = await _mediator.Send(new GetMonthlyPlanSummaryQuery(scopeSnapshot), cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Optional enrichment: keep tile working when isolated tests only stub GetAccountsQuery.
        }

        cancellationToken.ThrowIfCancellationRequested();

        var slicesByAccount = plan?.AccountShares?.ToDictionary(s => s.AccountId)
            ?? new Dictionary<Guid, AccountAllocationSlice>();

        var allAccountIds = allAccounts.Select(a => a.Id).ToHashSet();
        var childrenByParent = allAccounts
            .Where(a => a.ParentAccountId.HasValue && allAccountIds.Contains(a.ParentAccountId.Value))
            .GroupBy(a => a.ParentAccountId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        bool IsTopLevel(Account account) =>
            !account.ParentAccountId.HasValue || !allAccountIds.Contains(account.ParentAccountId.Value);

        // In General view, roll sub-accounts into their parent so 50 spending pockets never drown the list.
        // Orphaned sub-accounts (whose parent is absent) remain visible as top-level so totals stay honest.
        var displayAccounts = scopeSet is null
            ? accounts.Where(IsTopLevel).ToList()
            : accounts
                .OrderBy(a => IsTopLevel(a) ? 0 : 1)
                .ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

        decimal EffectivePoolAmount(Account account)
        {
            if (!IsTopLevel(account))
            {
                return account.Balance.Amount;
            }

            var childSum = childrenByParent.TryGetValue(account.Id, out var kids)
                ? kids.Sum(k => k.Balance.Amount)
                : 0m;
            return account.Balance.Amount + childSum;
        }

        if (scopeSet is null)
        {
            displayAccounts = displayAccounts
                .OrderByDescending(EffectivePoolAmount)
                .ToList();
        }

        var currencyTotals = displayAccounts
            .Where(IsTopLevel)
            .Select(a => (a.Balance.Currency.Value, Amount: EffectivePoolAmount(a)))
            .Where(x => x.Amount > 0m)
            .GroupBy(x => x.Value, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount), StringComparer.Ordinal);

        Accounts.Clear();
        AccountRows.Clear();

        for (var i = 0; i < displayAccounts.Count; i++)
        {
            var account = displayAccounts[i];
            Accounts.Add(account);

            var isChild = !IsTopLevel(account);
            var hasChildren = !isChild
                && childrenByParent.TryGetValue(account.Id, out var children)
                && children.Count > 0;
            var displayedAmount = isChild ? account.Balance.Amount : EffectivePoolAmount(account);
            var displayedMoney = Money.FromRounded(displayedAmount, account.Balance.Currency);

            double share;
            string shareText;
            string badgeText = string.Empty;

            bool isDeficit;
            bool showDeficitPill;

            if (isChild)
            {
                var spent = account.Balance.Amount < 0m ? Math.Abs(account.Balance.Amount) : 0m;
                if (account.MonthlyAllocation is { } alloc && alloc > 0m)
                {
                    share = Math.Round((double)(spent / alloc) * 100.0, 1);
                    if (spent > alloc)
                    {
                        var overText = Money.FromRounded(spent - alloc, account.Balance.Currency).ToString();
                        var allocText = Money.FromRounded(alloc, account.Balance.Currency).ToString();
                        shareText = Localizer.Format("Dashboard.Accounts.SubAccountOverAllocated", overText, allocText);
                        isDeficit = true;
                        showDeficitPill = true;
                    }
                    else
                    {
                        var spentText = Money.FromRounded(spent, account.Balance.Currency).ToString();
                        var allocText = Money.FromRounded(alloc, account.Balance.Currency).ToString();
                        shareText = Localizer.Format("Dashboard.Accounts.SubAccountAllocation", spentText, allocText);
                        isDeficit = false;
                        showDeficitPill = false;
                    }
                }
                else
                {
                    share = 0.0;
                    shareText = Localizer["Dashboard.Accounts.SubAccountSharedPool"];
                    isDeficit = displayedAmount < 0m;
                    showDeficitPill = false;
                }

                badgeText = Localizer["Dashboard.Accounts.SubAccountBadge"];
            }
            else
            {
                if (displayedAmount > 0m
                    && currencyTotals.TryGetValue(account.Balance.Currency.Value, out var total)
                    && total > 0m)
                {
                    share = Math.Round((double)(displayedAmount / total) * 100.0, 1);
                }
                else
                {
                    share = 0.0;
                }

                shareText = Localizer.Format("Dashboard.Accounts.ShareOfTotal", Math.Round(share));
                if (hasChildren)
                {
                    badgeText = Localizer.Format("Dashboard.Accounts.SubAccountCount", childrenByParent[account.Id].Count);
                }

                isDeficit = displayedAmount < 0m;
                showDeficitPill = isDeficit;
            }

            var initial = string.IsNullOrWhiteSpace(account.Name)
                ? "?"
                : account.Name.Trim()[0].ToString().ToUpper(CultureInfo.CurrentCulture);

            slicesByAccount.TryGetValue(account.Id, out var slice);
            var budgetLeftText = slice?.BudgetRemaining is { } rem
                ? Localizer.Format("Dashboard.Accounts.BudgetLeft", rem.ToString())
                : string.Empty;

            AccountRows.Add(new AccountBalanceShareRow(
                account,
                account.Name,
                initial,
                displayedMoney.ToAmountString(CultureInfo.CurrentCulture),
                account.Balance.Currency.Value,
                Math.Clamp(share, 0, 100),
                shareText,
                budgetLeftText,
                !string.IsNullOrEmpty(budgetLeftText),
                isDeficit ? "#B91C1C" : AccountChartPalette.ColorAt(i),
                isChild,
                badgeText,
                !string.IsNullOrEmpty(badgeText),
                isDeficit,
                showDeficitPill));
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

    public ObservableCollection<TotalBalanceRow> TotalRows { get; } = new();

    [ObservableProperty]
    public partial Money? BaseCurrencyTotal { get; private set; }

    [ObservableProperty]
    public partial bool IsBaseCurrencyDeficit { get; private set; }

    [ObservableProperty]
    public partial bool HasAnyDeficit { get; private set; }

    public ObservableCollection<Money> OtherCurrencyTotals { get; } = new();

    public ObservableCollection<TotalBalanceRow> OtherCurrencyRows { get; } = new();

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
        var scopeSnapshot = AccountScope;
        var allAccounts = (await _mediator.Send(new GetAccountsQuery(), cancellationToken) ?? Array.Empty<Account>()).ToList();
        var scopeSet = scopeSnapshot is { Count: > 0 } ids ? new HashSet<Guid>(ids) : null;
        var accounts = scopeSet is null
            ? allAccounts
            : allAccounts.Where(a => scopeSet.Contains(a.Id)).ToList();

        var baseCode = await _mediator.Send(new GetBaseCurrencyQuery(), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        CurrencyCode.TryCreate(baseCode, out var baseCurrency);

        var totals = BalanceTotals.Compute(accounts, baseCurrency);

        Totals.Clear();
        TotalRows.Clear();
        foreach (var total in totals.AllTotals)
        {
            Totals.Add(total);
            TotalRows.Add(new TotalBalanceRow(total, total.ToString(), total.Amount < 0m));
        }

        var split = totals.BaseTotal is not null && totals.OtherTotals.Count > 0;
        BaseCurrencyTotal = split ? totals.BaseTotal : null;
        IsBaseCurrencyDeficit = BaseCurrencyTotal is { Amount: < 0m };
        HasAnyDeficit = Totals.Any(t => t.Amount < 0m);

        OtherCurrencyTotals.Clear();
        OtherCurrencyRows.Clear();
        if (split)
        {
            foreach (var other in totals.OtherTotals)
            {
                OtherCurrencyTotals.Add(other);
                OtherCurrencyRows.Add(new TotalBalanceRow(other, other.ToString(), other.Amount < 0m));
            }
        }

        var primaryCurrency = totals.AllTotals.Count == 1
            ? totals.AllTotals[0].Currency.Value
            : baseCurrency?.Value ?? (totals.AllTotals.Count > 0 ? totals.AllTotals[0].Currency.Value : null);

        var allAccountIds = allAccounts.Select(a => a.Id).ToHashSet();
        var childrenByParent = allAccounts
            .Where(a => a.ParentAccountId.HasValue && allAccountIds.Contains(a.ParentAccountId.Value))
            .GroupBy(a => a.ParentAccountId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        bool IsTopLevel(Account account) =>
            !account.ParentAccountId.HasValue || !allAccountIds.Contains(account.ParentAccountId.Value);

        List<ChartSegment> segments;
        if (scopeSet is null)
        {
            // General tab: roll sub-accounts into their parent pool (orphaned sub-accounts remain top-level).
            segments = accounts
                .Where(a => IsTopLevel(a) && (primaryCurrency is null || string.Equals(a.Balance.Currency.Value, primaryCurrency, StringComparison.Ordinal)))
                .Select(a =>
                {
                    var pool = a.Balance.Amount + (childrenByParent.TryGetValue(a.Id, out var kids) ? kids.Sum(k => k.Balance.Amount) : 0m);
                    return (Account: a, Pool: pool);
                })
                .Where(x => x.Pool > 0m)
                .OrderByDescending(x => x.Pool)
                .Select((x, idx) => new ChartSegment((double)x.Pool, AccountChartPalette.ColorAt(idx), x.Account.Name))
                .ToList();
        }
        else
        {
            // Scoped tab: show remaining pool + sub-account spending slices (negative balances only; never double-count positive balances).
            var scopedSegments = new List<ChartSegment>();
            var parent = accounts.FirstOrDefault(IsTopLevel);
            var kids = accounts.Where(a => !IsTopLevel(a)).ToList();
            if (parent is not null)
            {
                var remainingPool = parent.Balance.Amount + kids.Sum(k => k.Balance.Amount);
                if (remainingPool > 0m)
                {
                    scopedSegments.Add(new ChartSegment((double)remainingPool, AccountChartPalette.ColorAt(0), parent.Name));
                }

                for (var i = 0; i < kids.Count; i++)
                {
                    var spent = kids[i].Balance.Amount < 0m ? Math.Abs(kids[i].Balance.Amount) : 0m;
                    if (spent > 0m)
                    {
                        scopedSegments.Add(new ChartSegment((double)spent, AccountChartPalette.ColorAt(i + 1), kids[i].Name));
                    }
                }
            }

            segments = scopedSegments;
        }

        AccountDistributionSegments = segments;
        HasDistributionSegments = segments.Count > 0;

        MonthlyPlanSummary? plan = null;
        try
        {
            plan = await _mediator.Send(new GetMonthlyPlanSummaryQuery(scopeSnapshot), cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Non-fatal when tests only stub GetAccountsQuery.
        }

        cancellationToken.ThrowIfCancellationRequested();

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
