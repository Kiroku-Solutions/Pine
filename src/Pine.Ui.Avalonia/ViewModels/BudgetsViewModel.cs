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
using Pine.Application.Budgets.Commands;
using Pine.Application.Budgets.Queries;
using Pine.Core.Budgets;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Categories;
using Pine.Core.Common;
using Pine.Core.Transactions;
using Pine.Ui.Avalonia.Services;
using Pine.Ui.Avalonia.Diagnostics;
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

/// <summary>
/// The budgets page.
/// </summary>
/// <remarks>
/// IT DECLARED <c>IRecipient&lt;DataChangedMessage&gt;</c> FOR MONTHS WITHOUT EVER RECEIVING ONE
/// (TD-34). Nothing registered it -- there was no manual messenger registration
/// call anywhere for this type -- so <see cref="Receive"/> was unreachable code and adding a
/// transaction never refreshed the budget bars. Deriving from <see cref="ObservableRecipient"/> is
/// the fix rather than deleting the interface, because the interface described the intended
/// behaviour correctly; it was the wiring that was missing. Activation now registers it like every
/// other page.
/// </remarks>
public partial class BudgetsViewModel : ObservableRecipient, IDashboardWidgetViewModel, IRecipient<DataChangedMessage>, IAsyncInitializable
{
    private readonly IMediator _mediator;
    private readonly INavigationService _navigationService;
    private readonly ILogger<BudgetsViewModel>? _logger;

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

    public BudgetsViewModel(
        IMediator mediator,
        INavigationService navigationService,
        ILogger<BudgetsViewModel>? logger = null)
    {
        _mediator = mediator;
        _navigationService = navigationService;
        _logger = logger;
    }

    /// <summary>Reloads the budgets when something elsewhere changed the data.</summary>
    /// <remarks>
    /// Observed rather than discarded (TD-35). Until TD-34 this method was unreachable -- the class
    /// declared <c>IRecipient&lt;DataChangedMessage&gt;</c> and nothing ever registered it -- so its
    /// discarded task had never actually run.
    /// </remarks>
    /// <param name="message">The broadcast. It carries no payload.</param>
    public void Receive(DataChangedMessage message)
    {
        ObservedTask.Run(
            LoadBudgetsAsync,
            _logger,
            "budgets-reload",
            BudgetsUnavailable,
            reported => ErrorMessage = reported);
    }

    /// <inheritdoc />
    public InitializationFailurePolicy InitializationFailurePolicy => new(
        _logger,
        "budgets-load",
        BudgetsUnavailable,
        message => ErrorMessage = message);

    /// <summary>What the user reads when the budget list cannot be read.</summary>
    /// <remarks>
    /// <c>LoadBudgetsAsync</c> has a <c>try/finally</c> and no <c>catch</c>, and it clears
    /// <c>Budgets</c> BEFORE its await -- so a failure used to leave an empty list with
    /// <c>IsLoading</c> correctly false, which reads as "you have no budgets" (TD-35).
    /// </remarks>
    private const string BudgetsUnavailable =
        "Your budgets could not be loaded, so this page is not showing them. Try again.";

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken)
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
