using Avalonia.Controls;
using Pine.Ui.Avalonia.ViewModels;

namespace Pine.Ui.Avalonia.Views;

public partial class ShellView : UserControl
{
    /// <summary>Below this width the sidebar becomes a drawer.</summary>
    private const double CompactWidth = 720;

    public ShellView()
    {
        InitializeComponent();
        SizeChanged += (_, e) => ApplyWidth(e.NewSize.Width);
        DataContextChanged += (_, _) => ApplyWidth(Bounds.Width);
    }

    private void ApplyWidth(double width)
    {
        if (width <= 0)
            return;

        var compact = width < CompactWidth;
        var split = this.FindControl<SplitView>("NavSplit");
        if (split is not null)
            split.DisplayMode = compact ? SplitViewDisplayMode.Overlay : SplitViewDisplayMode.Inline;

        if (DataContext is ShellViewModel vm && vm.IsCompact != compact)
            vm.IsCompact = compact;
    }
}
