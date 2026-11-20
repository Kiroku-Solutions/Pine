using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Pine.Ui.Avalonia.ViewModels;
using Pine.Ui.Avalonia.ViewModels.Widgets;
using System;
using System.Linq;

namespace Pine.Ui.Avalonia.Views;

public partial class DashboardView : UserControl
{
    private Point _dragStartPoint;
    private bool _isDragging;

    public DashboardView()
    {
        InitializeComponent();
        SizeChanged += (_, e) => ApplyLayout(e.NewSize.Width);
    }

    /// <summary>Below this width the tiles become one full-width column instead of fixed-size wrapped cards.</summary>
    private const double StackedWidth = 700;

    private bool? _stacked;

    private void ApplyLayout(double width)
    {
        if (width <= 0)
        {
            return;
        }

        var stacked = width < (OperatingSystem.IsAndroid() || OperatingSystem.IsIOS() ? 900 : StackedWidth);
        if (_stacked == stacked)
        {
            return;
        }

        _stacked = stacked;
        var items = this.FindControl<ItemsControl>("WidgetsItemsControl")!;
        var source = items.ItemsSource;
        items.ItemsSource = null;
        items.ItemsPanel = stacked
            ? new global::Avalonia.Controls.Templates.FuncTemplate<Panel?>(() => new StackPanel())
            : new global::Avalonia.Controls.Templates.FuncTemplate<Panel?>(() => new WrapPanel());
        items.ItemsSource = source;
        if (stacked)
        {
            global::Avalonia.Threading.Dispatcher.UIThread.Post(StretchTiles, global::Avalonia.Threading.DispatcherPriority.Loaded);
            items.ContainerPrepared += (_, _) => global::Avalonia.Threading.Dispatcher.UIThread.Post(StretchTiles, global::Avalonia.Threading.DispatcherPriority.Loaded);
        }
    }

    /// <summary>A tile's width comes from its view model (a desktop gesture); stacked, each takes the full column. Setting it locally replaces the binding until the list is rebuilt.</summary>
    private void StretchTiles()
    {
        if (_stacked != true)
        {
            return;
        }

        foreach (var card in global::Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(this).OfType<Border>())
        {
            if (card.Classes.Contains("card") && card.DataContext is IDashboardWidgetViewModel)
            {
                card.Width = double.NaN;
                card.Height = double.NaN;
                card.MaxWidth = double.PositiveInfinity;
                card.HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch;
            }
        }
    }

    private void ResizeThumb_DragDelta(object? sender, VectorEventArgs e)
    {
        if (sender is Thumb thumb && thumb.DataContext is IDashboardWidgetViewModel widget)
        {
            var newWidth = widget.Width + e.Vector.X;
            var newHeight = widget.Height + e.Vector.Y;

            // Constrain minimum size
            widget.Width = Math.Max(200, newWidth);
            widget.Height = Math.Max(150, newHeight);
        }
    }

    private Control? _draggedControl;
    private IDashboardWidgetViewModel? _draggedWidget;
    private global::Avalonia.Controls.Shapes.Rectangle? _dragGhost;
    private Point _dragOffset;

