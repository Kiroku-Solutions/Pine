using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Pine.Core.Ai;

namespace Pine.Ui.Avalonia.Controls;

/// <summary>
/// Interactive receipt image viewer where the user draws bounding boxes to tag fields
/// (Merchant, Total, Date, Tax) and feed the local on-device receipt learner.
/// Works identically with touch (Android) and mouse (Desktop).
/// </summary>
public sealed class ReceiptTagOverlayControl : Control
{
    public static readonly StyledProperty<IImage?> SourceProperty =
        AvaloniaProperty.Register<ReceiptTagOverlayControl, IImage?>(nameof(Source));

    public static readonly StyledProperty<ReceiptTagField> ActiveFieldProperty =
        AvaloniaProperty.Register<ReceiptTagOverlayControl, ReceiptTagField>(nameof(ActiveField), ReceiptTagField.Total);

    public static readonly StyledProperty<IReadOnlyList<ReceiptFieldBox>?> TaggedBoxesProperty =
        AvaloniaProperty.Register<ReceiptTagOverlayControl, IReadOnlyList<ReceiptFieldBox>?>(nameof(TaggedBoxes));

    public static readonly StyledProperty<IReadOnlyList<OcrWord>?> OcrWordsProperty =
        AvaloniaProperty.Register<ReceiptTagOverlayControl, IReadOnlyList<OcrWord>?>(nameof(OcrWords));

    public static readonly StyledProperty<int> OcrImageWidthProperty =
        AvaloniaProperty.Register<ReceiptTagOverlayControl, int>(nameof(OcrImageWidth));

    public static readonly StyledProperty<int> OcrImageHeightProperty =
        AvaloniaProperty.Register<ReceiptTagOverlayControl, int>(nameof(OcrImageHeight));

    public static readonly StyledProperty<IReadOnlyDictionary<ReceiptTagField, string>?> FieldLabelsProperty =
        AvaloniaProperty.Register<ReceiptTagOverlayControl, IReadOnlyDictionary<ReceiptTagField, string>?>(nameof(FieldLabels));

    private static readonly IBrush OcrHintBrush = new ImmutableSolidColorBrush(Color.FromArgb(42, 255, 255, 255));
    private static readonly IPen OcrHintPen = new Pen(new ImmutableSolidColorBrush(Color.FromArgb(95, 255, 255, 255)), 1);
    private static readonly IBrush LabelTextBrush = Brushes.White;
    private static readonly Typeface LabelTypeface = new(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);

    private static readonly Dictionary<ReceiptTagField, (IBrush Fill, IPen Stroke, IBrush Badge)> Palette = new()
    {
        [ReceiptTagField.Merchant] = (
            new ImmutableSolidColorBrush(Color.FromArgb(60, 4, 120, 87)),
            new Pen(new ImmutableSolidColorBrush(Color.Parse("#047857")), 2.5),
            new ImmutableSolidColorBrush(Color.Parse("#047857"))),
        [ReceiptTagField.Total] = (
            new ImmutableSolidColorBrush(Color.FromArgb(60, 194, 65, 12)),
            new Pen(new ImmutableSolidColorBrush(Color.Parse("#C2410C")), 2.5),
            new ImmutableSolidColorBrush(Color.Parse("#C2410C"))),
        [ReceiptTagField.Date] = (
            new ImmutableSolidColorBrush(Color.FromArgb(60, 29, 78, 216)),
            new Pen(new ImmutableSolidColorBrush(Color.Parse("#1D4ED8")), 2.5),
            new ImmutableSolidColorBrush(Color.Parse("#1D4ED8"))),
        [ReceiptTagField.Tax] = (
            new ImmutableSolidColorBrush(Color.FromArgb(60, 109, 40, 217)),
            new Pen(new ImmutableSolidColorBrush(Color.Parse("#6D28D9")), 2.5),
            new ImmutableSolidColorBrush(Color.Parse("#6D28D9"))),
    };

    private bool _isDrawing;
    private int? _activePointerId;
    private Point _dragStartNorm;
    private Point _dragCurrentNorm;

    static ReceiptTagOverlayControl()
    {
        AffectsRender<ReceiptTagOverlayControl>(
            SourceProperty,
            ActiveFieldProperty,
            TaggedBoxesProperty,
            OcrWordsProperty,
            OcrImageWidthProperty,
            OcrImageHeightProperty,
            FieldLabelsProperty);
    }

    public event EventHandler<ReceiptBoxDrawnEventArgs>? BoxDrawn;

    public IImage? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public ReceiptTagField ActiveField
    {
        get => GetValue(ActiveFieldProperty);
        set => SetValue(ActiveFieldProperty, value);
    }

    public IReadOnlyList<ReceiptFieldBox>? TaggedBoxes
    {
        get => GetValue(TaggedBoxesProperty);
        set => SetValue(TaggedBoxesProperty, value);
    }

    public IReadOnlyList<OcrWord>? OcrWords
    {
        get => GetValue(OcrWordsProperty);
        set => SetValue(OcrWordsProperty, value);
    }

    public int OcrImageWidth
    {
        get => GetValue(OcrImageWidthProperty);
        set => SetValue(OcrImageWidthProperty, value);
    }

    public int OcrImageHeight
    {
        get => GetValue(OcrImageHeightProperty);
        set => SetValue(OcrImageHeightProperty, value);
    }

