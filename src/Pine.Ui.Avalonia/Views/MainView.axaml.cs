using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.VisualTree;

namespace Pine.Ui.Avalonia.Views;

/// <summary>
/// The root content of the application: the page navigator and the view-model-to-view templates.
/// Hosted by <see cref="MainWindow"/> on the desktop heads and set directly as the
/// <c>MainView</c> of the single-view lifetime on Android and iOS, which have no window.
/// </summary>
/// <remarks>
/// Deliberately free of the application statics: <c>MainWindow</c> and <c>App</c> are the only
/// types allowed to read them (see ArchitectureTests), so each attaches its own activity tracking.
/// </remarks>
public partial class MainView : UserControl
{
    private IInsetsManager? _insetsManager;

    public MainView()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        // CrossFade on Android can stall with an empty/grey surface when the EGL surface is
        // recreated across background/foreground transitions. Use crisp instant swaps on Android.
        if (OperatingSystem.IsAndroid() && this.FindControl<TransitioningContentControl>("PageHost") is { } host)
        {
            host.PageTransition = null;
        }

        _insetsManager = TopLevel.GetTopLevel(this)?.InsetsManager;
        if (_insetsManager is null)
            return;

        Padding = _insetsManager.SafeAreaPadding;
        _insetsManager.SafeAreaChanged += OnSafeAreaChanged;
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (_insetsManager is not null)
        {
            _insetsManager.SafeAreaChanged -= OnSafeAreaChanged;
            _insetsManager = null;
        }
    }

    private void OnSafeAreaChanged(object? sender, SafeAreaChangedArgs e)
    {
        Padding = e.SafeAreaPadding;
    }

    /// <summary>
    /// Forces a clean layout and repaint pass after a mobile EGL surface resumes from background.
    /// </summary>
    public void RefreshVisualTree()
    {
        var insets = TopLevel.GetTopLevel(this)?.InsetsManager;
        if (insets is not null)
        {
            Padding = insets.SafeAreaPadding;
        }

        InvalidateMeasure();
        InvalidateArrange();
        InvalidateVisual();

        if (this.FindControl<TransitioningContentControl>("PageHost") is { } host)
        {
            host.InvalidateMeasure();
            host.InvalidateArrange();
            host.InvalidateVisual();

            foreach (var descendant in host.GetVisualDescendants())
            {
                if (descendant is Visual visual)
                {
                    visual.InvalidateVisual();
                }
            }
        }
    }
}
