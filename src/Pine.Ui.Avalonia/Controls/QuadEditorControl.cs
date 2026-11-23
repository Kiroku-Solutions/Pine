using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Pine.Core.Ai;

namespace Pine.Ui.Avalonia.Controls;

/// <summary>
/// Shows a picture with a four-cornered outline over it. Editable, the corners have large touch handles (a 56 px hit
/// target) and a magnifier follows the finger so the edge under it stays visible; read-only, it is the live viewfinder's
/// overlay. Corners are fractions of the picture (<see cref="ReceiptQuad"/>), so the same value works at any size.
/// </summary>
/// <remarks>
/// Drawn directly (no adorner layer) so it works inside any page. Colours: a pink outline with a white keyline stays
/// above 4.5:1 against both a dark table and a white receipt; the area outside the outline is dimmed.
/// </remarks>
public sealed class QuadEditorControl : Control
{
    /// <summary>The picture shown (a camera frame or the upright photo).</summary>
    public static readonly StyledProperty<IImage?> SourceProperty =
        AvaloniaProperty.Register<QuadEditorControl, IImage?>(nameof(Source));

    /// <summary>The outline; null draws none.</summary>
    public static readonly StyledProperty<ReceiptQuad?> QuadProperty =
        AvaloniaProperty.Register<QuadEditorControl, ReceiptQuad?>(nameof(Quad), defaultBindingMode: global::Avalonia.Data.BindingMode.TwoWay);

    /// <summary>Whether the handles can be dragged.</summary>
    public static readonly StyledProperty<bool> IsEditableProperty =
        AvaloniaProperty.Register<QuadEditorControl, bool>(nameof(IsEditable));

    private const double HandleRadius = 14;
    private const double HitRadius = 28;
    private const double LensRadius = 52;
    private const double LensZoom = 3;