    private void Widget_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control control || control.DataContext is not IDashboardWidgetViewModel widget) return;

        var pointerPoint = e.GetCurrentPoint(this);
        if (!pointerPoint.Properties.IsLeftButtonPressed) return;

        // A finger on a tile is a scroll, not a reorder: capturing the pointer would freeze the page under it.
        if (e.Pointer.Type == PointerType.Touch) return;

        _draggedControl = control;
        _draggedWidget = widget;
        _dragStartPoint = e.GetPosition(this);
        _dragOffset = e.GetPosition(control); // Offset relative to the card's top-left
        _isDragging = false; // Will set to true in PointerMoved

        // Capture pointer so we get events even if mouse leaves bounds
        e.Pointer.Capture(control);
    }

    private void Widget_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_draggedControl == null || _draggedWidget == null) return;

        var point = e.GetPosition(this);
        
        if (!_isDragging)
        {
            var diff = point - _dragStartPoint;
            if (Math.Abs(diff.X) > 3 || Math.Abs(diff.Y) > 3)
            {
                _isDragging = true;
                
                _ghostRotate = new global::Avalonia.Media.RotateTransform(-2);
                var scaleTransform = new global::Avalonia.Media.ScaleTransform(0.95, 0.95);
                var transformGroup = new global::Avalonia.Media.TransformGroup();
                transformGroup.Children.Add(scaleTransform);
                transformGroup.Children.Add(_ghostRotate);

                // Create the ghost
                _dragGhost = new global::Avalonia.Controls.Shapes.Rectangle
                {
                    Width = _draggedControl.Bounds.Width,
                    Height = _draggedControl.Bounds.Height,
                    Fill = new global::Avalonia.Media.VisualBrush(_draggedControl),
                    Opacity = 0.9,
                    RenderTransformOrigin = new global::Avalonia.RelativePoint(0.5, 0.5, global::Avalonia.RelativeUnit.Relative),
                    RenderTransform = transformGroup
                };
                
                DragOverlayCanvas.Children.Add(_dragGhost);
                
                // Hide the original temporarily, but not too transparent
                _draggedControl.Opacity = 0.6;

                // Start jiggle timer
                _jiggleTimer = new global::Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
                _jiggleTimer.Tick += (s, ev) => 
                {
                    if (_ghostRotate != null) _ghostRotate.Angle = _ghostRotate.Angle > 0 ? -2 : 2;
                };
                _jiggleTimer.Start();
            }
        }

        if (_isDragging && _dragGhost != null)
        {
            // Move ghost with pointer
            global::Avalonia.Controls.Canvas.SetLeft(_dragGhost, point.X - _dragOffset.X);
            global::Avalonia.Controls.Canvas.SetTop(_dragGhost, point.Y - _dragOffset.Y);
        }
    }

    private void Widget_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        FinishDrag(e.GetPosition(this));
    }

    private void Widget_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        FinishDrag(new Point(-1, -1)); // Cancel drag if lost
    }

    private global::Avalonia.Threading.DispatcherTimer? _jiggleTimer;
    private global::Avalonia.Media.RotateTransform? _ghostRotate;

    private void FinishDrag(Point dropPoint)
    {
        if (_jiggleTimer != null)
        {
            _jiggleTimer.Stop();
            _jiggleTimer = null;
        }

        if (_draggedControl == null || _draggedWidget == null) return;

        _draggedControl.Opacity = 1.0;
        
        if (_isDragging && _dragGhost != null)
        {
            DragOverlayCanvas.Children.Remove(_dragGhost);
            
            // Hit test to find drop target
            var dropTargetControl = FindDropTarget(dropPoint);
            if (dropTargetControl != null && dropTargetControl.DataContext is IDashboardWidgetViewModel targetWidget)
            {
                if (_draggedWidget != targetWidget && DataContext is DashboardViewModel vm)
                {
                    var widgets = vm.Widgets;
                    int sourceIndex = widgets.IndexOf(_draggedWidget);
                    int targetIndex = widgets.IndexOf(targetWidget);

                    if (sourceIndex >= 0 && targetIndex >= 0)
                    {
                        widgets.Move(sourceIndex, targetIndex);
                        
                        for (int i = 0; i < widgets.Count; i++)
                        {
                            widgets[i].OrderIndex = i;
                        }
                    }
                }
            }
        }

        _isDragging = false;
        _dragGhost = null;
        _draggedControl = null;
        _draggedWidget = null;
        _ghostRotate = null;
    }

    private Control? FindDropTarget(Point point)
    {
        if (point.X < 0 || point.Y < 0) return null;

        // Perform basic hit testing on the items control children
        var itemsControl = this.FindControl<ItemsControl>("WidgetsItemsControl");
        if (itemsControl == null) return null;

        var panel = itemsControl.ItemsPanelRoot;
        if (panel == null) return null;

        foreach (Control child in panel.Children)
        {
            var bounds = child.Bounds;
            // Translate point to panel's coordinate space
            var p = this.TranslatePoint(point, panel);
            if (p.HasValue && bounds.Contains(p.Value))
            {
                return child; // This is the border card
            }
        }
        
        return null;
    }
}
