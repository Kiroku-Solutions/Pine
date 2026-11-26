using System;
using System.Threading;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MediatR;
using Pine.Application.Accounts.Queries;
using Pine.Application.Budgets.Commands;
using Pine.Application.Budgets.Queries;
using Pine.Application.Planning;
using Pine.Core.Accounts;
using Pine.Core.Budgets;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Common;
using Pine.Core.Planning;
using Pine.Ui.Avalonia.Controls;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.Services;
using Pine.Ui.Avalonia.Diagnostics;
using Pine.Ui.Avalonia.ViewModels.Messages;
using Pine.Ui.Avalonia.ViewModels.Widgets;

namespace Pine.Ui.Avalonia.ViewModels;

public partial class BudgetProgressViewModel : ObservableObject
{
    public static BudgetProgressViewModel From(BudgetProgress row, ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(localizer);

        var vm = new BudgetProgressViewModel
        {
            BudgetId = row.BudgetId,
            CategoryName = row.Category,
            PeriodText = localizer[PeriodKey(row.Period)],
            AccountId = row.AccountId,
            AccountName = row.AccountName ?? string.Empty,
            LimitAmount = row.Limit.Amount,
            CurrencyCode = row.Limit.Currency.Value,
            IsSpendKnown = row.IsSpendKnown,
            SpentAmount = row.Spent?.Amount ?? 0m,
            UnknownSpendText = localizer["Budgets.Progress.SpendUnknown"],
        };

        vm.UpdateStatusText(localizer);
        return vm;
    }

    public static string PeriodKey(BudgetPeriod period) => period switch
    {
        BudgetPeriod.Weekly => "Budgets.Period.Weekly",
        BudgetPeriod.BiWeekly => "Budgets.Period.BiWeekly",
        BudgetPeriod.Monthly => "Budgets.Period.Monthly",
        BudgetPeriod.Yearly => "Budgets.Period.Yearly",
        BudgetPeriod.Custom => "Budgets.Period.Custom",
        _ => throw new ArgumentOutOfRangeException(nameof(period), period, "A budget period with no name on screen."),
    };

    [ObservableProperty]
    public partial Guid BudgetId { get; set; }

    [ObservableProperty]
    public partial string PeriodText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CategoryName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial Guid? AccountId { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAccountScope))]
    public partial string AccountName { get; set; } = string.Empty;

    public bool HasAccountScope => !string.IsNullOrWhiteSpace(AccountName);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LimitAmountText))]
    [NotifyPropertyChangedFor(nameof(ProgressPercentage))]
    [NotifyPropertyChangedFor(nameof(ClampedProgressPercentage))]
    [NotifyPropertyChangedFor(nameof(IsExceeded))]
    [NotifyPropertyChangedFor(nameof(IsNearLimit))]
    [NotifyPropertyChangedFor(nameof(IsHealthy))]
    public partial decimal LimitAmount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SpentAmountText))]
    [NotifyPropertyChangedFor(nameof(ProgressPercentage))]
    [NotifyPropertyChangedFor(nameof(ClampedProgressPercentage))]
    [NotifyPropertyChangedFor(nameof(IsExceeded))]
    [NotifyPropertyChangedFor(nameof(IsNearLimit))]
    [NotifyPropertyChangedFor(nameof(IsHealthy))]
    public partial decimal SpentAmount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SpentAmountText))]
    [NotifyPropertyChangedFor(nameof(LimitAmountText))]
    public partial string CurrencyCode { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SpentAmountText))]
    [NotifyPropertyChangedFor(nameof(ProgressPercentage))]
    [NotifyPropertyChangedFor(nameof(ClampedProgressPercentage))]
    [NotifyPropertyChangedFor(nameof(IsExceeded))]
    [NotifyPropertyChangedFor(nameof(IsNearLimit))]
    [NotifyPropertyChangedFor(nameof(IsHealthy))]
    public partial bool IsSpendKnown { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SpentAmountText))]
    public partial string UnknownSpendText { get; set; } = "—";

    [ObservableProperty]
    public partial string RemainingStatusText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PercentBadgeText { get; set; } = string.Empty;

    public double ProgressPercentage =>
        IsSpendKnown && LimitAmount > 0 ? (double)(SpentAmount / LimitAmount) * 100 : 0;

    public double ClampedProgressPercentage => Math.Clamp(ProgressPercentage, 0, 100);

    public bool IsExceeded => IsSpendKnown && SpentAmount > LimitAmount;

    public bool IsNearLimit => IsSpendKnown && !IsExceeded && ProgressPercentage >= 75.0;

    public bool IsHealthy => IsSpendKnown && !IsExceeded && !IsNearLimit;

    public string SpentAmountText => IsSpendKnown ? FormatAtCurrencyScale(SpentAmount) : UnknownSpendText;

    public string LimitAmountText => FormatAtCurrencyScale(LimitAmount);

    private void UpdateStatusText(ILocalizer localizer)
    {
        if (!IsSpendKnown)
        {
            RemainingStatusText = string.Empty;
            PercentBadgeText = "—";
            return;
        }

        var pct = (int)Math.Round(ProgressPercentage);
        PercentBadgeText = localizer.Format("Budgets.Progress.PercentUsed", pct);

        if (IsExceeded)
        {
            var over = FormatMoneyWithCode(SpentAmount - LimitAmount);
            RemainingStatusText = $"-{over} · {localizer.Format("Budgets.Progress.OverBy", over)}";
        }
        else
        {
            var left = FormatMoneyWithCode(LimitAmount - SpentAmount);
            RemainingStatusText = localizer.Format("Budgets.Progress.Remaining", left);
        }
    }

    private string FormatMoneyWithCode(decimal amount) =>
        string.IsNullOrWhiteSpace(CurrencyCode)
            ? FormatAtCurrencyScale(amount)
            : $"{FormatAtCurrencyScale(amount)} {CurrencyCode}";

    private string FormatAtCurrencyScale(decimal amount)
    {
        if (string.IsNullOrWhiteSpace(CurrencyCode))
        {
            return amount.ToString("N2", CultureInfo.CurrentCulture);
        }

        var currency = Pine.Core.Common.ValueObjects.CurrencyCode.FromStoredValue(CurrencyCode);
        return Money.FromRounded(amount, currency).ToAmountString(CultureInfo.CurrentCulture);
    }
}

