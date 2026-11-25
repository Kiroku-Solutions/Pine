using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Pine.Ui.Avalonia.Views;

public partial class UnlockView : UserControl
{
    private const double CompactWidth = 700;
    private bool? _compact;

    public UnlockView()
    {
        InitializeComponent();
        SizeChanged += (_, e) => ApplyLayout(e.NewSize.Width);
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
        var card = this.FindControl<Border>("UnlockCard");
        var stack = this.FindControl<StackPanel>("ContentStack");
        var title = this.FindControl<TextBlock>("BrandTitle");
        if (card is null || stack is null || title is null)
            return;

        if (compact)
        {
            stack.Margin = new Thickness(16, 20);
            stack.Spacing = 22;
            title.FontSize = 36;
            card.Padding = new Thickness(20);
        }
        else
        {
            stack.Margin = new Thickness(32, 40);
            stack.Spacing = 32;
            title.FontSize = 48;
            card.Padding = new Thickness(40);
        }
    }
}
