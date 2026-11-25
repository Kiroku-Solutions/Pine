using Avalonia;
using Avalonia.Controls;

namespace Pine.Ui.Avalonia.Views;

using Pine.Ui.Avalonia.Services;
using Pine.Ui.Avalonia.ViewModels;

public partial class TransactionsView : UserControl
{
    private const double CompactWidth = 700;
    private bool? _compact;

    public TransactionsView()
    {
        InitializeComponent();
        SizeChanged += (_, e) => ApplyLayout(e.NewSize.Width);
        DataContextChanged += (_, _) =>
        {
            // TD-141: the view owns the window, so it hands the view model the Avalonia file picker.
            // The view model only ever sees IAttachmentFilePicker.
            if (DataContext is TransactionsViewModel vm)
            {
                vm.AttachmentFilePicker = new AvaloniaAttachmentFilePicker(() => TopLevel.GetTopLevel(this));
            }
        };
    }

    private void ApplyLayout(double width)
    {
        if (width <= 0)
            return;

        var compact = width < CompactWidth;
        if (_compact == compact)
            return;

        _compact = compact;
        var mainGrid = this.FindControl<Grid>("MainGrid");
        var headerGrid = this.FindControl<Grid>("HeaderGrid");
        var headerTitle = this.FindControl<TextBlock>("HeaderTitle");
        var headerActions = this.FindControl<StackPanel>("HeaderActions");
        var listBorder = this.FindControl<Border>("ListBorder");
        if (mainGrid is null || headerGrid is null || headerTitle is null || headerActions is null || listBorder is null)
            return;

        if (compact)
        {
            mainGrid.Margin = new Thickness(16);
            headerGrid.ColumnDefinitions = new ColumnDefinitions("*");
            headerGrid.RowDefinitions = new RowDefinitions("Auto,12,Auto");
            headerGrid.Margin = new Thickness(0, 0, 0, 16);
            Grid.SetColumn(headerActions, 0);
            Grid.SetRow(headerActions, 2);
            headerTitle.FontSize = 24;
            listBorder.Padding = new Thickness(12);
        }
        else
        {
            mainGrid.Margin = new Thickness(40);
            headerGrid.ColumnDefinitions = new ColumnDefinitions("*, Auto");
            headerGrid.RowDefinitions = new RowDefinitions();
            headerGrid.Margin = new Thickness(0, 0, 0, 40);
            Grid.SetColumn(headerActions, 1);
            Grid.SetRow(headerActions, 0);
            headerTitle.FontSize = 32;
            listBorder.Padding = new Thickness(20);
        }
    }
}
