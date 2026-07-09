namespace Pine.Ui.Avalonia.ViewModels.Widgets;

public partial class BalanceWidgetViewModel : DashboardWidgetViewModelBase
{
    public BalanceWidgetViewModel()
    {
        Title = "Balance";
        ColumnSpan = 1;
        RowSpan = 1;
    }
}

public partial class ExpensesWidgetViewModel : DashboardWidgetViewModelBase
{
    public ExpensesWidgetViewModel()
    {
        Title = "Expenses";
        ColumnSpan = 1;
        RowSpan = 1;
    }
}

public partial class IncomeWidgetViewModel : DashboardWidgetViewModelBase
{
    public IncomeWidgetViewModel()
    {
        Title = "Income";
        ColumnSpan = 1;
        RowSpan = 1;
    }
}

public partial class InvestmentsWidgetViewModel : DashboardWidgetViewModelBase
{
    public InvestmentsWidgetViewModel()
    {
        Title = "Investments";
        ColumnSpan = 2;
        RowSpan = 2;
    }
}
