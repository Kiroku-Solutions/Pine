using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Pine.Ui.Avalonia.Views;

public partial class VaultSelectionView : UserControl
{
    private const double CompactWidth = 700;
    private bool? _compact;

    public VaultSelectionView()
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
        var rootGrid = this.FindControl<Grid>("RootGrid");
        var title = this.FindControl<TextBlock>("TitleText");
        var createButton = this.FindControl<Button>("CreateVaultButton");
        if (rootGrid is null || title is null || createButton is null)
            return;

        if (compact)
        {
            rootGrid.Margin = new Thickness(16, 20);
            title.FontSize = 24;
            createButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        }
        else
        {
            rootGrid.Margin = new Thickness(40);
            title.FontSize = 32;
            createButton.HorizontalAlignment = HorizontalAlignment.Right;
        }
    }
}
