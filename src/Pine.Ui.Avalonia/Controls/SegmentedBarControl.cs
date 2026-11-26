using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Pine.Ui.Avalonia.Controls;

/// <summary>One colored slice of a <see cref="SegmentedBarControl"/>.</summary>
/// <param name="Value">Positive weight of this segment.</param>
/// <param name="ColorHex">Semantic fallback hex or resolved color for the slice.</param>
/// <param name="Label">Optional accessible/display label for legends.</param>
public sealed record ChartSegment(double Value, string ColorHex, string Label = "")
{
    private static readonly ConcurrentDictionary<string, IBrush> BrushCache = new(StringComparer.OrdinalIgnoreCase);

    public IBrush Brush => BrushCache.GetOrAdd(ColorHex, static hex => new ImmutableSolidColorBrush(Color.Parse(hex)));
}

/// <summary>
/// Lightweight proportional horizontal stacked bar chart drawn directly on Skia.
/// Used for monthly income allocation and account balance share breakdowns.
/// </summary>
public sealed class SegmentedBarControl : Control
{
    public static readonly StyledProperty<IEnumerable<ChartSegment>?> SegmentsProperty =
        AvaloniaProperty.Register<SegmentedBarControl, IEnumerable<ChartSegment>?>(nameof(Segments));

    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<SegmentedBarControl, IBrush?>(nameof(TrackBrush));

    public static readonly StyledProperty<double> BarCornerRadiusProperty =
        AvaloniaProperty.Register<SegmentedBarControl, double>(nameof(BarCornerRadius), 6.0);

    static SegmentedBarControl()
    {
        AffectsRender<SegmentedBarControl>(SegmentsProperty, TrackBrushProperty, BarCornerRadiusProperty);
    }

    public IEnumerable<ChartSegment>? Segments
    {
        get => GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    public IBrush? TrackBrush
    {
        get => GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public double BarCornerRadius
    {
        get => GetValue(BarCornerRadiusProperty);
        set => SetValue(BarCornerRadiusProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width <= 2 || height <= 2)
        {
            return;
        }

        var radius = Math.Min(BarCornerRadius, height / 2.0);
        var fullRect = new RoundedRect(new Rect(0, 0, width, height), radius);

        context.DrawRectangle(TrackBrush ?? Brushes.DimGray, null, fullRect);

        var items = Segments?.Where(s => s.Value > 0.0001).ToList();
        if (items is null || items.Count == 0)
        {
            return;
        }

        var total = items.Sum(s => s.Value);
        if (total <= 0)
        {
            return;
        }

        const double gap = 2.0;
        var totalGap = gap * Math.Max(0, items.Count - 1);
        var usableWidth = Math.Max(0, width - totalGap);

        using (context.PushClip(fullRect))
        {
            var x = 0.0;
            for (var i = 0; i < items.Count; i++)
            {
                var segment = items[i];
                var segWidth = i == items.Count - 1
                    ? Math.Max(0, width - x)
                    : Math.Max(2.0, usableWidth * (segment.Value / total));

                var rect = new Rect(x, 0, segWidth, height);
                context.DrawRectangle(segment.Brush, null, rect);
                x += segWidth + gap;
            }
        }
    }
}
