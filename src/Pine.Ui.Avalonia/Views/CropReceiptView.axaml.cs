using Avalonia.Controls;
using Avalonia.Interactivity;
using Pine.Core.Common;
using Pine.Ui.Avalonia.ViewModels;
using Pine.Ui.Avalonia.Diagnostics;

namespace Pine.Ui.Avalonia.Views;

public partial class CropReceiptView : UserControl
{
    public CropReceiptView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Confirms the crop. Was <c>async void</c> until TD-35.
    /// </summary>
    private void OnConfirmClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not CropReceiptViewModel vm)
        {
            return;
        }

        ObservedTask.Run(
            () => vm.ConfirmCropAsync(
                Cropper.TopLeft.X, Cropper.TopLeft.Y,
                Cropper.TopRight.X, Cropper.TopRight.Y,
                Cropper.BottomRight.X, Cropper.BottomRight.Y,
                Cropper.BottomLeft.X, Cropper.BottomLeft.Y),
            vm.Logger,
            "receipt-crop-confirm",
            new UserFacingMessage("CropReceipt.Failure.Confirm"),
            vm.ReportFailure);
    }
}
