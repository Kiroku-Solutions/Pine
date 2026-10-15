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
using Pine.Core.Budgets;
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
    /// <summary>
    /// Builds a row from one measured budget, the one way both the budgets page and the dashboard's
    /// budgets tile do it (post-audit plan item 1.3).
    /// </summary>
    /// <remarks>
    /// Extracted rather than copied when the dashboard gained a budgets tile: the page and the tile
    /// must agree about what an unknown spend and an unresolved category look like, and two mappings
    /// written apart are two mappings that drift apart. TD-99's rule -- an unknown spend is "Not
    /// known", never a zero -- lives here once.
    /// </remarks>
    /// <param name="row">The measured budget.</param>
    /// <param name="localizer">The string table the "not known" and "unknown category" text come from.</param>
    /// <returns>The row.</returns>
    public static BudgetProgressViewModel From(BudgetProgress row, ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(localizer);

        return new BudgetProgressViewModel
        {
            BudgetId = row.BudgetId,

            // NO FALLBACK, BECAUSE THERE IS NOTHING LEFT TO FALL BACK FROM (TD-132, plan item 2.3).
            // This used to read `row.CategoryName ?? localizer["Budgets.Category.Unresolved"]`, and
            // "Unknown category" was what EVERY budget rendered as: the name came from a
            // `Categories` row and nothing in the shipping application ever wrote one. The budget
            // now carries the label itself, and the aggregate refuses a budget without one.
            //
            // THE COLOUR SWATCH IS GONE WITH IT. The hex came from `Category.ColorHex`, an attribute
            // of the deleted aggregate. Deriving one from the label — hashing it into a palette —
            // would put a colour on the screen that the user never chose and that would change if
            // they renamed the category, which is the shape of invention this audit exists to
            // remove. When category management is built, the swatch comes back with a real value
            // behind it.
            CategoryName = row.Category,
            PeriodText = localizer[PeriodKey(row.Period)],
            LimitAmount = row.Limit.Amount,
            CurrencyCode = row.Limit.Currency.Value,
            IsSpendKnown = row.IsSpendKnown,
            SpentAmount = row.Spent?.Amount ?? 0m,
            UnknownSpendText = localizer["Budgets.Progress.SpendUnknown"],
        };
    }

    /// <summary>The string-table key naming a period on screen.</summary>
    /// <remarks>
    /// A switch rather than <c>"Budgets.Period." + period</c>, so every key is a literal the catalog test
    /// can find, and a new enum member fails to compile here instead of rendering as a raw key.
    /// </remarks>
    /// <param name="period">The period.</param>
    /// <returns>The key.</returns>
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

    /// <summary>
    /// What the budget is measured over — weekly, monthly — in the user's language (TD-152).
    /// </summary>
    /// <remarks>
    /// The row never said. That was survivable while nothing could create a budget; with a form that
    /// offers four periods, two rows for the same category would otherwise be indistinguishable, and
    /// the over-limit warning used to say "monthly" whatever the period was.
    /// </remarks>
    [ObservableProperty]
    public partial string PeriodText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CategoryName { get; set; } = string.Empty;

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
        return Money.FromRounded(amount, currency).ToAmountString(CultureInfo.CurrentCulture);
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
public partial class BudgetsViewModel : ObservableRecipient, IDashboardWidgetViewModel, IRecipient<DataChangedMessage>, IAsyncInitializable, IDisposable
{
    private readonly LocalizedMessageSlot _error;

    private readonly IMediator _mediator;
    private readonly ILocalizer _localizer;
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
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool IsLoading { get; set; }

    /// <summary>
    /// True when the list loaded and holds nothing — the state every vault was in until this page could
    /// create a budget (TD-152). The page said nothing at all in that state.
    /// </summary>
    public bool IsEmpty => !IsLoading && !IsCreating && Budgets.Count == 0 && string.IsNullOrEmpty(ErrorMessage);

    /// <summary>Whether the new-budget form is open.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool IsCreating { get; set; }

