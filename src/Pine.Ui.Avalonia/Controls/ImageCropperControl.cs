using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;

namespace Pine.Ui.Avalonia.Controls;

public class ImageCropperControl : TemplatedControl
{
    public static readonly StyledProperty<Bitmap?> SourceProperty =
        AvaloniaProperty.Register<ImageCropperControl, Bitmap?>(nameof(Source));

    public Bitmap? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    // Coordinates in image relative space (0.0 to 1.0)
    public Point TopLeft { get; set; } = new Point(0.1, 0.1);
    public Point TopRight { get; set; } = new Point(0.9, 0.1);
    public Point BottomRight { get; set; } = new Point(0.9, 0.9);
    public Point BottomLeft { get; set; } = new Point(0.1, 0.9);

    private Image? _imageControl;
    private CropAdorner? _adorner;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (_adorner != null && _imageControl != null)
        {
            var layer = AdornerLayer.GetAdornerLayer(this);
            layer?.Children.Remove(_adorner);
        }

        _imageControl = e.NameScope.Find<Image>("PART_Image");

        if (_imageControl != null)
        {
            _adorner = new CropAdorner(this, _imageControl);
            
            // Wait for layout to be ready to add adorner
            _imageControl.Loaded += (s, ev) =>
            {
                var layer = AdornerLayer.GetAdornerLayer(this);
                if (layer != null && !layer.Children.Contains(_adorner))
                {
                    AdornerLayer.SetAdornedElement(_adorner, _imageControl);
                    layer.Children.Add(_adorner);
                }
            };
        }
    }
}

public class CropAdorner : Control
{
    private readonly ImageCropperControl _parent;
    private readonly Image _adornedImage;
    private const double HandleRadius = 15;
    
    private int _draggingHandle = -1; // 0:TL, 1:TR, 2:BR, 3:BL
    private Point _dragStartPoint;
    private Point _originalHandlePos;

    private readonly IBrush _handleBrush = new SolidColorBrush(Color.Parse("#FF9800")); // Pine Orange
    private readonly IPen _linePen = new Pen(new SolidColorBrush(Color.Parse("#FF9800")), 2);

    public CropAdorner(ImageCropperControl parent, Image adornedImage)
    {
        _parent = parent;
        _adornedImage = adornedImage;
        ClipToBounds = false;
        ZIndex = 100; // Ensure it's on top
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (_adornedImage.Bounds.Width == 0 || _adornedImage.Bounds.Height == 0) return;

        var bounds = _adornedImage.Bounds;
        
        // Convert relative points to absolute screen points
        Point p0 = new Point(_parent.TopLeft.X * bounds.Width, _parent.TopLeft.Y * bounds.Height);
        Point p1 = new Point(_parent.TopRight.X * bounds.Width, _parent.TopRight.Y * bounds.Height);
        Point p2 = new Point(_parent.BottomRight.X * bounds.Width, _parent.BottomRight.Y * bounds.Height);
        Point p3 = new Point(_parent.BottomLeft.X * bounds.Width, _parent.BottomLeft.Y * bounds.Height);

        // Draw connecting lines
        context.DrawLine(_linePen, p0, p1);
        context.DrawLine(_linePen, p1, p2);
        context.DrawLine(_linePen, p2, p3);
        context.DrawLine(_linePen, p3, p0);

        // Draw translucent dark overlay outside the polygon
        DrawDarkOverlay(context, bounds, p0, p1, p2, p3);

        // Draw handles
        DrawHandle(context, p0);
        DrawHandle(context, p1);
        DrawHandle(context, p2);
        DrawHandle(context, p3);
    }

    private static void DrawDarkOverlay(DrawingContext context, Rect bounds, Point p0, Point p1, Point p2, Point p3)
    {
        // For simplicity, we just use a generic geometry to clip out the inner part, 
        // or draw a semi-transparent background. Avalonia Geometry implementation:
        var path = new PathGeometry();
        var figure = new PathFigure { StartPoint = p0, IsClosed = true };
        figure.Segments!.Add(new LineSegment { Point = p1 });
        figure.Segments.Add(new LineSegment { Point = p2 });
        figure.Segments.Add(new LineSegment { Point = p3 });
        path.Figures!.Add(figure);
        
        // Draw the dark overlay
        var group = new GeometryGroup { FillRule = FillRule.EvenOdd };
        group.Children.Add(new RectangleGeometry(bounds));
        group.Children.Add(path);
        
        context.DrawGeometry(new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)), null, group);
    }

    private void DrawHandle(DrawingContext context, Point p)
    {
        context.DrawEllipse(_handleBrush, new Pen(Brushes.White, 2), p, HandleRadius, HandleRadius);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var pos = e.GetPosition(this);
        
        var bounds = _adornedImage.Bounds;
        Point[] points = {
            new Point(_parent.TopLeft.X * bounds.Width, _parent.TopLeft.Y * bounds.Height),
            new Point(_parent.TopRight.X * bounds.Width, _parent.TopRight.Y * bounds.Height),
            new Point(_parent.BottomRight.X * bounds.Width, _parent.BottomRight.Y * bounds.Height),
            new Point(_parent.BottomLeft.X * bounds.Width, _parent.BottomLeft.Y * bounds.Height)
        };

        // Find which handle was clicked
        for (int i = 0; i < 4; i++)
        {
            var distance = Math.Sqrt(Math.Pow(pos.X - points[i].X, 2) + Math.Pow(pos.Y - points[i].Y, 2));
            if (distance <= HandleRadius * 2) // Generous hit area
            {
                _draggingHandle = i;
                _dragStartPoint = pos;
                _originalHandlePos = points[i];
                e.Handled = true;
                break;
            }
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_draggingHandle == -1) return;

        var pos = e.GetPosition(this);
        var bounds = _adornedImage.Bounds;
        
        var offsetX = pos.X - _dragStartPoint.X;
        var offsetY = pos.Y - _dragStartPoint.Y;

        var newX = Math.Clamp((_originalHandlePos.X + offsetX) / bounds.Width, 0.0, 1.0);
        var newY = Math.Clamp((_originalHandlePos.Y + offsetY) / bounds.Height, 0.0, 1.0);

        switch (_draggingHandle)
        {
            case 0: _parent.TopLeft = new Point(newX, newY); break;
            case 1: _parent.TopRight = new Point(newX, newY); break;
            case 2: _parent.BottomRight = new Point(newX, newY); break;
            case 3: _parent.BottomLeft = new Point(newX, newY); break;
        }

        InvalidateVisual(); // Request redraw
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _draggingHandle = -1;
        e.Handled = true;
    }
}
