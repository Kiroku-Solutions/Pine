using System;
using Avalonia.Controls;
using Pine.Ui.Avalonia.ViewModels;

namespace Pine.Ui.Avalonia.Views;

public partial class ShellView : UserControl
{
    /// <summary>Below this width the sidebar becomes a drawer.</summary>
    private const double CompactWidth = 720;

    /// <summary>On a touch device a tablet held upright (about 800 dp) is too narrow for a fixed sidebar beside a form.</summary>
    private const double TouchCompactWidth = 900;

    public ShellView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) =>
        {
            if (OperatingSystem.IsAndroid() && this.FindControl<TransitioningContentControl>("ShellPageHost") is { } host)
            {
                host.PageTransition = null;
            }
        };
        SizeChanged += (_, e) => ApplyWidth(e.NewSize.Width);
        DataContextChanged += (_, _) => ApplyWidth(Bounds.Width);
    }

    private void ApplyWidth(double width)
    {
        if (width <= 0)
            return;

        var touch = OperatingSystem.IsAndroid() || OperatingSystem.IsIOS();
        var compact = width < (touch ? TouchCompactWidth : CompactWidth);
        var split = this.FindControl<SplitView>("NavSplit");
        if (split is not null)
        {
            split.DisplayMode = compact ? SplitViewDisplayMode.Overlay : SplitViewDisplayMode.Inline;
            split.Classes.Set("touch", touch);
            if (touch)
                split.OpenPaneLength = compact ? 300 : 260;
        }

        if (DataContext is ShellViewModel vm)
        {
            if (vm.IsCompact != compact)
                vm.IsCompact = compact;
            if (vm.UseBottomNav != (compact && touch))
                vm.UseBottomNav = compact && touch;
        }
    }
}
