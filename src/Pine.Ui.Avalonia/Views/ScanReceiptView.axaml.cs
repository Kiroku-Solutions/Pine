using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Pine.Ui.Avalonia.ViewModels;
using Pine.Ui.Avalonia.Diagnostics;
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
            null,
            "receipt-image-select",
            "That image could not be opened. Try another file.",
            message =>
            {
                if (DataContext is ScanReceiptViewModel target)
                {
                    target.ErrorMessage = message;
                }
            });
    }

    private async System.Threading.Tasks.Task SelectImageAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Receipt Image",
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