    private static readonly IBrush OutlineBrush = new SolidColorBrush(Color.Parse("#FF4F9A"));
    private static readonly IPen OutlinePen = new Pen(OutlineBrush, 3);
    private static readonly IPen KeylinePen = new Pen(Brushes.White, 6);
    private static readonly IBrush Dim = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0));

    private int _active = -1;

    static QuadEditorControl()
    {
        AffectsRender<QuadEditorControl>(SourceProperty, QuadProperty, IsEditableProperty);
    }

    /// <inheritdoc cref="SourceProperty"/>
    public IImage? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <inheritdoc cref="QuadProperty"/>
    public ReceiptQuad? Quad
    {
        get => GetValue(QuadProperty);
        set => SetValue(QuadProperty, value);
    }

    /// <inheritdoc cref="IsEditableProperty"/>
    public bool IsEditable
    {
        get => GetValue(IsEditableProperty);
        set => SetValue(IsEditableProperty, value);
    }

    /// <summary>The picture's rectangle inside the control (uniform fit, centred), in control coordinates.</summary>
    internal static Rect FitRect(Size picture, Size area)
    {
        if (picture.Width <= 0 || picture.Height <= 0 || area.Width <= 0 || area.Height <= 0)
        {
            return default;
        }

        var scale = Math.Min(area.Width / picture.Width, area.Height / picture.Height);
        var size = new Size(picture.Width * scale, picture.Height * scale);
        return new Rect((area.Width - size.Width) / 2, (area.Height - size.Height) / 2, size.Width, size.Height);
    }

    private static Point[] Corners(ReceiptQuad quad, Rect fit) =>
    [
        new(fit.X + (quad.TopLeft.X * fit.Width), fit.Y + (quad.TopLeft.Y * fit.Height)),
        new(fit.X + (quad.TopRight.X * fit.Width), fit.Y + (quad.TopRight.Y * fit.Height)),
        new(fit.X + (quad.BottomRight.X * fit.Width), fit.Y + (quad.BottomRight.Y * fit.Height)),
        new(fit.X + (quad.BottomLeft.X * fit.Width), fit.Y + (quad.BottomLeft.Y * fit.Height)),
    ];

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var source = Source;
        if (source is null)
        {
            return;
        }

        var fit = FitRect(source.Size, Bounds.Size);
        if (fit.Width <= 0)
        {
            return;
        }

        context.DrawImage(source, new Rect(source.Size), fit);
        if (Quad is not { } quad)
        {
            return;
        }

        var p = Corners(quad, fit);
        var shape = new PathGeometry();
        var figure = new PathFigure { StartPoint = p[0], IsClosed = true };
        figure.Segments!.Add(new LineSegment { Point = p[1] });
        figure.Segments.Add(new LineSegment { Point = p[2] });
        figure.Segments.Add(new LineSegment { Point = p[3] });
        shape.Figures!.Add(figure);

        var outside = new GeometryGroup { FillRule = FillRule.EvenOdd };
        outside.Children.Add(new RectangleGeometry(fit));
        outside.Children.Add(shape);
        context.DrawGeometry(Dim, null, outside);
        context.DrawGeometry(null, KeylinePen, shape);
        context.DrawGeometry(null, OutlinePen, shape);

        if (!IsEditable)
        {
            return;
        }

        for (var i = 0; i < 4; i++)
        {
            var radius = i == _active ? HandleRadius + 4 : HandleRadius;
            context.DrawEllipse(OutlineBrush, new Pen(Brushes.White, 3), p[i], radius, radius);
        }

        if (_active >= 0)
        {
            DrawLens(context, source, fit, p[_active]);
        }
    }

    private void DrawLens(DrawingContext context, IImage source, Rect fit, Point at)
    {
        // Above the finger, or below when the finger is near the top edge, clamped into the control.
        var centre = new Point(
            Math.Clamp(at.X, LensRadius, Math.Max(LensRadius, Bounds.Width - LensRadius)),
            at.Y - (LensRadius * 2) < 0 ? at.Y + (LensRadius * 2) : at.Y - (LensRadius * 2));
        var lens = new Rect(centre.X - LensRadius, centre.Y - LensRadius, LensRadius * 2, LensRadius * 2);
        var span = LensRadius / LensZoom;
        var sx = (at.X - fit.X) / fit.Width * source.Size.Width;
        var sy = (at.Y - fit.Y) / fit.Height * source.Size.Height;
        var sw = span * 2 / fit.Width * source.Size.Width;
        var sh = span * 2 / fit.Height * source.Size.Height;
        using (context.PushClip(new RoundedRect(lens, LensRadius)))
        {
            context.FillRectangle(Brushes.Black, lens);
            context.DrawImage(source, new Rect(sx - (sw / 2), sy - (sh / 2), sw, sh), lens);
            context.DrawLine(new Pen(OutlineBrush, 2), new Point(centre.X - 10, centre.Y), new Point(centre.X + 10, centre.Y));
            context.DrawLine(new Pen(OutlineBrush, 2), new Point(centre.X, centre.Y - 10), new Point(centre.X, centre.Y + 10));
        }

        context.DrawEllipse(null, new Pen(Brushes.White, 3), centre, LensRadius, LensRadius);
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!IsEditable || Quad is not { } quad || Source is null)
        {
            return;
        }

        var fit = FitRect(Source.Size, Bounds.Size);
        var at = e.GetPosition(this);
        var p = Corners(quad, fit);
        var best = -1;
        var bestDistance = HitRadius;
        for (var i = 0; i < 4; i++)
        {
            var d = Math.Sqrt(Math.Pow(at.X - p[i].X, 2) + Math.Pow(at.Y - p[i].Y, 2));
            if (d <= bestDistance)
            {
                bestDistance = d;
                best = i;
            }
        }

        if (best >= 0)
        {
            _active = best;
            e.Pointer.Capture(this);
            e.Handled = true;
            InvalidateVisual();
        }
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_active < 0 || Quad is not { } quad || Source is null)
        {
            return;
        }

        var fit = FitRect(Source.Size, Bounds.Size);
        var at = e.GetPosition(this);
        var moved = new NormalizedPoint(
            Math.Clamp((at.X - fit.X) / fit.Width, 0, 1),
            Math.Clamp((at.Y - fit.Y) / fit.Height, 0, 1));
        Quad = _active switch
        {
            0 => quad with { TopLeft = moved },
            1 => quad with { TopRight = moved },
            2 => quad with { BottomRight = moved },
            _ => quad with { BottomLeft = moved },
        };
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_active >= 0)
        {
            _active = -1;
            e.Pointer.Capture(null);
            InvalidateVisual();
        }
    }
}