    /// <summary>Whether a budget is being saved, which disables the form.</summary>
    [ObservableProperty]
    public partial bool IsSaving { get; set; }

    /// <summary>The category the new budget measures: the same free label a transaction's split carries.</summary>
    [ObservableProperty]
    public partial string NewCategory { get; set; } = string.Empty;

    /// <summary>The new budget's limit, in <see cref="BaseCurrency"/>; null while the box is empty.</summary>
    /// <remarks>
    /// NULLABLE ON PURPOSE (independent review of item 3.0). Bound as a plain <see cref="decimal"/>, clearing
    /// the box pushed null into a property that cannot hold it, the binding kept the last number, and Save
    /// sent a limit the screen was no longer showing. An empty box is now sent as zero, which the validator
    /// refuses with a sentence.
    /// </remarks>
    [ObservableProperty]
    public partial decimal? NewLimit { get; set; }

    /// <summary>
    /// The currency the new budget is measured in: the vault's base currency, shown and never chosen
    /// (TD-63).
    /// </summary>
    [ObservableProperty]
    public partial string? BaseCurrency { get; set; }

    /// <summary>The period the new budget is measured over.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsDates))]
    public partial BudgetPeriodOption? NewPeriod { get; set; }

    /// <summary>First day of a custom budget.</summary>
    [ObservableProperty]
    public partial DateTimeOffset? NewStartDate { get; set; }

    /// <summary>Last day of a custom budget, inclusive as the user picks it.</summary>
    [ObservableProperty]
    public partial DateTimeOffset? NewLastDate { get; set; }

    /// <summary>Whether the chosen period needs the two date pickers.</summary>
    public bool NeedsDates => NewPeriod?.Period == BudgetPeriod.Custom;

    /// <summary>What went wrong creating a budget, in the user's language, or empty.</summary>
    [ObservableProperty]
    public partial string CreateMessage { get; set; } = string.Empty;

    /// <summary>
    /// The periods the form offers: weekly, monthly, yearly, and custom dates.
    /// </summary>
    /// <remarks>
    /// BI-WEEKLY IS NOT OFFERED. A fortnight has no calendar anchor, so a bi-weekly budget without dates
    /// can never resolve its window and would render "not known" for ever (<c>Budget.ResolveWindow</c>),
    /// and one with dates is exactly what "custom dates" already creates. The validator refuses the
    /// dateless form too, for any caller that is not this page.
    /// </remarks>
    public IReadOnlyList<BudgetPeriodOption> PeriodOptions { get; private set; } = Array.Empty<BudgetPeriodOption>();

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
        _error = new LocalizedMessageSlot(_localizer, text => ErrorMessage = text);
        _createMessage = new LocalizedMessageSlot(_localizer, text => CreateMessage = text ?? string.Empty);
        Budgets.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
        _localizer.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>What the rows were built from, so a language change can build them again.</summary>
    private IReadOnlyList<BudgetProgress> _lastProgress = Array.Empty<BudgetProgress>();

    /// <summary>
    /// Re-words the rows and the period picker when the language changes (independent review of item 3.0).
    /// </summary>
    /// <remarks>
    /// Both were worded when built: a row's period and its "not known", and the picker's labels. The
    /// dashboard's budgets tile already rebuilt its rows on a change; this page did not. The chosen period
    /// is kept by value, since the options are rebuilt as new objects.
    /// </remarks>
    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        Budgets.Clear();
        foreach (var row in _lastProgress)
        {
            Budgets.Add(BudgetProgressViewModel.From(row, _localizer));
        }

