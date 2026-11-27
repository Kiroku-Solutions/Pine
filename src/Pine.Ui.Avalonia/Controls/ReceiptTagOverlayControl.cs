using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Pine.Core.Ai;

namespace Pine.Ui.Avalonia.Controls;

/// <summary>
/// Interactive receipt image viewer with zoom, pan, pinch-zoom, and bounding-box drawing so the user
/// can tag standard fields, products, and custom tags to feed the local on-device receipt learner.
/// Works identically with touch (Android) and mouse (Desktop).
/// </summary>
public sealed class ReceiptTagOverlayControl : Control
{
    public const double MinZoom = 1.0;
    public const double MaxZoom = 6.0;

    public static readonly StyledProperty<IImage?> SourceProperty =
        AvaloniaProperty.Register<ReceiptTagOverlayControl, IImage?>(nameof(Source));

    public static readonly StyledProperty<ReceiptTagField> ActiveFieldProperty =
        AvaloniaProperty.Register<ReceiptTagOverlayControl, ReceiptTagField>(nameof(ActiveField), ReceiptTagField.Total);

    public static readonly StyledProperty<string?> ActiveCustomTagNameProperty =
        AvaloniaProperty.Register<ReceiptTagOverlayControl, string?>(nameof(ActiveCustomTagName));

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

    public static readonly StyledProperty<double> ZoomProperty =
        AvaloniaProperty.Register<ReceiptTagOverlayControl, double>(
            nameof(Zoom),
            defaultValue: 1.0,
            defaultBindingMode: BindingMode.TwoWay,
            coerce: (_, value) => Math.Clamp(double.IsFinite(value) ? value : 1.0, MinZoom, MaxZoom));

    public static readonly StyledProperty<bool> IsPanModeProperty =
        AvaloniaProperty.Register<ReceiptTagOverlayControl, bool>(
            nameof(IsPanMode),
            defaultValue: false,
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<Guid?> SelectedBoxIdProperty =
        AvaloniaProperty.Register<ReceiptTagOverlayControl, Guid?>(
            nameof(SelectedBoxId),
            defaultValue: null,
            defaultBindingMode: BindingMode.TwoWay);

    // Subtle blue-slate tint stays visible on both white thermal paper and dark receipts.
    private static readonly IBrush OcrHintBrush = new ImmutableSolidColorBrush(Color.FromArgb(26, 37, 99, 235));
    private static readonly IPen OcrHintPen = new Pen(new ImmutableSolidColorBrush(Color.FromArgb(95, 37, 99, 235)), 1);
    private static readonly IPen SelectedHaloPen = new Pen(Brushes.White, 4.5);
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
        [ReceiptTagField.LineItem] = (
            new ImmutableSolidColorBrush(Color.FromArgb(60, 15, 118, 110)),
            new Pen(new ImmutableSolidColorBrush(Color.Parse("#0F766E")), 2.5),
            new ImmutableSolidColorBrush(Color.Parse("#0F766E"))),
        [ReceiptTagField.Custom] = (
            new ImmutableSolidColorBrush(Color.FromArgb(60, 190, 24, 93)),
            new Pen(new ImmutableSolidColorBrush(Color.Parse("#BE185D")), 2.5),
            new ImmutableSolidColorBrush(Color.Parse("#BE185D"))),
    };

    private readonly Dictionary<int, Point> _activePointers = new();

    private double _panX;
    private double _panY;
    private double _lastZoom = 1.0;
    private bool _updatingZoomInternally;

    private bool _isDrawing;
    private bool _isPanning;
    private bool _isPinching;
    private int? _activePointerId;
    private int? _pinchPointerA;
    private int? _pinchPointerB;

    private Point _dragStartScreen;
    private Point _dragStartNorm;
    private Point _dragCurrentNorm;

    private Point _panStartScreen;
    private Point _panOrigin;

    private double _pinchStartDistance;
    private double _pinchStartZoom;
    private Point _pinchStartMidpoint;
    private Point _pinchStartPan;

