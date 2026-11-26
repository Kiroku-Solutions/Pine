using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Pine.Ui.Avalonia.Controls;

/// <summary>
/// Lightweight vector circular progress ring drawn natively on Skia for savings goals and status tiles.
/// </summary>
public sealed class RadialProgressRing : Control
{
    public static readonly StyledProperty<double> PercentageProperty =
        AvaloniaProperty.Register<RadialProgressRing, double>(nameof(Percentage), 0.0);

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<RadialProgressRing, double>(nameof(StrokeThickness), 7.0);

    public static readonly StyledProperty<IBrush?> RingBrushProperty =
        AvaloniaProperty.Register<RadialProgressRing, IBrush?>(nameof(RingBrush));

    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<RadialProgressRing, IBrush?>(nameof(TrackBrush));

    public static readonly StyledProperty<IBrush?> TextBrushProperty =
        AvaloniaProperty.Register<RadialProgressRing, IBrush?>(nameof(TextBrush));

    public static readonly StyledProperty<string?> CenterTextProperty =
        AvaloniaProperty.Register<RadialProgressRing, string?>(nameof(CenterText));

    static RadialProgressRing()
    {
        AffectsRender<RadialProgressRing>(
            PercentageProperty,
            StrokeThicknessProperty,
            RingBrushProperty,
            TrackBrushProperty,
            TextBrushProperty,
            CenterTextProperty);
    }

    public double Percentage
    {
        get => GetValue(PercentageProperty);
        set => SetValue(PercentageProperty, value);
    }

    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public IBrush? RingBrush
    {
        get => GetValue(RingBrushProperty);
        set => SetValue(RingBrushProperty, value);
    }

    public IBrush? TrackBrush
    {
        get => GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public IBrush? TextBrush
    {
        get => GetValue(TextBrushProperty);
        set => SetValue(TextBrushProperty, value);
    }

    public string? CenterText
    {
        get => GetValue(CenterTextProperty);
        set => SetValue(CenterTextProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (!double.IsFinite(Bounds.Width) || !double.IsFinite(Bounds.Height))
        {
            return;
        }

        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size < 8)
        {
            return;
        }

        var maxThickness = Math.Max(1.0, size / 4.0);
        var thickness = Math.Clamp(StrokeThickness, Math.Min(2.0, maxThickness), maxThickness);
        var radius = (size - thickness) / 2.0;
        var center = new Point(Bounds.Width / 2.0, Bounds.Height / 2.0);

        var trackBrush = TrackBrush ?? Brushes.Gray;
        var ringBrush = RingBrush ?? Brushes.MediumSeaGreen;
        var textBrush = TextBrush ?? Brushes.White;

        var trackPen = new Pen(trackBrush, thickness);
        context.DrawEllipse(null, trackPen, center, radius, radius);

        var clamped = double.IsFinite(Percentage) ? Math.Clamp(Percentage, 0.0, 100.0) : 0.0;
        if (clamped >= 99.95)
        {
            var fullPen = new Pen(ringBrush, thickness);
            context.DrawEllipse(null, fullPen, center, radius, radius);
        }
        else if (clamped > 0.1)
        {
            var sweepDegrees = (clamped / 100.0) * 360.0;
            var startRad = -Math.PI / 2.0;
            var endRad = startRad + (sweepDegrees * Math.PI / 180.0);

            var startPoint = new Point(
                center.X + radius * Math.Cos(startRad),
                center.Y + radius * Math.Sin(startRad));
            var endPoint = new Point(
                center.X + radius * Math.Cos(endRad),
                center.Y + radius * Math.Sin(endRad));

            var geometry = new StreamGeometry();
            using (var gc = geometry.Open())
            {
                gc.BeginFigure(startPoint, isFilled: false);
                gc.ArcTo(
                    endPoint,
                    new Size(radius, radius),
                    rotationAngle: 0,
                    isLargeArc: sweepDegrees > 180.0,
                    SweepDirection.Clockwise);
                gc.EndFigure(isClosed: false);
            }

            var arcPen = new Pen(ringBrush, thickness, lineCap: PenLineCap.Round);
            context.DrawGeometry(null, arcPen, geometry);
        }

        var label = string.IsNullOrWhiteSpace(CenterText)
            ? string.Create(CultureInfo.CurrentCulture, $"{Math.Round(clamped):0}%")
            : CenterText;

        var fontSize = Math.Clamp(size * 0.22, 10, 18);
        var formatted = new FormattedText(
            label,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold),
            fontSize,
            textBrush);

        var textOrigin = new Point(
            center.X - (formatted.Width / 2.0),
            center.Y - (formatted.Height / 2.0));
        context.DrawText(formatted, textOrigin);
    }
}
