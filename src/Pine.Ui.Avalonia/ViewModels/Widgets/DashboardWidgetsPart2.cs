namespace Pine.Ui.Avalonia.ViewModels.Widgets;

public partial class BudgetWidgetViewModel : DashboardWidgetViewModelBase
{
    public BudgetWidgetViewModel()
    {
        Title = "Budget";
        ColumnSpan = 1;
        RowSpan = 2;
    }
}

public partial class SavingsWidgetViewModel : DashboardWidgetViewModelBase
{
    public SavingsWidgetViewModel()
    {
        Title = "Savings";
        ColumnSpan = 1;
        RowSpan = 1;
    }
}

public partial class NetWorthWidgetViewModel : DashboardWidgetViewModelBase
{
    public NetWorthWidgetViewModel()
    {
        Title = "Net Worth";
        ColumnSpan = 2;
        RowSpan = 1;
    }
}

public partial class CalendarWidgetViewModel : DashboardWidgetViewModelBase
{
    public CalendarWidgetViewModel()
    {
        Title = "Calendar";
        ColumnSpan = 1;
        RowSpan = 2;
    }
}
