using CommunityToolkit.Mvvm.ComponentModel;

namespace Pine.Ui.Avalonia.ViewModels.Widgets;

public abstract partial class DashboardWidgetViewModelBase : ObservableObject, IDashboardWidgetViewModel
{
    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int ColumnSpan { get; set; } = 1;

    [ObservableProperty]
    public partial int RowSpan { get; set; } = 1;

    [ObservableProperty]
    public partial int OrderIndex { get; set; } = 0;

    [ObservableProperty]
    public partial double Width { get; set; } = 300;

    [ObservableProperty]
    public partial double Height { get; set; } = 200;
}
