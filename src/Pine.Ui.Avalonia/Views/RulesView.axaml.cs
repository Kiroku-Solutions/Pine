using Avalonia;
using Avalonia.Controls;

namespace Pine.Ui.Avalonia.Views;

public partial class RulesView : UserControl
{
    private const double CompactWidth = 700;
    private bool? _compact;

    public RulesView()
    {
        InitializeComponent();
        SizeChanged += (_, e) => ApplyLayout(e.NewSize.Width);
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
        var title = this.FindControl<TextBlock>("RulesTitle");
        var card = this.FindControl<Border>("FormCard");
        var tcGrid = this.FindControl<Grid>("TitleCategoryGrid");
        var categoryStack = this.FindControl<StackPanel>("CategoryFieldStack");
        var condGrid = this.FindControl<Grid>("PrimaryConditionGrid");
        var fieldCombo = this.FindControl<ComboBox>("FieldCombo");
        var opCombo = this.FindControl<ComboBox>("OperatorCombo");
        var valueBox = this.FindControl<TextBox>("ValueBox");
        var priorityBox = this.FindControl<NumericUpDown>("PriorityBox");
        if (stack is null || title is null || card is null || tcGrid is null || categoryStack is null
            || condGrid is null || fieldCombo is null || opCombo is null || valueBox is null || priorityBox is null)
            return;

        if (compact)
        {
            stack.Margin = new Thickness(16);
            stack.Spacing = 16;
            title.FontSize = 24;
            card.Padding = new Thickness(16);

            tcGrid.ColumnDefinitions = new ColumnDefinitions("*");
            tcGrid.RowDefinitions = new RowDefinitions("Auto,12,Auto");
            Grid.SetColumn(categoryStack, 0);
            Grid.SetRow(categoryStack, 2);

            condGrid.ColumnDefinitions = new ColumnDefinitions("*");
            condGrid.RowDefinitions = new RowDefinitions("Auto,10,Auto,10,Auto,10,Auto");
            Grid.SetColumn(fieldCombo, 0); Grid.SetRow(fieldCombo, 0);
            Grid.SetColumn(opCombo, 0); Grid.SetRow(opCombo, 2);
            Grid.SetColumn(valueBox, 0); Grid.SetRow(valueBox, 4);
            Grid.SetColumn(priorityBox, 0); Grid.SetRow(priorityBox, 6);
        }
        else
        {
            stack.Margin = new Thickness(40);
            stack.Spacing = 24;
            title.FontSize = 28;
            card.Padding = new Thickness(24);

            tcGrid.ColumnDefinitions = new ColumnDefinitions("*, 12, *");
            tcGrid.RowDefinitions = new RowDefinitions("Auto, Auto");
            Grid.SetColumn(categoryStack, 2);
            Grid.SetRow(categoryStack, 0);

            condGrid.ColumnDefinitions = new ColumnDefinitions("Auto, 12, Auto, 12, *, 12, Auto");
            condGrid.RowDefinitions = new RowDefinitions();
            Grid.SetColumn(fieldCombo, 0); Grid.SetRow(fieldCombo, 0);
            Grid.SetColumn(opCombo, 2); Grid.SetRow(opCombo, 0);
            Grid.SetColumn(valueBox, 4); Grid.SetRow(valueBox, 0);
            Grid.SetColumn(priorityBox, 6); Grid.SetRow(priorityBox, 0);
        }
    }
}