public sealed class RecurringIncomeRowViewModel : ObservableObject
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string AmountText { get; init; } = string.Empty;
    public string AccountName { get; init; } = string.Empty;
    public string ScheduleText { get; init; } = string.Empty;
    public string NextPaydayText { get; init; } = string.Empty;
    public string StatusText { get; init; } = string.Empty;
    public string ToggleLabel { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public bool IsDueNow { get; init; }

    public static RecurringIncomeRowViewModel From(RecurringIncomeSummary item, ILocalizer localizer)
    {
        var freqKey = item.Frequency switch
        {
            RecurringFrequency.BiWeekly => "Planning.Income.Freq.BiWeekly",
            RecurringFrequency.Weekly => "Planning.Income.Freq.Weekly",
            _ => "Planning.Income.Freq.Monthly",
        };

        return new RecurringIncomeRowViewModel
        {
            Id = item.Id,
            Name = item.Name,
            AmountText = item.Amount.ToString(),
            AccountName = item.AccountName,
            ScheduleText = localizer.Format("Planning.Income.DaySummary", item.DayOfMonth, localizer[freqKey]),
            NextPaydayText = localizer.Format("Planning.Income.NextPayday", item.NextPayday.ToString("d", CultureInfo.CurrentCulture)),
            StatusText = localizer[item.IsActive ? "Planning.Income.Active" : "Planning.Income.Paused"],
            ToggleLabel = localizer[item.IsActive ? "Planning.Income.Pause" : "Planning.Income.Resume"],
            IsActive = item.IsActive,
            IsDueNow = item.IsDueNow,
        };
    }
}

public sealed partial class SavingsGoalRowViewModel : ObservableObject
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string LinkedAccountName { get; init; } = string.Empty;
    public bool HasLinkedAccount => !string.IsNullOrWhiteSpace(LinkedAccountName);
    public string SavedText { get; init; } = string.Empty;
    public string TargetText { get; init; } = string.Empty;
    public string RemainingText { get; init; } = string.Empty;
    public string MonthlyChipText { get; init; } = string.Empty;
    public string MonthsLeftText { get; init; } = string.Empty;
    public string QuickAddText { get; init; } = string.Empty;
    public decimal QuickAddAmount { get; init; }
    public double ProgressPercentage { get; init; }
    public bool IsCompleted { get; init; }
    public bool HasMonthlyTarget => QuickAddAmount > 0m;
    public string CenterPercentText => string.Create(CultureInfo.CurrentCulture, $"{Math.Round(ProgressPercentage):0}%");

    [ObservableProperty]
    public partial bool IsEnteringCustomAmount { get; set; }

    [ObservableProperty]
    public partial decimal? CustomContributeAmount { get; set; }

    public static SavingsGoalRowViewModel From(SavingsGoalSummary goal, ILocalizer localizer)
    {
        var quickAmount = goal.MonthlyTarget.Amount > 0m
            ? goal.MonthlyTarget.Amount
            : Math.Min(goal.RemainingAmount.Amount > 0m ? goal.RemainingAmount.Amount : 10m, 50m);

        var monthsText = goal.IsCompleted
            ? localizer["Planning.Goals.Completed"]
            : goal.EstimatedMonthsRemaining is { } months
                ? localizer.Format("Planning.Goals.MonthsLeft", months)
                : string.Empty;

        return new SavingsGoalRowViewModel
        {
            Id = goal.Id,
            Name = goal.Name,
            LinkedAccountName = goal.LinkedAccountName ?? string.Empty,
            SavedText = goal.CurrentAmount.ToString(),
            TargetText = goal.TargetAmount.ToString(),
            RemainingText = goal.IsCompleted
                ? localizer["Planning.Goals.Completed"]
                : localizer.Format("Planning.Goals.Remaining", goal.RemainingAmount.ToString()),
            MonthlyChipText = goal.MonthlyTarget.Amount > 0m
                ? localizer.Format("Planning.Goals.MonthlyChip", goal.MonthlyTarget.ToString())
                : string.Empty,
            MonthsLeftText = monthsText,
            QuickAddAmount = quickAmount,
            QuickAddText = quickAmount > 0m
                ? localizer.Format("Planning.Goals.QuickAdd", Money.FromRounded(quickAmount, goal.TargetAmount.Currency).ToString())
                : localizer["Planning.Goals.AddCustom"],
            ProgressPercentage = goal.ProgressPercentage,
            IsCompleted = goal.IsCompleted,
        };
    }
}

