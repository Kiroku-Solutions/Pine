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
        var addButton = this.FindControl<Button>("AddTransactionButton");
        var filterInputsGrid = this.FindControl<Grid>("FilterInputsGrid");
        var searchBox = this.FindControl<TextBox>("SearchBox");
        var accountFilterBox = this.FindControl<ComboBox>("AccountFilterBox");
        var listBorder = this.FindControl<Border>("ListBorder");
        if (mainGrid is null || headerGrid is null || headerTitle is null || headerActions is null || listBorder is null)
            return;

        if (compact)
        {
            mainGrid.Margin = new Thickness(16);
            headerGrid.ColumnDefinitions = new ColumnDefinitions("*");
            headerGrid.RowDefinitions = new RowDefinitions("Auto,12,Auto");
            headerGrid.Margin = new Thickness(0, 0, 0, 14);
            Grid.SetColumn(headerActions, 0);
            Grid.SetRow(headerActions, 2);
            headerActions.Orientation = global::Avalonia.Layout.Orientation.Vertical;
            headerActions.Spacing = 10;
            headerActions.HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch;
            if (addButton is not null)
            {
                addButton.HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch;
            }

            if (filterInputsGrid is not null && searchBox is not null && accountFilterBox is not null)
            {
                filterInputsGrid.ColumnDefinitions = new ColumnDefinitions("*");
                filterInputsGrid.RowDefinitions = new RowDefinitions("Auto,8,Auto");
                Grid.SetColumn(searchBox, 0);
                Grid.SetRow(searchBox, 0);
                Grid.SetColumn(accountFilterBox, 0);
                Grid.SetRow(accountFilterBox, 2);
                accountFilterBox.HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch;
                accountFilterBox.MaxWidth = double.PositiveInfinity;
            }

            headerTitle.FontSize = 24;
            listBorder.Padding = new Thickness(12);
        }
        else
        {
            mainGrid.Margin = new Thickness(40);
            headerGrid.ColumnDefinitions = new ColumnDefinitions("*, Auto");
            headerGrid.RowDefinitions = new RowDefinitions();
            headerGrid.Margin = new Thickness(0, 0, 0, 24);
            Grid.SetColumn(headerActions, 1);
            Grid.SetRow(headerActions, 0);
            headerActions.Orientation = global::Avalonia.Layout.Orientation.Horizontal;
            headerActions.Spacing = 12;
            headerActions.HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Right;
            if (addButton is not null)
            {
                addButton.HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center;
            }

            if (filterInputsGrid is not null && searchBox is not null && accountFilterBox is not null)
            {
                filterInputsGrid.ColumnDefinitions = new ColumnDefinitions("*, 10, Auto");
                filterInputsGrid.RowDefinitions = new RowDefinitions();
                Grid.SetColumn(searchBox, 0);
                Grid.SetRow(searchBox, 0);
                Grid.SetColumn(accountFilterBox, 2);
                Grid.SetRow(accountFilterBox, 0);
                accountFilterBox.HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Right;
                accountFilterBox.MaxWidth = 220;
            }

            headerTitle.FontSize = 32;
            listBorder.Padding = new Thickness(20);
        }
    }
}
