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
    public ScanReceiptView()
    {
        InitializeComponent();
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