public sealed record AccountScopeOption(Guid? AccountId, string Label)
{
    public override string ToString() => Label;
}

public sealed record FrequencyOption(RecurringFrequency Frequency, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// The Planning &amp; Budgets hub: monthly income allocation bar, category/account budgets,
/// recurring incomes, and savings goals in one intuitive 3-tab mobile-first view.
/// </summary>
public partial class BudgetsViewModel : ObservableRecipient, IDashboardWidgetViewModel, IRecipient<DataChangedMessage>, IAsyncInitializable, IDisposable
{
    private readonly LocalizedMessageSlot _error;
    private readonly LocalizedMessageSlot _createMessage;
    private readonly LocalizedMessageSlot _incomeMessage;
    private readonly LocalizedMessageSlot _goalMessage;
    private readonly LocalizedMessageSlot _autoPostNotice;

    private readonly IMediator _mediator;
    private readonly ILocalizer _localizer;
    private readonly ILogger<BudgetsViewModel>? _logger;

    private IReadOnlyList<BudgetProgress> _lastProgress = Array.Empty<BudgetProgress>();
    private IReadOnlyList<RecurringIncomeSummary> _lastIncomes = Array.Empty<RecurringIncomeSummary>();
    private IReadOnlyList<SavingsGoalSummary> _lastGoals = Array.Empty<SavingsGoalSummary>();
    private MonthlyPlanSummary? _lastPlanSummary;
    private int _loadGeneration;

    public string Title { get; set; } = "Budgets";
    public int ColumnSpan { get; set; } = 2;
    public int RowSpan { get; set; } = 1;
    public int OrderIndex { get; set; } = 3;
    public double Width { get; set; } = 400;
    public double Height { get; set; } = 300;

    // ─── Tab state (0 = Budgets, 1 = Recurring Income, 2 = Savings Goals) ───
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBudgetsTab))]
    [NotifyPropertyChangedFor(nameof(IsIncomeTab))]
    [NotifyPropertyChangedFor(nameof(IsGoalsTab))]
    public partial int ActiveTabIndex { get; set; }

    public bool IsBudgetsTab => ActiveTabIndex == 0;
    public bool IsIncomeTab => ActiveTabIndex == 1;
    public bool IsGoalsTab => ActiveTabIndex == 2;

    [RelayCommand]
    private void SelectTab(string indexText)
    {
        if (int.TryParse(indexText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) && index is >= 0 and <= 2)
        {
            ActiveTabIndex = index;
        }
    }

    // ─── Monthly Plan Hero Summary ───
    [ObservableProperty]
    public partial bool HasRecurringIncomePlan { get; set; }

    [ObservableProperty]
    public partial bool IsPlanOverAllocated { get; set; }

    [ObservableProperty]
    public partial string PlanMonthlyIncomeText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PlanBudgetsAllocatedText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PlanSavingsAllocatedText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PlanUnallocatedText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PlanOverAllocatedWarning { get; set; } = string.Empty;

    [ObservableProperty]
    public partial IReadOnlyList<ChartSegment> PlanAllocationSegments { get; set; } = Array.Empty<ChartSegment>();

    [ObservableProperty]
    public partial string AutoPostNotice { get; set; } = string.Empty;

    // ─── Budgets State (preserved public surface for existing tests) ───
    [ObservableProperty]
    public partial ObservableCollection<BudgetProgressViewModel> Budgets { get; set; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(IsIncomesEmpty))]
    [NotifyPropertyChangedFor(nameof(IsGoalsEmpty))]
    public partial bool IsLoading { get; set; }

    public bool IsEmpty => !IsLoading && !IsCreating && Budgets.Count == 0 && string.IsNullOrEmpty(ErrorMessage);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool IsCreating { get; set; }

    [ObservableProperty]
    public partial bool IsSaving { get; set; }

    [ObservableProperty]
    public partial string NewCategory { get; set; } = string.Empty;

    [ObservableProperty]
    public partial decimal? NewLimit { get; set; }

    [ObservableProperty]
    public partial string? BaseCurrency { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsDates))]
    public partial BudgetPeriodOption? NewPeriod { get; set; }

    [ObservableProperty]
    public partial AccountScopeOption? NewBudgetAccount { get; set; }

    public IReadOnlyList<AccountScopeOption> BudgetAccountOptions { get; private set; } = Array.Empty<AccountScopeOption>();

    [ObservableProperty]
    public partial DateTimeOffset? NewStartDate { get; set; }

    [ObservableProperty]
    public partial DateTimeOffset? NewLastDate { get; set; }

    public bool NeedsDates => NewPeriod?.Period == BudgetPeriod.Custom;

    [ObservableProperty]
    public partial string CreateMessage { get; set; } = string.Empty;

    public IReadOnlyList<BudgetPeriodOption> PeriodOptions { get; private set; } = Array.Empty<BudgetPeriodOption>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial string? ErrorMessage { get; set; }

    // ─── Recurring Incomes State ───
    public ObservableCollection<RecurringIncomeRowViewModel> RecurringIncomes { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIncomesEmpty))]
    public partial bool IsCreatingIncome { get; set; }

    public bool IsIncomesEmpty => !IsLoading && !IsCreatingIncome && RecurringIncomes.Count == 0;

    [ObservableProperty]
    public partial string NewIncomeName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial decimal? NewIncomeAmount { get; set; }

    [ObservableProperty]
    public partial int NewIncomeDay { get; set; } = 1;

    [ObservableProperty]
    public partial Account? SelectedIncomeAccount { get; set; }

    public ObservableCollection<Account> AvailableAccounts { get; } = new();

    [ObservableProperty]
    public partial FrequencyOption? SelectedIncomeFrequency { get; set; }

    public IReadOnlyList<FrequencyOption> FrequencyOptions { get; private set; } = Array.Empty<FrequencyOption>();

    [ObservableProperty]
    public partial string IncomeMessage { get; set; } = string.Empty;

    // ─── Savings Goals State ───
    public ObservableCollection<SavingsGoalRowViewModel> SavingsGoals { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGoalsEmpty))]
    public partial bool IsCreatingGoal { get; set; }

    public bool IsGoalsEmpty => !IsLoading && !IsCreatingGoal && SavingsGoals.Count == 0;

    [ObservableProperty]
    public partial string NewGoalName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial decimal? NewGoalMonthlyTarget { get; set; }

    [ObservableProperty]
    public partial decimal? NewGoalTargetAmount { get; set; }

    [ObservableProperty]
    public partial decimal? NewGoalInitialAmount { get; set; }

    [ObservableProperty]
    public partial Account? SelectedGoalAccount { get; set; }

    [ObservableProperty]
    public partial string GoalMessage { get; set; } = string.Empty;

    public BudgetsViewModel(
        IMediator mediator,
        ILocalizer? localizer = null,
        ILogger<BudgetsViewModel>? logger = null)
    {
        _mediator = mediator;
        _localizer = localizer ?? Localizer.Instance;
        _logger = logger;
        _error = new LocalizedMessageSlot(_localizer, text => ErrorMessage = text);
        _createMessage = new LocalizedMessageSlot(_localizer, text => CreateMessage = text ?? string.Empty);
        _incomeMessage = new LocalizedMessageSlot(_localizer, text => IncomeMessage = text ?? string.Empty);
        _goalMessage = new LocalizedMessageSlot(_localizer, text => GoalMessage = text ?? string.Empty);
        _autoPostNotice = new LocalizedMessageSlot(_localizer, text => AutoPostNotice = text ?? string.Empty);

        Budgets.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
        RecurringIncomes.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsIncomesEmpty));
        SavingsGoals.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsGoalsEmpty));
        _localizer.LanguageChanged += OnLanguageChanged;

        FrequencyOptions = FrequencyChoices();
        SelectedIncomeFrequency = FrequencyOptions[0];
    }

    partial void OnNewGoalMonthlyTargetChanged(decimal? value)
    {
        if (value is > 0m && (!NewGoalTargetAmount.HasValue || NewGoalTargetAmount <= 0m))
        {
            NewGoalTargetAmount = value.Value * 12m;
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        RebuildLocalizedViews();

        if (PeriodOptions.Count > 0)
        {
            var chosen = NewPeriod?.Period;
            PeriodOptions = PeriodChoices();
            OnPropertyChanged(nameof(PeriodOptions));
            NewPeriod = PeriodOptions.FirstOrDefault(o => o.Period == chosen) ?? PeriodOptions[0];
        }

        var chosenFreq = SelectedIncomeFrequency?.Frequency;
        FrequencyOptions = FrequencyChoices();
        OnPropertyChanged(nameof(FrequencyOptions));
        SelectedIncomeFrequency = FrequencyOptions.FirstOrDefault(f => f.Frequency == chosenFreq) ?? FrequencyOptions[0];
    }

    private void RebuildLocalizedViews()
    {
        Budgets.Clear();
        foreach (var row in _lastProgress)
        {
            Budgets.Add(BudgetProgressViewModel.From(row, _localizer));
        }

        RecurringIncomes.Clear();
        foreach (var item in _lastIncomes)
        {
            RecurringIncomes.Add(RecurringIncomeRowViewModel.From(item, _localizer));
        }

        SavingsGoals.Clear();
        foreach (var goal in _lastGoals)
        {
            SavingsGoals.Add(SavingsGoalRowViewModel.From(goal, _localizer));
        }

        ApplyPlanSummary(_lastPlanSummary);
    }

    private List<BudgetPeriodOption> PeriodChoices() =>
        new[] { BudgetPeriod.Monthly, BudgetPeriod.Weekly, BudgetPeriod.Yearly, BudgetPeriod.Custom }
            .Select(period => new BudgetPeriodOption(period, _localizer[BudgetProgressViewModel.PeriodKey(period)]))
            .ToList();

    private FrequencyOption[] FrequencyChoices() =>
        new[]
        {
            new FrequencyOption(RecurringFrequency.Monthly, _localizer["Planning.Income.Freq.Monthly"]),
            new FrequencyOption(RecurringFrequency.BiWeekly, _localizer["Planning.Income.Freq.BiWeekly"]),
            new FrequencyOption(RecurringFrequency.Weekly, _localizer["Planning.Income.Freq.Weekly"]),
        };

    // ─── Budget Commands ───

    [RelayCommand]
    private async Task BeginCreateAsync()
    {
        _createMessage.Clear();

        string? baseCurrency;
        try
        {
            baseCurrency = await _mediator.Send(new GetBaseCurrencyQuery());
        }
        catch (Exception ex)
        {
            _createMessage.Set(UserFacingError.Report(_logger, ex, "budget-create-accounts", CreateFailed));
            return;
        }

        if (string.IsNullOrEmpty(baseCurrency))
        {
            _createMessage.Set("Budgets.Create.NoAccounts");
            return;
        }

        PeriodOptions = PeriodChoices();
        OnPropertyChanged(nameof(PeriodOptions));

        await RefreshAccountOptionsAsync();

        NewCategory = string.Empty;
        NewLimit = null;
        BaseCurrency = baseCurrency;
        NewPeriod = PeriodOptions[0];
        NewBudgetAccount = BudgetAccountOptions.Count > 0 ? BudgetAccountOptions[0] : null;
        NewStartDate = null;
        NewLastDate = null;
        IsCreating = true;
    }

    [RelayCommand]
    private void CancelCreate()
    {
        _createMessage.Clear();
        IsCreating = false;
    }

    [RelayCommand]
    private async Task SaveBudgetAsync()
    {
        if (IsSaving)
        {
            return;
        }

        IsSaving = true;
        _createMessage.Clear();

        try
        {
            var period = NewPeriod?.Period ?? BudgetPeriod.Monthly;
            var custom = period == BudgetPeriod.Custom;

            await _mediator.Send(new CreateBudgetCommand(
                NewCategory,
                NewLimit ?? 0m,
                period,
                custom ? NewStartDate?.Date is { } start ? new DateTimeOffset(start) : null : null,
                custom ? NewLastDate?.Date is { } last ? new DateTimeOffset(last.AddDays(1)) : null : null,
                NewBudgetAccount?.AccountId));

            WeakReferenceMessenger.Default.Send(new DataChangedMessage());
        }
        catch (Exception ex)
        {
            _createMessage.Set(UserFacingError.Report(_logger, ex, "budget-create", CreateFailed));
            return;
        }
        finally
        {
            IsSaving = false;
        }

        IsCreating = false;
        try
        {
            await LoadBudgetsAsync();
        }
        catch (Exception ex)
        {
            _error.Set(UserFacingError.Report(_logger, ex, "budgets-reload-after-create", BudgetsUnavailable));
        }
    }

    [RelayCommand]
    private async Task DeleteBudgetAsync(Guid budgetId)
    {
        try
        {
            await _mediator.Send(new DeleteBudgetCommand(budgetId));
            WeakReferenceMessenger.Default.Send(new DataChangedMessage());
            await LoadBudgetsAsync();
        }
        catch (Exception ex)
        {
            _error.Set(UserFacingError.Report(_logger, ex, "budget-delete", BudgetsUnavailable));
        }
    }

    // ─── Recurring Income Commands ───

    [RelayCommand]
    private async Task BeginCreateIncomeAsync()
    {
        _incomeMessage.Clear();
        await RefreshAccountOptionsAsync();

        if (AvailableAccounts.Count == 0)
        {
            _incomeMessage.Set("Budgets.Create.NoAccounts");
            return;
        }

        NewIncomeName = string.Empty;
        NewIncomeAmount = null;
        NewIncomeDay = 1;
        SelectedIncomeAccount = AvailableAccounts[0];
        SelectedIncomeFrequency = FrequencyOptions[0];
        IsCreatingIncome = true;
    }

    [RelayCommand]
    private void CancelCreateIncome()
    {
        _incomeMessage.Clear();
        IsCreatingIncome = false;
    }

    [RelayCommand]
    private async Task SaveRecurringIncomeAsync()
    {
        if (IsSaving)
        {
            return;
        }

        IsSaving = true;
        _incomeMessage.Clear();

        try
        {
            await _mediator.Send(new CreateRecurringIncomeCommand(
                NewIncomeName,
                NewIncomeAmount ?? 0m,
                SelectedIncomeAccount?.Id ?? Guid.Empty,
                NewIncomeDay,
                SelectedIncomeFrequency?.Frequency ?? RecurringFrequency.Monthly));

            WeakReferenceMessenger.Default.Send(new DataChangedMessage());
        }
        catch (Exception ex)
        {
            _incomeMessage.Set(UserFacingError.Report(_logger, ex, "recurring-income-create", IncomeFailed));
            return;
        }
        finally
        {
            IsSaving = false;
        }

        IsCreatingIncome = false;
        await ReloadPlanningSafeAsync();
    }

    [RelayCommand]
    private async Task PostRecurringIncomeNowAsync(Guid id)
    {
        try
        {
            await _mediator.Send(new PostRecurringIncomeNowCommand(id));
            WeakReferenceMessenger.Default.Send(new DataChangedMessage());
            await ReloadPlanningSafeAsync();
        }
        catch (Exception ex)
        {
            _incomeMessage.Set(UserFacingError.Report(_logger, ex, "recurring-income-post", IncomeFailed));
        }
    }

    [RelayCommand]
    private async Task ToggleRecurringIncomeAsync(RecurringIncomeRowViewModel row)
    {
        if (row is null)
        {
            return;
        }

        try
        {
            await _mediator.Send(new ToggleRecurringIncomeCommand(row.Id, !row.IsActive));
            WeakReferenceMessenger.Default.Send(new DataChangedMessage());
            await ReloadPlanningSafeAsync();
        }
        catch (Exception ex)
        {
            _incomeMessage.Set(UserFacingError.Report(_logger, ex, "recurring-income-toggle", IncomeFailed));
        }
    }

    [RelayCommand]
    private async Task DeleteRecurringIncomeAsync(Guid id)
    {
        try
        {
            await _mediator.Send(new DeleteRecurringIncomeCommand(id));
            WeakReferenceMessenger.Default.Send(new DataChangedMessage());
            await ReloadPlanningSafeAsync();
        }
        catch (Exception ex)
        {
            _incomeMessage.Set(UserFacingError.Report(_logger, ex, "recurring-income-delete", IncomeFailed));
        }
    }

    // ─── Savings Goal Commands ───

    [RelayCommand]
    private async Task BeginCreateGoalAsync()
    {
        _goalMessage.Clear();

        string? baseCurrency;
        try
        {
            baseCurrency = await _mediator.Send(new GetBaseCurrencyQuery());
        }
        catch (Exception ex)
        {
            _goalMessage.Set(UserFacingError.Report(_logger, ex, "savings-goal-base-currency", GoalFailed));
            return;
        }

        if (string.IsNullOrEmpty(baseCurrency))
        {
            _goalMessage.Set("Budgets.Create.NoAccounts");
            return;
        }

        BaseCurrency = baseCurrency;
        await RefreshAccountOptionsAsync();
        if (AvailableAccounts.Count == 0)
        {
            _goalMessage.Set("Budgets.Create.NoAccounts");
            return;
        }

        NewGoalName = string.Empty;
        NewGoalMonthlyTarget = null;
        NewGoalTargetAmount = null;
        NewGoalInitialAmount = 0m;
        SelectedGoalAccount = AvailableAccounts.FirstOrDefault();
        IsCreatingGoal = true;
    }

    [RelayCommand]
    private void CancelCreateGoal()
    {
        _goalMessage.Clear();
        IsCreatingGoal = false;
    }

    [RelayCommand]
    private async Task SaveGoalAsync()
    {
        if (IsSaving)
        {
            return;
        }

        if (SelectedGoalAccount is null)
        {
            _goalMessage.Set("Planning.Goals.AccountRequired");
            return;
        }

        IsSaving = true;
        _goalMessage.Clear();

        try
        {
            var monthly = NewGoalMonthlyTarget ?? 0m;
            var target = NewGoalTargetAmount ?? (monthly > 0m ? monthly * 12m : 0m);

            await _mediator.Send(new CreateSavingsGoalCommand(
                NewGoalName,
                target,
                monthly,
                NewGoalInitialAmount ?? 0m,
                LinkedAccountId: SelectedGoalAccount.Id));

            WeakReferenceMessenger.Default.Send(new DataChangedMessage());
        }
        catch (Exception ex)
        {
            _goalMessage.Set(UserFacingError.Report(_logger, ex, "savings-goal-create", GoalFailed));
            return;
        }
        finally
        {
            IsSaving = false;
        }

        IsCreatingGoal = false;
        await ReloadPlanningSafeAsync();
    }

    [RelayCommand]
    private async Task QuickContributeGoalAsync(SavingsGoalRowViewModel row)
    {
        if (row is null || row.QuickAddAmount <= 0m)
        {
            return;
        }

        try
        {
            await _mediator.Send(new ContributeSavingsGoalCommand(row.Id, row.QuickAddAmount));
            WeakReferenceMessenger.Default.Send(new DataChangedMessage());
            await ReloadPlanningSafeAsync();
        }
        catch (Exception ex)
        {
            _goalMessage.Set(UserFacingError.Report(_logger, ex, "savings-goal-contribute", GoalFailed));
        }
    }

    [RelayCommand]
    private void BeginCustomContributeGoal(SavingsGoalRowViewModel row)
    {
        if (row is null)
        {
            return;
        }

        _goalMessage.Clear();
        row.CustomContributeAmount = null;
        row.IsEnteringCustomAmount = !row.IsEnteringCustomAmount;
    }

    [RelayCommand]
    private void CancelCustomContributeGoal(SavingsGoalRowViewModel row)
    {
        if (row is null)
        {
            return;
        }

        _goalMessage.Clear();
        row.CustomContributeAmount = null;
        row.IsEnteringCustomAmount = false;
    }

    [RelayCommand]
    private async Task SubmitCustomContributeGoalAsync(SavingsGoalRowViewModel row)
    {
        if (row is null)
        {
            return;
        }

        var amount = row.CustomContributeAmount ?? 0m;
        if (amount <= 0m)
        {
            _goalMessage.Set("Validation.SavingsGoal.ContributionNotPositive");
            return;
        }

        _goalMessage.Clear();
        try
        {
            await _mediator.Send(new ContributeSavingsGoalCommand(row.Id, amount));
            row.IsEnteringCustomAmount = false;
            row.CustomContributeAmount = null;
            WeakReferenceMessenger.Default.Send(new DataChangedMessage());
            await ReloadPlanningSafeAsync();
        }
        catch (Exception ex)
        {
            _goalMessage.Set(UserFacingError.Report(_logger, ex, "savings-goal-contribute-custom", GoalFailed));
        }
    }

    [RelayCommand]
    private async Task DeleteGoalAsync(Guid id)
    {
        try
        {
            await _mediator.Send(new DeleteSavingsGoalCommand(id));
            WeakReferenceMessenger.Default.Send(new DataChangedMessage());
            await ReloadPlanningSafeAsync();
        }
        catch (Exception ex)
        {
            _goalMessage.Set(UserFacingError.Report(_logger, ex, "savings-goal-delete", GoalFailed));
        }
    }

    private static readonly UserFacingMessage CreateFailed = new("Budgets.Create.Failure");
    private static readonly UserFacingMessage IncomeFailed = new("Planning.Income.Failure");
    private static readonly UserFacingMessage GoalFailed = new("Planning.Goals.Failure");
    private static readonly UserFacingMessage BudgetsUnavailable = new("Budgets.Failure.Load");

    public void Receive(DataChangedMessage message)
    {
        ObservedTask.Run(
            LoadBudgetsAsync,
            _logger,
            "budgets-reload",
            BudgetsUnavailable,
            _error.Set);
    }

    public InitializationFailurePolicy InitializationFailurePolicy => new(
        _logger,
        "budgets-load",
        BudgetsUnavailable,
        _error.Set);

    public void Dispose()
    {
        _localizer.LanguageChanged -= OnLanguageChanged;
        _error.Dispose();
        _createMessage.Dispose();
        _incomeMessage.Dispose();
        _goalMessage.Dispose();
        _autoPostNotice.Dispose();
        GC.SuppressFinalize(this);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        try
        {
            var posted = await _mediator.Send(new ProcessDueRecurringIncomesCommand(), cancellationToken);
            if (posted > 0)
            {
                _autoPostNotice.Set("Planning.Income.AutoPostedNotice", posted);
                WeakReferenceMessenger.Default.Send(new DataChangedMessage());
            }
        }
        catch (Exception)
        {
            // Non-fatal: e.g. unit test mocks or empty vaults without recurring incomes.
        }

        await LoadBudgetsAsync();
    }

    [RelayCommand]
    private async Task LoadBudgetsAsync()
    {
        var generation = Interlocked.Increment(ref _loadGeneration);
        IsLoading = true;

        try
        {
            var progress = await _mediator.Send(new GetBudgetProgressQuery()) ?? Array.Empty<BudgetProgress>();

            IReadOnlyList<RecurringIncomeSummary>? incomes = null;
            IReadOnlyList<SavingsGoalSummary>? goals = null;
            MonthlyPlanSummary? planSummary = null;
            var planningLoaded = false;

            try
            {
                incomes = await _mediator.Send(new GetRecurringIncomesQuery());
                goals = await _mediator.Send(new GetSavingsGoalsQuery());
                planSummary = await _mediator.Send(new GetMonthlyPlanSummaryQuery());
                planningLoaded = true;
            }
            catch (Exception)
            {
                // Keep Budgets screen functional even when optional planning queries are unmocked in isolated tests.
            }

            // Drop stale results from an earlier overlapping load so lists never duplicate or revert.
            if (generation != Volatile.Read(ref _loadGeneration))
            {
                return;
            }

            _lastProgress = progress;
            Budgets.Clear();
            foreach (var row in progress)
            {
                Budgets.Add(BudgetProgressViewModel.From(row, _localizer));
            }

            if (planningLoaded)
            {
                _lastIncomes = incomes ?? Array.Empty<RecurringIncomeSummary>();
                RecurringIncomes.Clear();
                foreach (var item in _lastIncomes)
                {
                    RecurringIncomes.Add(RecurringIncomeRowViewModel.From(item, _localizer));
                }

                _lastGoals = goals ?? Array.Empty<SavingsGoalSummary>();
                SavingsGoals.Clear();
                foreach (var goal in _lastGoals)
                {
                    SavingsGoals.Add(SavingsGoalRowViewModel.From(goal, _localizer));
                }

                _lastPlanSummary = planSummary;
                ApplyPlanSummary(planSummary);
            }
        }
        finally
        {
            if (generation == Volatile.Read(ref _loadGeneration))
            {
                IsLoading = false;
            }
        }
    }

    private async Task ReloadPlanningSafeAsync()
    {
        try
        {
            await LoadBudgetsAsync();
        }
        catch (Exception ex)
        {
            _error.Set(UserFacingError.Report(_logger, ex, "planning-reload", BudgetsUnavailable));
        }
    }

    private async Task RefreshAccountOptionsAsync()
    {
        try
        {
            var accounts = await _mediator.Send(new GetAccountsQuery()) ?? Array.Empty<Account>();
            AvailableAccounts.Clear();
            foreach (var acct in accounts)
            {
                AvailableAccounts.Add(acct);
            }

            var options = new List<AccountScopeOption>
            {
                new(null, _localizer["Budgets.Create.AllAccounts"]),
            };
            options.AddRange(accounts
                .Where(a => string.IsNullOrEmpty(BaseCurrency) || string.Equals(a.Balance.Currency.Value, BaseCurrency, StringComparison.OrdinalIgnoreCase))
                .Select(a => new AccountScopeOption(a.Id, $"{a.Name} ({a.Balance.Currency.Value})")));

            BudgetAccountOptions = options;
            OnPropertyChanged(nameof(BudgetAccountOptions));
        }
        catch (Exception)
        {
            BudgetAccountOptions = new[] { new AccountScopeOption(null, _localizer["Budgets.Create.AllAccounts"]) };
            OnPropertyChanged(nameof(BudgetAccountOptions));
        }
    }

    private void ApplyPlanSummary(MonthlyPlanSummary? summary)
    {
        if (summary is null || summary.BaseCurrency is null)
        {
            HasRecurringIncomePlan = false;
            IsPlanOverAllocated = false;
            PlanAllocationSegments = Array.Empty<ChartSegment>();
            PlanOverAllocatedWarning = string.Empty;
            return;
        }

        var currency = summary.BaseCurrency;
        HasRecurringIncomePlan = summary.HasRecurringIncome;
        IsPlanOverAllocated = summary.IsOverAllocated;
        PlanMonthlyIncomeText = Money.FromRounded(summary.MonthlyRecurringIncome, currency).ToString();
        PlanBudgetsAllocatedText = Money.FromRounded(summary.MonthlyBudgetsAllocated, currency).ToString();
        PlanSavingsAllocatedText = Money.FromRounded(summary.MonthlySavingsAllocated, currency).ToString();
        // Keep the real negative sign when over-allocated so a deficit is immediately visible.
        PlanUnallocatedText = Money.FromRounded(summary.UnallocatedMonthlyIncome, currency).ToString();

        PlanOverAllocatedWarning = summary.IsOverAllocated
            ? _localizer.Format("Budgets.Plan.OverAllocated", Money.FromRounded(-summary.UnallocatedMonthlyIncome, currency).ToString())
            : string.Empty;

        var segments = new List<ChartSegment>();
        if (summary.MonthlyBudgetsAllocated > 0m)
        {
            segments.Add(new ChartSegment((double)summary.MonthlyBudgetsAllocated, "#F57104", _localizer["Budgets.Plan.AllocatedBudgets"]));
        }

        if (summary.MonthlySavingsAllocated > 0m)
        {
            segments.Add(new ChartSegment((double)summary.MonthlySavingsAllocated, "#10B981", _localizer["Budgets.Plan.AllocatedSavings"]));
        }

        if (summary.UnallocatedMonthlyIncome > 0m)
        {
            segments.Add(new ChartSegment((double)summary.UnallocatedMonthlyIncome, "#3B82F6", _localizer["Budgets.Plan.Unallocated"]));
        }

        PlanAllocationSegments = segments;
    }
}

public sealed record BudgetPeriodOption(BudgetPeriod Period, string Label)
{
    public override string ToString() => Label;
}
