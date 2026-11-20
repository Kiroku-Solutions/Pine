using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Pine.Core.Common;
using Pine.Ui.Avalonia.ViewModels;
using Pine.Ui.Avalonia.Diagnostics;
using Pine.Ui.Avalonia.Localization;
using System.IO;
using System.Linq;

namespace Pine.Ui.Avalonia.Views;

public partial class ScanReceiptView : UserControl
{
    /// <summary>Below this width the two columns stack: preview on top, form underneath, one scrolling page.</summary>
    private const double StackedWidth = 700;

    private bool? _stacked;

    public ScanReceiptView()
    {
        InitializeComponent();
        SizeChanged += (_, e) => ApplyLayout(e.NewSize.Width);
    }

    private void ApplyLayout(double width)
    {
        if (width <= 0)
        {
            return;
        }

        var stacked = width < StackedWidth;
        if (_stacked == stacked)
        {
            return;
        }

        _stacked = stacked;
        var cols = this.FindControl<Grid>("Cols")!;
        var left = this.FindControl<StackPanel>("LeftPane")!;
        var right = this.FindControl<Grid>("RightPane")!;
        var card = this.FindControl<Border>("Card")!;
        var preview = this.FindControl<Border>("Preview")!;
        var page = this.FindControl<ScrollViewer>("PageScroll")!;
        var fields = this.FindControl<ScrollViewer>("FieldsScroll")!;

        if (stacked)
        {
            cols.ColumnDefinitions = new ColumnDefinitions("*");
            cols.RowDefinitions = new RowDefinitions("Auto,Auto");
            Grid.SetColumn(left, 0);
            Grid.SetRow(left, 0);
            Grid.SetColumn(right, 0);
            Grid.SetRow(right, 1);
            right.RowDefinitions = new RowDefinitions("Auto,Auto");
            right.VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top;
            right.Margin = new global::Avalonia.Thickness(0, 24, 0, 0);
            card.Padding = new global::Avalonia.Thickness(16);
            card.Margin = new global::Avalonia.Thickness(8);
            card.VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top;
            preview.Height = 320;
            page.VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
            fields.VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
        }
        else
        {
            cols.ColumnDefinitions = new ColumnDefinitions("*, 30, 400");
            cols.RowDefinitions = new RowDefinitions();
            Grid.SetColumn(left, 0);
            Grid.SetRow(left, 0);
            Grid.SetColumn(right, 2);
            Grid.SetRow(right, 0);
            right.RowDefinitions = new RowDefinitions("*,Auto");
            right.VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Stretch;
            right.Margin = default;
            card.Padding = new global::Avalonia.Thickness(30);
            card.Margin = new global::Avalonia.Thickness(0, 20);
            card.VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Stretch;
            preview.Height = 400;
            page.VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
            fields.VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
        }
    }

    /// <summary>
    /// Picks a receipt image from disk. Was <c>async void</c> with four unguarded awaits until
    /// TD-35 -- the file picker, opening the file and copying it are all ordinary I/O failures.
    /// </summary>
    private void OnSelectImageClicked(object? sender, RoutedEventArgs e)
    {
        ObservedTask.Run(
            SelectImageAsync,
            (DataContext as ScanReceiptViewModel)?.Logger,
            "receipt-image-select",
            new UserFacingMessage("ScanReceipt.Failure.SelectImage"),
            messages =>
            {
                if (DataContext is ScanReceiptViewModel target)
                {
                    target.ReportFailure(messages);
                }
            });
    }

    private async System.Threading.Tasks.Task SelectImageAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        // Localizer.Instance, not an injected ILocalizer: FilePickerOpenOptions is built by a
        // code-behind method with no container reach, the same constraint TrExtension's and
        // DiagnosticsView's Copy-label fix document (TD-150).
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Localizer.Instance["ScanReceipt.FilePicker.Title"],
            AllowMultiple = false,
            FileTypeFilter = new[] { FilePickerFileTypes.ImageAll }
        });

        if (files.Count >= 1 && DataContext is ScanReceiptViewModel vm)
        {
            await using var stream = await files[0].OpenReadAsync();
            var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            ms.Position = 0;

            await vm.ProcessReceiptImageAsync(ms);
        }
    }

    // Opens the reusable ImageLightbox; all zoom/pan lives inside that control.
    private void OnPreviewImageClicked(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ScanReceiptViewModel vm && vm.ReceiptImage is not null)
            vm.IsImagePreviewOpen = true;
    }
}
