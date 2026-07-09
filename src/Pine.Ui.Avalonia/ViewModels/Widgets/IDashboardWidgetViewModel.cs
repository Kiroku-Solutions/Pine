using System;

namespace Pine.Ui.Avalonia.ViewModels.Widgets;

public interface IDashboardWidgetViewModel
{
    string Title { get; }
    int ColumnSpan { get; set; }
    int RowSpan { get; set; }
    int OrderIndex { get; set; } // For drag and drop ordering
    double Width { get; set; }
    double Height { get; set; }
}
