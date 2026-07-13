using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediatR;
using Pine.Application.Budgets.Commands;
using Pine.Application.Budgets.Queries;
using Pine.Core.Budgets;
using Pine.Core.Categories;
using Pine.Core.Common;
using Pine.Core.Transactions;
using Pine.Ui.Avalonia.Services;

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
    public partial decimal LimitAmount { get; set; }

    [ObservableProperty]
    public partial decimal SpentAmount { get; set; }

    [ObservableProperty]
    public partial string CurrencyCode { get; set; } = string.Empty;

    public double ProgressPercentage => LimitAmount > 0 ? (double)(SpentAmount / LimitAmount) * 100 : 0;
    
    public bool IsExceeded => SpentAmount > LimitAmount;
}

public partial class BudgetsViewModel : ObservableObject, IDashboardWidgetViewModel
{
    private readonly IMediator _mediator;
    private readonly INavigationService _navigationService;

    [ObservableProperty]
    public partial ObservableCollection<BudgetProgressViewModel> Budgets { get; set; } = new();

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    public int RowSpan => 2;
    public int ColumnSpan => 2;
    public int Order => 3;

    public BudgetsViewModel(IMediator mediator, INavigationService navigationService)
    {
        _mediator = mediator;
        _navigationService = navigationService;
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
                    CategoryName = "Category " + b.CategoryId.ToString().Substring(0, 4), // Requires Category lookup
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
