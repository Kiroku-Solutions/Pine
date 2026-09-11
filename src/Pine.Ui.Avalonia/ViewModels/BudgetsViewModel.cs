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
using Pine.Core.Common.ValueObjects;
using Pine.Core.Common;
using Pine.Ui.Avalonia.Localization;
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

    /// <summary>
    /// Whether a real spend figure was measured for this budget.
    /// </summary>
    /// <remarks>
    /// <para>
    /// FALSE MEANS "NOT KNOWN", NOT "ZERO" (TD-99). <c>GetBudgetProgressQuery</c> returns a null
    /// spend for a budget whose category or period cannot be resolved, and this row must carry that
    /// distinction all the way to the screen: rendering an unknown spend as 0 is precisely the
    /// fabrication this row exists to remove — it is a bar reading "0% of your limit used", which
    /// is a factual claim about the user's money that nothing checked.
    /// </para>
    /// <para>
    /// It defaults to <see langword="true"/> so that a row built from a measured figure needs no
    /// extra ceremony, and so the formatting tests that construct a bare row keep meaning what they
    /// meant.
    /// </para>
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SpentAmountText))]
    [NotifyPropertyChangedFor(nameof(ProgressPercentage))]
    [NotifyPropertyChangedFor(nameof(IsExceeded))]
    public partial bool IsSpendKnown { get; set; } = true;

    /// <summary>
    /// What stands in for the spend when it is not known. Supplied by the page from the localizer,
    /// because this row is constructed by hand and must not reach into a static one.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SpentAmountText))]
    public partial string UnknownSpendText { get; set; } = "—";

    public double ProgressPercentage =>
        IsSpendKnown && LimitAmount > 0 ? (double)(SpentAmount / LimitAmount) * 100 : 0;

    public bool IsExceeded => IsSpendKnown && SpentAmount > LimitAmount;

    /// <summary>The spent figure, rendered at its currency's scale (TD-75), or said to be unknown.</summary>
    public string SpentAmountText => IsSpendKnown ? FormatAtCurrencyScale(SpentAmount) : UnknownSpendText;

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
/// <para>
/// AND FOR THOSE SAME MONTHS THE NUMBERS ON IT WERE INVENTED (TD-99). This page set
/// <c>SpentAmount = 0m</c> for every budget behind a comment reading "Will be calculated from
/// transactions", and named each row <c>"Category " + b.CategoryId.ToString()[..4]</c> — four hex
/// digits of a GUID, presented where a category name goes. So every bar read 0%,
/// <c>IsExceeded</c> was never true and the over-budget warning in <c>BudgetsView.axaml</c> was
/// markup no user could ever see. The figures now come from <see cref="GetBudgetProgressQuery"/>,
/// which measures them against the ledger; this page only renders what it is handed, including the
/// case where the answer is "not known".
/// </para>
/// </remarks>
public partial class BudgetsViewModel : ObservableRecipient, IDashboardWidgetViewModel, IRecipient<DataChangedMessage>, IAsyncInitializable
{
    private readonly IMediator _mediator;
    private readonly ILocalizer _localizer;
    private readonly ILogger<BudgetsViewModel>? _logger;

    /// <summary>
    /// The swatch a row falls back to when its category could not be resolved and so has no colour
    /// of its own. Pine orange -- a colour, not a claim about the category.
    /// </summary>
    private const string UnresolvedCategorySwatch = "#FF8C00";

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

    /// <param name="mediator">The request pipeline.</param>
    /// <param name="localizer">
    /// The string table. Optional, and defaulting to <see cref="Localizer.Instance"/> -- the same
    /// instance the container registers -- so that a test constructing this page by hand still gets
    /// real English rather than a key, and so the container keeps resolving it as before.
    /// </param>
    /// <param name="logger">Diagnostics.</param>
    /// <remarks>
    /// NO NAVIGATOR (TD-121). This page took <see cref="INavigationService"/> and never called it.
    /// That was harmless only because it was unused: the container supplied the WINDOW's navigator,
    /// so the first navigation anyone added here would have replaced the shell. A dependency held
    /// for no reason is a defect waiting for its first caller, so it is gone rather than retyped.
    /// </remarks>
    public BudgetsViewModel(
        IMediator mediator,
        ILocalizer? localizer = null,
        ILogger<BudgetsViewModel>? logger = null)
    {
        _mediator = mediator;
        _localizer = localizer ?? Localizer.Instance;
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
            var unknownSpend = _localizer["Budgets.Progress.SpendUnknown"];
            var unresolvedCategory = _localizer["Budgets.Category.Unresolved"];

            var progress = await _mediator.Send(new GetBudgetProgressQuery());

            foreach (var row in progress)
            {
                Budgets.Add(new BudgetProgressViewModel
                {
                    BudgetId = row.BudgetId,
                    CategoryName = row.CategoryName ?? unresolvedCategory,
                    CategoryColorHex = row.CategoryColorHex ?? UnresolvedCategorySwatch,
                    LimitAmount = row.Limit.Amount,
                    CurrencyCode = row.Limit.Currency.Value,
                    IsSpendKnown = row.IsSpendKnown,
                    SpentAmount = row.Spent?.Amount ?? 0m,
                    UnknownSpendText = unknownSpend,
                });
            }
        }
        finally
        {
            IsLoading = false;
        }
    }
}