    static ReceiptTagOverlayControl()
    {
        AffectsRender<ReceiptTagOverlayControl>(
            SourceProperty,
            ActiveFieldProperty,
            ActiveCustomTagNameProperty,
            TaggedBoxesProperty,
            OcrWordsProperty,
            OcrImageWidthProperty,
            OcrImageHeightProperty,
            FieldLabelsProperty,
            ZoomProperty,
            IsPanModeProperty,
            SelectedBoxIdProperty);

        ZoomProperty.Changed.AddClassHandler<ReceiptTagOverlayControl>((ctrl, _) => ctrl.OnZoomChanged());
        SourceProperty.Changed.AddClassHandler<ReceiptTagOverlayControl>((ctrl, _) => ctrl.ResetViewport());
    }

    public event EventHandler<ReceiptBoxDrawnEventArgs>? BoxDrawn;
    public event EventHandler<ReceiptBoxSelectedEventArgs>? BoxSelected;

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

    public string? ActiveCustomTagName
    {
        get => GetValue(ActiveCustomTagNameProperty);
        set => SetValue(ActiveCustomTagNameProperty, value);
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

    public double Zoom
    {
        get => GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    public bool IsPanMode
    {
        get => GetValue(IsPanModeProperty);
        set => SetValue(IsPanModeProperty, value);
    }

    public Guid? SelectedBoxId
    {
        get => GetValue(SelectedBoxIdProperty);
        set => SetValue(SelectedBoxIdProperty, value);
    }

    public void ResetViewport()
    {
        _panX = 0;
        _panY = 0;
        _lastZoom = Zoom;
        _activePointers.Clear();
        _isDrawing = false;
        _isPanning = false;
        _isPinching = false;
        _activePointerId = null;
        _pinchPointerA = null;
        _pinchPointerB = null;
        InvalidateVisual();
    }

    private void OnZoomChanged()
    {
        var current = Zoom;
        if (!_updatingZoomInternally)
        {
            if (current <= 1.001)
            {
                _panX = 0;
                _panY = 0;
            }
            else if (_lastZoom > 0.001)
            {
                // Preserve center focus when zooming via +/- buttons.
                var ratio = current / _lastZoom;
                _panX *= ratio;
                _panY *= ratio;
            }
        }

        _lastZoom = current;
        if (Source is not null)
        {
            ClampPan(Source.Size, Bounds.Size);
        }

        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var source = Source;
        if (source is null)
        {
            return;
        }

        var viewRect = ComputeViewRect(source.Size, Bounds.Size);
        if (viewRect.Width <= 0 || viewRect.Height <= 0)
        {
            return;
        }

        var viewport = new Rect(Bounds.Size);
        using (context.PushClip(viewport))
        {
            context.DrawImage(source, new Rect(source.Size), viewRect);

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
                            viewRect.X + (b.X / imgW * viewRect.Width),
                            viewRect.Y + (b.Y / imgH * viewRect.Height),
                            Math.Max(2, b.Width / imgW * viewRect.Width),
                            Math.Max(2, b.Height / imgH * viewRect.Height));
                        context.DrawRectangle(OcrHintBrush, OcrHintPen, wr, 2, 2);
                    }
                }
            }

            if (TaggedBoxes is { Count: > 0 } boxes)
            {
                foreach (var item in boxes)
                {
                    var isSelected = SelectedBoxId.HasValue && item.BoxId == SelectedBoxId.Value;
                    DrawFieldRect(context, viewRect, viewport, item.Field, item.CustomTagName, item.Box, isSelected);
                }
            }

