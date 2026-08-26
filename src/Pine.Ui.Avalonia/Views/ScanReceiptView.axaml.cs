using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Pine.Ui.Avalonia.ViewModels;
using System.IO;
using System.Linq;

namespace Pine.Ui.Avalonia.Views;

public partial class ScanReceiptView : UserControl
{
    public ScanReceiptView()
    {
        InitializeComponent();
    }

    private async void OnSelectImageClicked(object? sender, RoutedEventArgs e)
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
