using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Pine.Ui.Avalonia.Services;
using Pine.Ui.Avalonia.ViewModels;

namespace Pine.Ui.Avalonia.Views;

public partial class SettingsView : UserControl
{
    private const double CompactWidth = 700;
    private bool? _compact;

    public SettingsView()
    {
        InitializeComponent();
        SizeChanged += (_, e) => ApplyLayout(e.NewSize.Width);
        DataContextChanged += (_, _) =>
        {
            // TD-84: the view owns the window, so it hands the view model the Avalonia saver.
            if (DataContext is SettingsViewModel vm)
            {
                vm.DiagnosticBundleSaver = new AvaloniaDiagnosticBundleSaver(() => TopLevel.GetTopLevel(this));
            }
        };
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void ApplyLayout(double width)
    {
        if (width <= 0)
            return;

        var compact = width < CompactWidth;
        if (_compact == compact)
            return;

        _compact = compact;
        var stack = this.FindControl<StackPanel>("ContentStack");
        var title = this.FindControl<TextBlock>("SettingsTitle");
        if (stack is null || title is null)
            return;

        stack.Margin = compact ? new Thickness(16) : new Thickness(40);
        stack.Spacing = compact ? 16 : 24;
        title.FontSize = compact ? 24 : 32;

        StackRow(this.FindControl<Grid>("ThemeRow"), this.FindControl<ComboBox>("ThemeCombo"), compact, "*, Auto", HorizontalAlignment.Stretch);
        StackRow(this.FindControl<Grid>("AccentRow"), this.FindControl<ListBox>("AccentPicker"), compact, "*, Auto", HorizontalAlignment.Left);
        StackRow(this.FindControl<Grid>("LanguageRow"), this.FindControl<ComboBox>("LanguageCombo"), compact, "*, Auto", HorizontalAlignment.Stretch);
        StackRow(this.FindControl<Grid>("BaseCurrencyRow"), this.FindControl<AutoCompleteBox>("BaseCurrencyBox"), compact, "*, 240", HorizontalAlignment.Stretch);
    }

    private static void StackRow(Grid? row, Control? control, bool compact, string wideColumns, HorizontalAlignment compactAlign)
    {
        if (row is null || control is null)
            return;

        if (compact)
        {
            row.ColumnDefinitions = new ColumnDefinitions("*");
            row.RowDefinitions = new RowDefinitions("Auto,10,Auto");
            Grid.SetColumn(control, 0);
            Grid.SetRow(control, 2);
            control.HorizontalAlignment = compactAlign;
        }
        else
        {
            row.ColumnDefinitions = new ColumnDefinitions(wideColumns);
            row.RowDefinitions = new RowDefinitions();
            Grid.SetColumn(control, 1);
            Grid.SetRow(control, 0);
            control.HorizontalAlignment = HorizontalAlignment.Right;
        }
    }
}