    public IReadOnlyDictionary<ReceiptTagField, string>? FieldLabels
    {
        get => GetValue(FieldLabelsProperty);
        set => SetValue(FieldLabelsProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var source = Source;
        if (source is null)
        {
            return;
        }

        var fit = QuadEditorControl.FitRect(source.Size, Bounds.Size);
        if (fit.Width <= 0 || fit.Height <= 0)
        {
            return;
        }

        context.DrawImage(source, new Rect(source.Size), fit);

        // Draw subtle OCR word hints so the user sees what text boxes are recognized.
        if (OcrWords is { Count: > 0 } words)
        {
            var imgW = OcrImageWidth > 0 ? OcrImageWidth : source.Size.Width;
            var imgH = OcrImageHeight > 0 ? OcrImageHeight : source.Size.Height;
            if (imgW > 0 && imgH > 0)
            {
                foreach (var word in words)
                {
                    var b = word.BoundingBox;
                    var wr = new Rect(
                        fit.X + (b.X / imgW * fit.Width),
                        fit.Y + (b.Y / imgH * fit.Height),
                        Math.Max(2, b.Width / imgW * fit.Width),
                        Math.Max(2, b.Height / imgH * fit.Height));
                    context.DrawRectangle(OcrHintBrush, OcrHintPen, wr, 2, 2);
                }
            }
        }

        // Draw committed field boxes.
        if (TaggedBoxes is { Count: > 0 } boxes)
        {
            foreach (var item in boxes)
            {
                DrawFieldRect(context, fit, item.Field, item.Box);
            }
        }

        // Draw active in-progress rubber-band box.
        if (_isDrawing)
        {
            var draft = NormalizedRect.CreateClamped(_dragStartNorm.X, _dragStartNorm.Y, _dragCurrentNorm.X, _dragCurrentNorm.Y);
            DrawFieldRect(context, fit, ActiveField, draft);
        }
    }

    private void DrawFieldRect(DrawingContext context, Rect fit, ReceiptTagField field, NormalizedRect norm)
    {
        var (fill, stroke, badge) = Palette[field];
        var rect = new Rect(
            fit.X + (norm.X * fit.Width),
            fit.Y + (norm.Y * fit.Height),
            Math.Max(6, norm.Width * fit.Width),
            Math.Max(6, norm.Height * fit.Height));

        context.DrawRectangle(fill, stroke, rect, 4, 4);

        var label = FieldLabels is not null && FieldLabels.TryGetValue(field, out var localized)
            ? localized
            : field.ToString();

        var formatted = new FormattedText(
            label,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            LabelTypeface,
            11,
            LabelTextBrush);

        var pillWidth = formatted.Width + 10;
        var pillHeight = formatted.Height + 4;
        var pillY = rect.Y - pillHeight - 2 >= fit.Y ? rect.Y - pillHeight - 2 : rect.Y + 2;
        var pillX = Math.Clamp(rect.X, fit.X, Math.Max(fit.X, fit.Right - pillWidth));
        var pillRect = new Rect(pillX, pillY, pillWidth, pillHeight);

        context.DrawRectangle(badge, null, pillRect, 4, 4);
        context.DrawText(formatted, new Point(pillX + 5, pillY + 2));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Source is null || _isDrawing)
        {
            return;
        }

        if (e.Pointer.Type == PointerType.Mouse && !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var fit = QuadEditorControl.FitRect(Source.Size, Bounds.Size);
        if (fit.Width <= 0 || fit.Height <= 0)
        {
            return;
        }

        var pos = e.GetPosition(this);
        if (!fit.Contains(pos))
        {
            return;
        }

        _dragStartNorm = ToNormalized(pos, fit);
        _dragCurrentNorm = _dragStartNorm;
        _isDrawing = true;
        _activePointerId = e.Pointer.Id;
        e.Pointer.Capture(this);
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_isDrawing || _activePointerId != e.Pointer.Id || Source is null)
        {
            return;
        }

        var fit = QuadEditorControl.FitRect(Source.Size, Bounds.Size);
        if (fit.Width <= 0 || fit.Height <= 0)
        {
            return;
        }

        _dragCurrentNorm = ToNormalized(e.GetPosition(this), fit);
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_isDrawing || _activePointerId != e.Pointer.Id || Source is null)
        {
            return;
        }

        var fit = QuadEditorControl.FitRect(Source.Size, Bounds.Size);
        _dragCurrentNorm = fit.Width > 0 && fit.Height > 0
            ? ToNormalized(e.GetPosition(this), fit)
            : _dragCurrentNorm;

        _isDrawing = false;
        _activePointerId = null;
        e.Pointer.Capture(null);
        e.Handled = true;

        var drawn = NormalizedRect.CreateClamped(_dragStartNorm.X, _dragStartNorm.Y, _dragCurrentNorm.X, _dragCurrentNorm.Y);
        BoxDrawn?.Invoke(this, new ReceiptBoxDrawnEventArgs(ActiveField, drawn));
        InvalidateVisual();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_isDrawing && _activePointerId == e.Pointer.Id)
        {
            _isDrawing = false;
            _activePointerId = null;
            InvalidateVisual();
        }
    }

    private static Point ToNormalized(Point point, Rect fit) =>
        new(
            Math.Clamp((point.X - fit.X) / fit.Width, 0.0, 1.0),
            Math.Clamp((point.Y - fit.Y) / fit.Height, 0.0, 1.0));
}

public sealed class ReceiptBoxDrawnEventArgs : EventArgs
{
    public ReceiptBoxDrawnEventArgs(ReceiptTagField field, NormalizedRect box)
    {
        Field = field;
        Box = box;
    }

    public ReceiptTagField Field { get; }
    public NormalizedRect Box { get; }
}
