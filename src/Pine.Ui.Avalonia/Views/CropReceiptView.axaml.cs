using Avalonia.Controls;
using Avalonia.Interactivity;
using Pine.Ui.Avalonia.ViewModels;

namespace Pine.Ui.Avalonia.Views;

public partial class CropReceiptView : UserControl
{
    public CropReceiptView()
    {
        InitializeComponent();
    }

    private async void OnConfirmClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CropReceiptViewModel vm)
        {
            await vm.ConfirmCropAsync(
                Cropper.TopLeft.X, Cropper.TopLeft.Y,
                Cropper.TopRight.X, Cropper.TopRight.Y,
                Cropper.BottomRight.X, Cropper.BottomRight.Y,
                Cropper.BottomLeft.X, Cropper.BottomLeft.Y
            );
        }
    }
}
