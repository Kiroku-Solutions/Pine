namespace Pine.Ui.Avalonia.ViewModels.Widgets;

public partial class GoalsWidgetViewModel : DashboardWidgetViewModelBase
{
    public GoalsWidgetViewModel()
    {
        Title = "Goals";
        ColumnSpan = 1;
        RowSpan = 1;
    }
}

public partial class ChartsWidgetViewModel : DashboardWidgetViewModelBase
{
    public ChartsWidgetViewModel()
    {
        Title = "Charts";
        ColumnSpan = 2;
        RowSpan = 2;
    }
}

public partial class RecentTransactionsWidgetViewModel : DashboardWidgetViewModelBase
{
    public RecentTransactionsWidgetViewModel()
    {
        Title = "Recent Transactions";
        ColumnSpan = 2;
        RowSpan = 2;
    }
}

public partial class AiSuggestionsWidgetViewModel : DashboardWidgetViewModelBase
{
    public AiSuggestionsWidgetViewModel()
    {
        Title = "AI Suggestions";
        ColumnSpan = 1;
        RowSpan = 1;
    }
}
