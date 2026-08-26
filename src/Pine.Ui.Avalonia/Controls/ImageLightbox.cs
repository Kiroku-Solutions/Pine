using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Pine.Ui.Avalonia.Controls;

/// <summary>
/// Reusable full-size image viewer ("lightbox") with mouse-wheel zoom and
/// drag-to-pan. Drop it into any view, bind <see cref="Source"/> to the image
/// and toggle <see cref="IsOpen"/> to show/hide it:
/// <code>
/// &lt;controls:ImageLightbox Source="{Binding ReceiptImage}" IsOpen="{Binding IsPreviewOpen}" /&gt;
/// </code>
/// The host is only responsible for the thumbnail and for setting IsOpen=true.
/// </summary>
public class ImageLightbox : TemplatedControl
{
    public static readonly StyledProperty<Bitmap?> SourceProperty =
        AvaloniaProperty.Register<ImageLightbox, Bitmap?>(nameof(Source));

    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<ImageLightbox, bool>(
            nameof(IsOpen), defaultBindingMode: BindingMode.TwoWay);

    public Bitmap? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public bool IsOpen
    {
        get => GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    private const double MinZoom = 1.0;
    private const double MaxZoom = 8.0;

    private Image? _image;
    private Control? _viewport;
    private double _zoom = 1.0;
    private double _panX;
    private double _panY;
    private bool _isPanning;
    private Point _panStart;

    public ImageLightbox()
    {
        Focusable = true; // so Escape can close it
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (_viewport != null)
        {
            _viewport.PointerWheelChanged -= OnWheel;
            _viewport.PointerPressed -= OnPointerPressed;
            _viewport.PointerMoved -= OnPointerMoved;
            _viewport.PointerReleased -= OnPointerReleased;
        }

        _viewport = e.NameScope.Find<Control>("PART_Viewport");
        _image = e.NameScope.Find<Image>("PART_Image");

        if (_viewport != null)
        {
            _viewport.PointerWheelChanged += OnWheel;
            _viewport.PointerPressed += OnPointerPressed;
            _viewport.PointerMoved += OnPointerMoved;
            _viewport.PointerReleased += OnPointerReleased;
        }

        WireButton(e, "PART_ZoomIn", () => SetZoom(_zoom * 1.3));
        WireButton(e, "PART_ZoomOut", () => SetZoom(_zoom / 1.3));
        WireButton(e, "PART_Reset", ResetZoom);
        WireButton(e, "PART_Close", () => IsOpen = false);

        ApplyTransform();
    }

    private static void WireButton(TemplateAppliedEventArgs e, string name, Action action)
    {
        if (e.NameScope.Find<Button>(name) is { } button)
            button.Click += (_, _) => action();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsOpenProperty && change.GetNewValue<bool>())
        {
            // Reset to "fit" and grab focus each time it opens.
            ResetZoom();
            Dispatcher.UIThread.Post(() => Focus());
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && IsOpen)
        {
            IsOpen = false;
            e.Handled = true;
        }
    }

    // ─── Zoom & pan ───────────────────────────────────────────────────────
    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        var factor = e.Delta.Y > 0 ? 1.15 : 1.0 / 1.15;
        SetZoom(_zoom * factor);
        e.Handled = true;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_zoom <= MinZoom || _viewport is null) return;
        _isPanning = true;
        _panStart = e.GetPosition(_viewport);
        e.Pointer.Capture(_viewport);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isPanning || _viewport is null) return;
        var p = e.GetPosition(_viewport);
        _panX += p.X - _panStart.X;
        _panY += p.Y - _panStart.Y;
        _panStart = p;
        ApplyTransform();
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _isPanning = false;
        e.Pointer.Capture(null);
    }

    private void SetZoom(double value)
    {
        _zoom = Math.Clamp(value, MinZoom, MaxZoom);
        if (_zoom <= MinZoom) { _panX = 0; _panY = 0; }
        ApplyTransform();
    }

    private void ResetZoom()
    {
        _zoom = 1.0;
        _panX = 0;
        _panY = 0;
        _isPanning = false;
        ApplyTransform();
    }

    private void ApplyTransform()
    {
        if (_image is null) return;
        _image.RenderTransform = new TransformGroup
        {
            Children =
            {
                new ScaleTransform(_zoom, _zoom),
                new TranslateTransform(_panX, _panY)
            }
        };
    }
}
