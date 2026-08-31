using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MediatR;
using Pine.Application.Accounts.Queries;
using Pine.Core.Accounts;
using Pine.Core.Common.ValueObjects;
using Pine.Ui.Avalonia.ViewModels.Messages;
using Microsoft.Extensions.Logging;
using Pine.Ui.Avalonia.Diagnostics;

namespace Pine.Ui.Avalonia.ViewModels;

public partial class DashboardViewModel : ObservableObject, IRecipient<DataChangedMessage>
{
    private readonly IMediator _mediator;
    private readonly ILogger<DashboardViewModel>? _logger;

    [ObservableProperty]
    public partial string Greeting { get; set; } = "Welcome to Pine";

    [ObservableProperty]
    public partial string TotalBalance { get; set; } = "0.00 USD";

    public ObservableCollection<Account> Accounts { get; } = new();

    /// <summary>
    /// Renders the headline balance: one total per currency, each summed through <see cref="Money"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHAT THIS REPLACED WAS A FABRICATED NUMBER. The previous version added every account's
    /// <c>Balance.Amount</c> into one bare <see cref="decimal"/> and then labelled the result with
    /// whichever currency the LAST account in the loop happened to carry. A user holding 1 000 USD
    /// and 900 EUR was shown "1900 EUR" — a figure that is not a quantity of anything.
    /// <see cref="Money.Add"/> forbids exactly this; the old code got past it by never using
    /// <c>Money</c>, adding raw decimals instead.
    /// </para>
    /// <para>
    /// SO THE FIX IS TO SUM THROUGH THE DOMAIN, which makes the defect unrepresentable rather than
    /// merely absent: accounts are grouped by currency first, and each group is folded with
    /// <see cref="Money.Add"/>, so a cross-currency addition would now throw here instead of
    /// producing a plausible-looking wrong answer. Pine has no exchange-rate source, so there is no
    /// honest single number to show for a multi-currency vault, and inventing one is what the old
    /// code did.
    /// </para>
    /// <para>
    /// The previous format string was <c>{0:C2}</c>, which renders the CURRENT MACHINE's currency
    /// symbol. On a Spanish-locale machine a dollar balance came out as "1.234,56 € USD" — a euro
    /// sign next to the code USD. Each figure is now written by <see cref="Money.ToString"/>, at its
    /// own currency's scale, with its own code and no borrowed symbol.
    /// </para>
    /// </remarks>
    private static string SummariseBalances(IEnumerable<Account> accounts)
    {
        var totals = accounts
            .GroupBy(a => a.Balance.Currency)
            .Select(group => group.Aggregate(
                Money.Zero(group.Key),
                (running, account) => running + account.Balance))
            .OrderBy(total => total.Currency.Value, StringComparer.Ordinal)
            .ToList();

        return totals.Count == 0
            ? Money.Zero(new CurrencyCode("USD")).ToString()
            : string.Join("  ·  ", totals);
    }

    public ObservableCollection<Pine.Ui.Avalonia.ViewModels.Widgets.IDashboardWidgetViewModel> Widgets { get; } = new();

    [ObservableProperty]
    public partial bool IsCreateAccountDialogVisible { get; set; }

    [ObservableProperty]
    public partial CreateAccountViewModel? CreateAccountVm { get; set; }

    [ObservableProperty]
    public partial bool IsImportWizardVisible { get; set; }

    [ObservableProperty]
    public partial Pine.Ui.Avalonia.ViewModels.Importing.ImportWizardViewModel? ImportWizardVm { get; set; }

    public DashboardViewModel(IMediator mediator, ILogger<DashboardViewModel>? logger = null)
    {
        _mediator = mediator;
        _logger = logger;
        InitializeWidgets();
        WeakReferenceMessenger.Default.Register<DataChangedMessage>(this, (r, m) => ((DashboardViewModel)r).Receive(m));
        _ = LoadDataAsync();
    }

    public void Receive(DataChangedMessage message)
    {
        _ = LoadDataAsync();
    }

    private void InitializeWidgets()
    {
        Widgets.Add(new Pine.Ui.Avalonia.ViewModels.Widgets.BalanceWidgetViewModel(_mediator) { OrderIndex = 0 });
        Widgets.Add(new Pine.Ui.Avalonia.ViewModels.Widgets.NetWorthWidgetViewModel { OrderIndex = 1 });
        Widgets.Add(new Pine.Ui.Avalonia.ViewModels.Widgets.ExpensesWidgetViewModel { OrderIndex = 2 });
        Widgets.Add(new Pine.Ui.Avalonia.ViewModels.Widgets.IncomeWidgetViewModel { OrderIndex = 3 });
        Widgets.Add(new Pine.Ui.Avalonia.ViewModels.Widgets.BudgetWidgetViewModel { OrderIndex = 4 });
        Widgets.Add(new Pine.Ui.Avalonia.ViewModels.Widgets.SavingsWidgetViewModel { OrderIndex = 5 });
        Widgets.Add(new Pine.Ui.Avalonia.ViewModels.Widgets.CalendarWidgetViewModel { OrderIndex = 6 });
        Widgets.Add(new Pine.Ui.Avalonia.ViewModels.Widgets.GoalsWidgetViewModel { OrderIndex = 7 });
        Widgets.Add(new Pine.Ui.Avalonia.ViewModels.Widgets.InvestmentsWidgetViewModel { OrderIndex = 8 });
        Widgets.Add(new Pine.Ui.Avalonia.ViewModels.Widgets.ChartsWidgetViewModel { OrderIndex = 9 });
        Widgets.Add(new Pine.Ui.Avalonia.ViewModels.Widgets.RecentTransactionsWidgetViewModel(_mediator) { OrderIndex = 10 });
        Widgets.Add(new Pine.Ui.Avalonia.ViewModels.Widgets.AiSuggestionsWidgetViewModel { OrderIndex = 11 });
    }

    [RelayCommand]
    private void ShowCreateAccountDialog()
    {
        CreateAccountVm = new CreateAccountViewModel(_mediator, () =>
        {
            IsCreateAccountDialogVisible = false;
            CreateAccountVm = null;
            _ = LoadDataAsync(); // Refresh accounts after creation
        });
        IsCreateAccountDialogVisible = true;
    }

    [RelayCommand]
    private void ShowImportWizard()
    {
        ImportWizardVm = new Pine.Ui.Avalonia.ViewModels.Importing.ImportWizardViewModel(_mediator, TimeProvider.System, () => 
        {
            IsImportWizardVisible = false;
            ImportWizardVm = null;
            _ = LoadDataAsync();
        });
        IsImportWizardVisible = true;
    }

    [RelayCommand]
    private async Task LoadDataAsync()
    {
        try
        {
            var accounts = await _mediator.Send(new GetAccountsQuery());
            Accounts.Clear();
            
            foreach (var account in accounts)
            {
                Accounts.Add(account);
            }

            TotalBalance = SummariseBalances(accounts);
        }
        catch (Exception ex)
        {
            // WAS: "Error loading data: " + ex.Message (TD-24). This is a READ path, so there is
            // nothing to reassure the user about having been written; what they need is to know the
            // figures on screen are not theirs. The sentence still lands in Greeting because that is
            // the only bound text this screen has -- giving the dashboard a dedicated error surface
            // is a view change and is not this ticket.
            Greeting = UserFacingError.Report(
                _logger,
                ex,
                "dashboard-load",
                "Your dashboard could not be loaded, so the figures below may be incomplete. Try again.");
        }
    }
}