            if (_isDrawing)
            {
                var draft = NormalizedRect.CreateClamped(_dragStartNorm.X, _dragStartNorm.Y, _dragCurrentNorm.X, _dragCurrentNorm.Y);
                var customName = ActiveField == ReceiptTagField.Custom ? ActiveCustomTagName : null;
                DrawFieldRect(context, viewRect, viewport, ActiveField, customName, draft, isSelected: true);
            }
        }
    }

    private void DrawFieldRect(
        DrawingContext context,
        Rect viewRect,
        Rect viewport,
        ReceiptTagField field,
        string? customTagName,
        NormalizedRect norm,
        bool isSelected)
    {
        var (fill, stroke, badge) = Palette.TryGetValue(field, out var entry)
            ? entry
            : Palette[ReceiptTagField.Custom];

        var rect = new Rect(
            viewRect.X + (norm.X * viewRect.Width),
            viewRect.Y + (norm.Y * viewRect.Height),
            Math.Max(6, norm.Width * viewRect.Width),
            Math.Max(6, norm.Height * viewRect.Height));

        if (isSelected)
        {
            context.DrawRectangle(null, SelectedHaloPen, rect, 4, 4);
        }

        context.DrawRectangle(fill, stroke, rect, 4, 4);

        string label;
        if (field == ReceiptTagField.Custom && !string.IsNullOrWhiteSpace(customTagName))
        {
            label = customTagName.Trim();
        }
        else if (FieldLabels is not null && FieldLabels.TryGetValue(field, out var localized))
        {
            label = localized;
        }
        else
        {
            label = field.ToString();
        }

        var formatted = new FormattedText(
            label,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            LabelTypeface,
            11,
            LabelTextBrush);

        var pillWidth = formatted.Width + 10;
        var pillHeight = formatted.Height + 4;
        var minTop = Math.Max(viewRect.Y, viewport.Y + 2);
        var pillY = rect.Y - pillHeight - 2 >= minTop ? rect.Y - pillHeight - 2 : rect.Y + 2;
        var minLeft = Math.Max(viewRect.X, viewport.X + 2);
        var maxRight = Math.Min(viewRect.Right, viewport.Right - 2);
        var pillX = Math.Clamp(rect.X, minLeft, Math.Max(minLeft, maxRight - pillWidth));
        var pillRect = new Rect(pillX, pillY, pillWidth, pillHeight);

        context.DrawRectangle(badge, null, pillRect, 4, 4);
        context.DrawText(formatted, new Point(pillX + 5, pillY + 2));
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (Source is null)
        {
            return;
        }

        var factor = e.Delta.Y > 0 ? 1.2 : 1.0 / 1.2;
        var nextZoom = Math.Clamp(Zoom * factor, MinZoom, MaxZoom);
        if (Math.Abs(nextZoom - Zoom) > 0.001)
        {
            var pointer = e.GetPosition(this);
            ZoomAroundPoint(nextZoom, pointer);
            e.Handled = true;
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Source is null)
        {
            return;
        }

        if (e.Pointer.Type == PointerType.Mouse && !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var viewRect = ComputeViewRect(Source.Size, Bounds.Size);
        if (viewRect.Width <= 0 || viewRect.Height <= 0)
        {
            return;
        }

        // Ignore 3rd+ simultaneous pointers without polluting _activePointers.
        if (_isPinching || _activePointers.Count >= 2)
        {
            e.Handled = true;
            return;
        }

        var pos = e.GetPosition(this);

        // Second finger starts two-finger pinch-zoom & pan.
        if (_activePointers.Count == 1)
        {
            _activePointers[e.Pointer.Id] = pos;
            _isDrawing = false;
            _isPanning = false;
            _activePointerId = null;
            StartPinch();
            e.Pointer.Capture(this);
            e.Handled = true;
            InvalidateVisual();
            return;
        }

        if (IsPanMode)
        {
            _activePointers[e.Pointer.Id] = pos;
            _isPanning = true;
            _activePointerId = e.Pointer.Id;
            _panStartScreen = pos;
            _panOrigin = new Point(_panX, _panY);
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        if (!viewRect.Contains(pos))
        {
            return;
        }

        _activePointers[e.Pointer.Id] = pos;
        _dragStartScreen = pos;
        _dragStartNorm = ToNormalized(pos, viewRect);
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
        if (Source is null)
        {
            return;
        }

        var pos = e.GetPosition(this);
        if (_activePointers.ContainsKey(e.Pointer.Id))
        {
            _activePointers[e.Pointer.Id] = pos;
        }

        if (_isPinching)
        {
            UpdatePinch();
            e.Handled = true;
            return;
        }

        if (_isPanning && _activePointerId == e.Pointer.Id)
        {
            _panX = _panOrigin.X + (pos.X - _panStartScreen.X);
            _panY = _panOrigin.Y + (pos.Y - _panStartScreen.Y);
            ClampPan(Source.Size, Bounds.Size);
            e.Handled = true;
            InvalidateVisual();
            return;
        }

        if (!_isDrawing || _activePointerId != e.Pointer.Id)
        {
            return;
        }

        var viewRect = ComputeViewRect(Source.Size, Bounds.Size);
        if (viewRect.Width <= 0 || viewRect.Height <= 0)
        {
            return;
        }

        _dragCurrentNorm = ToNormalized(pos, viewRect);
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var wasTracked = _activePointers.Remove(e.Pointer.Id);

        if (_isPinching)
        {
            if (wasTracked)
            {
                e.Pointer.Capture(null);
            }

            if (e.Pointer.Id == _pinchPointerA || e.Pointer.Id == _pinchPointerB || _activePointers.Count < 2)
            {
                _isPinching = false;
                _pinchPointerA = null;
                _pinchPointerB = null;
            }

            e.Handled = true;
            return;
        }

        if (_isPanning && _activePointerId == e.Pointer.Id)
        {
            var pos = e.GetPosition(this);
            var movePx = double.Hypot(pos.X - _panStartScreen.X, pos.Y - _panStartScreen.Y);
            _isPanning = false;
            _activePointerId = null;
            e.Pointer.Capture(null);
            e.Handled = true;

            // Allow tapping an existing box while in Pan mode to select and edit it.
            if (movePx < 10 && Source is not null)
            {
                var panViewRect = ComputeViewRect(Source.Size, Bounds.Size);
                if (panViewRect.Width > 0 && panViewRect.Height > 0
                    && TryHitExistingBox(ToNormalized(pos, panViewRect), out var panHitId))
                {
                    SelectedBoxId = panHitId;
                    BoxSelected?.Invoke(this, new ReceiptBoxSelectedEventArgs(panHitId));
                    InvalidateVisual();
                }
            }

            return;
        }

        if (!_isDrawing || _activePointerId != e.Pointer.Id)
        {
            return;
        }

        _isDrawing = false;
        _activePointerId = null;
        e.Pointer.Capture(null);
        e.Handled = true;

        if (Source is null)
        {
            InvalidateVisual();
            return;
        }

        var releasePos = e.GetPosition(this);
        var viewRect = ComputeViewRect(Source.Size, Bounds.Size);
        _dragCurrentNorm = viewRect.Width > 0 && viewRect.Height > 0
            ? ToNormalized(releasePos, viewRect)
            : _dragCurrentNorm;

        var dragPx = double.Hypot(releasePos.X - _dragStartScreen.X, releasePos.Y - _dragStartScreen.Y);
        if (dragPx < 10)
        {
            if (TryHitExistingBox(_dragCurrentNorm, out var hitBoxId))
            {
                SelectedBoxId = hitBoxId;
                BoxSelected?.Invoke(this, new ReceiptBoxSelectedEventArgs(hitBoxId));
                InvalidateVisual();
                return;
            }

            // Ignore accidental taps on blank paper unless tapping directly on a detected OCR word.
            if (!IsNearOcrWord(_dragCurrentNorm))
            {
                InvalidateVisual();
                return;
            }
        }

        var drawn = NormalizedRect.CreateClamped(_dragStartNorm.X, _dragStartNorm.Y, _dragCurrentNorm.X, _dragCurrentNorm.Y);
        BoxDrawn?.Invoke(this, new ReceiptBoxDrawnEventArgs(ActiveField, drawn));
        InvalidateVisual();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _activePointers.Remove(e.Pointer.Id);

        if (e.Pointer.Id == _pinchPointerA || e.Pointer.Id == _pinchPointerB || _activePointers.Count < 2)
        {
            _isPinching = false;
            _pinchPointerA = null;
            _pinchPointerB = null;
        }

        if (_activePointerId == e.Pointer.Id)
        {
            _isDrawing = false;
            _isPanning = false;
            _activePointerId = null;
            InvalidateVisual();
        }
    }

    private bool IsNearOcrWord(Point norm)
    {
        if (OcrWords is not { Count: > 0 } words || Source is null)
        {
            return false;
        }

        var imgW = OcrImageWidth > 0 ? OcrImageWidth : Source.Size.Width;
        var imgH = OcrImageHeight > 0 ? OcrImageHeight : Source.Size.Height;
        if (imgW <= 0 || imgH <= 0)
        {
            return false;
        }

        foreach (var word in words)
        {
            var b = word.BoundingBox;
            var left = b.Left / imgW;
            var top = b.Top / imgH;
            var right = b.Right / imgW;
            var bottom = b.Bottom / imgH;

            var dx = norm.X < left ? left - norm.X : (norm.X > right ? norm.X - right : 0.0);
            var dy = norm.Y < top ? top - norm.Y : (norm.Y > bottom ? norm.Y - bottom : 0.0);
            if (double.Hypot(dx, dy) <= 0.035)
            {
                return true;
            }
        }

        return false;
    }

    private bool TryHitExistingBox(Point norm, out Guid boxId)
    {
        boxId = Guid.Empty;
        if (TaggedBoxes is not { Count: > 0 } boxes)
        {
            return false;
        }

        for (var i = boxes.Count - 1; i >= 0; i--)
        {
            var b = boxes[i];
            if (b.BoxId is { } id
                && norm.X >= b.Box.X - 0.01 && norm.X <= b.Box.Right + 0.01
                && norm.Y >= b.Box.Y - 0.01 && norm.Y <= b.Box.Bottom + 0.01)
            {
                boxId = id;
                return true;
            }
        }

        return false;
    }

    private void StartPinch()
    {
        using var e = _activePointers.GetEnumerator();
        if (!e.MoveNext())
        {
            return;
        }
        var first = e.Current;
        if (!e.MoveNext())
        {
            return;
        }
        var second = e.Current;

        _pinchPointerA = first.Key;
        _pinchPointerB = second.Key;
        _isPinching = true;
        _pinchStartDistance = Math.Max(1.0, double.Hypot(second.Value.X - first.Value.X, second.Value.Y - first.Value.Y));
        _pinchStartZoom = Zoom;
        _pinchStartMidpoint = new Point((first.Value.X + second.Value.X) * 0.5, (first.Value.Y + second.Value.Y) * 0.5);
        _pinchStartPan = new Point(_panX, _panY);
    }

    private void UpdatePinch()
    {
        if (Source is null
            || !_pinchPointerA.HasValue
            || !_pinchPointerB.HasValue
            || !_activePointers.TryGetValue(_pinchPointerA.Value, out var p1)
            || !_activePointers.TryGetValue(_pinchPointerB.Value, out var p2))
        {
            return;
        }

        var dist = double.Hypot(p2.X - p1.X, p2.Y - p1.Y);
        var mid = new Point((p1.X + p2.X) * 0.5, (p1.Y + p2.Y) * 0.5);

        var nextZoom = _pinchStartDistance > 8
            ? Math.Clamp(_pinchStartZoom * (dist / _pinchStartDistance), MinZoom, MaxZoom)
            : _pinchStartZoom;

        var scaleRatio = nextZoom / Math.Max(0.001, _pinchStartZoom);
        var fit = QuadEditorControl.FitRect(Source.Size, Bounds.Size);
        var centerX = fit.X + (fit.Width * 0.5);
        var centerY = fit.Y + (fit.Height * 0.5);

        var offsetX = _pinchStartMidpoint.X - centerX - _pinchStartPan.X;
        var offsetY = _pinchStartMidpoint.Y - centerY - _pinchStartPan.Y;

        _panX = _pinchStartPan.X + (mid.X - _pinchStartMidpoint.X) - (offsetX * (scaleRatio - 1.0));
        _panY = _pinchStartPan.Y + (mid.Y - _pinchStartMidpoint.Y) - (offsetY * (scaleRatio - 1.0));
        SetZoomInternal(nextZoom);
    }

    private void ZoomAroundPoint(double nextZoom, Point focus)
    {
        if (Source is null)
        {
            Zoom = nextZoom;
            return;
        }

        var fit = QuadEditorControl.FitRect(Source.Size, Bounds.Size);
        var centerX = fit.X + (fit.Width * 0.5);
        var centerY = fit.Y + (fit.Height * 0.5);
        var scaleRatio = nextZoom / Math.Max(0.001, Zoom);

        var offsetX = focus.X - centerX - _panX;
        var offsetY = focus.Y - centerY - _panY;

        _panX -= offsetX * (scaleRatio - 1.0);
        _panY -= offsetY * (scaleRatio - 1.0);
        SetZoomInternal(nextZoom);
    }

    private void SetZoomInternal(double nextZoom)
    {
        _updatingZoomInternally = true;
        try
        {
            if (Math.Abs(Zoom - nextZoom) > 0.0001)
            {
                Zoom = nextZoom;
            }
            else if (Source is not null)
            {
                ClampPan(Source.Size, Bounds.Size);
                InvalidateVisual();
            }
        }
        finally
        {
            _updatingZoomInternally = false;
        }
    }

    private Rect ComputeViewRect(Size sourceSize, Size boundsSize)
    {
        var fit = QuadEditorControl.FitRect(sourceSize, boundsSize);
        if (fit.Width <= 0 || fit.Height <= 0)
        {
            return fit;
        }

        var zoom = Math.Clamp(Zoom, MinZoom, MaxZoom);
        if (zoom <= 1.001)
        {
            _panX = 0;
            _panY = 0;
            return fit;
        }

        ClampPan(sourceSize, boundsSize);
        var w = fit.Width * zoom;
        var h = fit.Height * zoom;
        var cx = fit.X + (fit.Width * 0.5);
        var cy = fit.Y + (fit.Height * 0.5);
        return new Rect(cx - (w * 0.5) + _panX, cy - (h * 0.5) + _panY, w, h);
    }

    private void ClampPan(Size sourceSize, Size boundsSize)
    {
        var fit = QuadEditorControl.FitRect(sourceSize, boundsSize);
        var zoom = Math.Clamp(Zoom, MinZoom, MaxZoom);
        if (zoom <= 1.001)
        {
            _panX = 0;
            _panY = 0;
            return;
        }

        var scaledW = fit.Width * zoom;
        var scaledH = fit.Height * zoom;
        var maxPanX = Math.Max(0.0, (scaledW - boundsSize.Width) * 0.5);
        var maxPanY = Math.Max(0.0, (scaledH - boundsSize.Height) * 0.5);

        _panX = Math.Clamp(_panX, -maxPanX, maxPanX);
        _panY = Math.Clamp(_panY, -maxPanY, maxPanY);
    }

    private static Point ToNormalized(Point point, Rect viewRect) =>
        new(
            Math.Clamp((point.X - viewRect.X) / viewRect.Width, 0.0, 1.0),
            Math.Clamp((point.Y - viewRect.Y) / viewRect.Height, 0.0, 1.0));
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

public sealed class ReceiptBoxSelectedEventArgs : EventArgs
{
    public ReceiptBoxSelectedEventArgs(Guid boxId)
    {
        BoxId = boxId;
    }

    public Guid BoxId { get; }
}
