using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MediatR;
using Pine.Application.Budgets.Commands;
using Pine.Application.Budgets.Queries;
using Pine.Core.Budgets;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Categories;
using Pine.Core.Common;
using Pine.Core.Transactions;
using Pine.Ui.Avalonia.Services;
using Pine.Ui.Avalonia.ViewModels.Messages;
using Pine.Ui.Avalonia.ViewModels.Widgets;

namespace Pine.Ui.Avalonia.ViewModels;

public partial class BudgetProgressViewModel : ObservableObject
{
    [ObservableProperty]
    public partial Guid BudgetId { get; set; }

    [ObservableProperty]
    public partial string CategoryName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CategoryColorHex { get; set; } = "#FFFFFF";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LimitAmountText))]
    [NotifyPropertyChangedFor(nameof(ProgressPercentage))]
    [NotifyPropertyChangedFor(nameof(IsExceeded))]
    public partial decimal LimitAmount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SpentAmountText))]
    [NotifyPropertyChangedFor(nameof(ProgressPercentage))]
    [NotifyPropertyChangedFor(nameof(IsExceeded))]
    public partial decimal SpentAmount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SpentAmountText))]
    [NotifyPropertyChangedFor(nameof(LimitAmountText))]
    public partial string CurrencyCode { get; set; } = string.Empty;

    public double ProgressPercentage => LimitAmount > 0 ? (double)(SpentAmount / LimitAmount) * 100 : 0;

    public bool IsExceeded => SpentAmount > LimitAmount;

    /// <summary>The spent figure, rendered at its currency's scale (TD-75).</summary>
    public string SpentAmountText => FormatAtCurrencyScale(SpentAmount);

    /// <summary>The limit figure, rendered at its currency's scale (TD-75).</summary>
    public string LimitAmountText => FormatAtCurrencyScale(LimitAmount);

    /// <summary>
    /// Renders one of this row's amounts the way the domain says its currency should be written.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The view used to do this with a hardcoded <c>{0:N2}</c>, which is two decimals for every
    /// currency including the ones that do not have two. This row holds a bare <see cref="decimal"/>
    /// and a currency code rather than a <see cref="Money"/>, so unlike the widgets it cannot simply
    /// bind through the shared converter — the pair has to be rejoined into a <c>Money</c> first, and
    /// this is the one place that knows both halves.
    /// </para>
    /// <para>
    /// <see cref="CurrencyCode.FromStoredValue"/> and <see cref="Money.FromRounded"/> are the
    /// non-throwing pair on purpose. This is a read path feeding a screen: a budget denominated in a
    /// code that is no longer in the ISO table (TD-63 lets one be created) must render as something
    /// rather than take the budgets page down, which is the same argument
    /// <c>Account.OpeningBalance</c> settled under TD-77.
    /// </para>
    /// </remarks>
    private string FormatAtCurrencyScale(decimal amount)
    {
        if (string.IsNullOrWhiteSpace(CurrencyCode))
        {
            return amount.ToString("N2", CultureInfo.CurrentCulture);
        }

        var currency = Pine.Core.Common.ValueObjects.CurrencyCode.FromStoredValue(CurrencyCode);
        return Money.FromRounded(amount, currency).ToAmountString();
    }
}

public partial class BudgetsViewModel : ObservableObject, IDashboardWidgetViewModel, IRecipient<DataChangedMessage>
{
    private readonly IMediator _mediator;
    private readonly INavigationService _navigationService;

    public string Title { get; set; } = "Budgets";
    public int ColumnSpan { get; set; } = 2;
    public int RowSpan { get; set; } = 1;
    public int OrderIndex { get; set; } = 3;
    public double Width { get; set; } = 400;
    public double Height { get; set; } = 300;

    [ObservableProperty]
    public partial ObservableCollection<BudgetProgressViewModel> Budgets { get; set; } = new();

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    public BudgetsViewModel(IMediator mediator, INavigationService navigationService)
    {
        _mediator = mediator;
        _navigationService = navigationService;
    }

    public void Receive(DataChangedMessage message)
    {
        _ = LoadBudgetsAsync();
    }

    public async Task InitializeAsync()
    {
        await LoadBudgetsAsync();
    }

    [RelayCommand]
    private async Task LoadBudgetsAsync()
    {
        IsLoading = true;
        Budgets.Clear();

        try
        {
            var budgets = await _mediator.Send(new GetBudgetsQuery());
            
            // To calculate progress, we need categories and transactions for the current month
            // For brevity in MVP, we would inject a query to get transactions.
            // Using dummy progress for visual setup, later wired to GetTransactionsQuery
            
            foreach (var b in budgets)
            {
                Budgets.Add(new BudgetProgressViewModel
                {
                    BudgetId = b.Id,
                    CategoryName = "Category " + b.CategoryId.ToString()[..4], // Requires Category lookup
                    CategoryColorHex = "#FF8C00", // Pine Orange
                    LimitAmount = b.Limit.Amount,
                    SpentAmount = 0m, // Will be calculated from transactions
                    CurrencyCode = b.Limit.Currency.Value
                });
            }
        }
        finally
        {
            IsLoading = false;
        }
    }
}