        if (PeriodOptions.Count > 0)
        {
            var chosen = NewPeriod?.Period;
            PeriodOptions = PeriodChoices();
            OnPropertyChanged(nameof(PeriodOptions));
            NewPeriod = PeriodOptions.FirstOrDefault(o => o.Period == chosen) ?? PeriodOptions[0];
        }
    }

    private List<BudgetPeriodOption> PeriodChoices() =>
        new[] { BudgetPeriod.Monthly, BudgetPeriod.Weekly, BudgetPeriod.Yearly, BudgetPeriod.Custom }
            .Select(period => new BudgetPeriodOption(period, _localizer[BudgetProgressViewModel.PeriodKey(period)]))
            .ToList();

    private readonly LocalizedMessageSlot _createMessage;

    /// <summary>Opens the new-budget form, offering the currencies of the user's accounts.</summary>
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

        NewCategory = string.Empty;
        NewLimit = null;
        BaseCurrency = baseCurrency;
        NewPeriod = PeriodOptions[0];
        NewStartDate = null;
        NewLastDate = null;
        IsCreating = true;
    }

    /// <summary>Closes the form without creating anything.</summary>
    [RelayCommand]
    private void CancelCreate()
    {
        _createMessage.Clear();
        IsCreating = false;
    }

    /// <summary>Creates the budget, and shows it with its measured spend.</summary>
    /// <remarks>
    /// <para>
    /// THE LAST DAY IS INCLUSIVE ON SCREEN AND EXCLUSIVE IN THE MODEL. A person choosing "1 May to 31 May"
    /// means to include the 31st; <c>Budget.ResolveWindow</c> is half-open, <c>[Start, End)</c>. So the end
    /// sent is the day after the one picked. Sending the picked day would silently drop the last day's
    /// spending from every custom budget.
    /// </para>
    /// <para>
    /// Refusals come back from <c>CreateBudgetCommandValidator</c> as keys and are shown here; nothing is
    /// checked twice on the page, so the page cannot disagree with the command about what is valid.
    /// </para>
    /// </remarks>
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
                custom ? NewLastDate?.Date is { } last ? new DateTimeOffset(last.AddDays(1)) : null : null));

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

        // THE BUDGET IS SAVED FROM HERE ON, SO NOTHING BELOW MAY SAY OTHERWISE (independent review of item
        // 3.0). The reload used to sit inside the try above: a failed reload was caught as a failed create,
        // the screen said "Nothing was saved" about a budget that was, the list emptied, and a second attempt
        // was refused as a duplicate. A reload failure is a load failure, reported where a load failure is.
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

    private static readonly UserFacingMessage CreateFailed = new("Budgets.Create.Failure");

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
            _error.Set);
    }

    /// <inheritdoc />
    public InitializationFailurePolicy InitializationFailurePolicy => new(
        _logger,
        "budgets-load",
        BudgetsUnavailable,
        _error.Set);

    /// <summary>What the user reads when the budget list cannot be read.</summary>
    /// <remarks>
    /// <c>LoadBudgetsAsync</c> has a <c>try/finally</c> and no <c>catch</c>, and it clears
    /// <c>Budgets</c> BEFORE its await -- so a failure used to leave an empty list with
    /// <c>IsLoading</c> correctly false, which reads as "you have no budgets" (TD-35).
    /// </remarks>
    private static readonly UserFacingMessage BudgetsUnavailable = new("Budgets.Failure.Load");

    /// <summary>Releases the localized message slot.</summary>
    /// <remarks>
    /// CA1001, for the reason every page that owns a slot gives: it subscribes to the process-lifetime
    /// localizer. <c>PageActivation.Deactivate</c> disposes a page that implements this.
    /// </remarks>
    public void Dispose()
    {
        _localizer.LanguageChanged -= OnLanguageChanged;
        _error.Dispose();
        _createMessage.Dispose();
        GC.SuppressFinalize(this);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
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
            var progress = await _mediator.Send(new GetBudgetProgressQuery());
            _lastProgress = progress;

            foreach (var row in progress)
            {
                Budgets.Add(BudgetProgressViewModel.From(row, _localizer));
            }
        }
        finally
        {
            IsLoading = false;
        }
    }
}

/// <summary>A period the new-budget form offers, with its name in the user's language.</summary>
/// <param name="Period">The period.</param>
/// <param name="Label">What the picker shows.</param>
public sealed record BudgetPeriodOption(BudgetPeriod Period, string Label)
{
    /// <inheritdoc />
    public override string ToString() => Label;
}
