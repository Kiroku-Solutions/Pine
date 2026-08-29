using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Pine.Core.Ai;
using Pine.Ui.Avalonia.Services;
using Pine.Ui.Avalonia.ViewModels.Messages;

namespace Pine.Ui.Avalonia.ViewModels;

public partial class CropReceiptViewModel : ObservableObject
{
    private readonly INavigationService _navigationService;
    private readonly IImageProcessor _imageProcessor;
    private readonly ILogger<CropReceiptViewModel>? _logger;
    private Stream? _originalStream;

    [ObservableProperty]
    public partial Bitmap? PreviewImage { get; set; }

    [ObservableProperty]
    public partial bool IsProcessing { get; set; }

    /// <summary>Creates the crop screen's view model.</summary>
    /// <param name="navigationService">Where the confirmed crop navigates to.</param>
    /// <param name="imageProcessor">The perspective-crop and OCR-enhance pipeline.</param>
    /// <param name="logger">
    /// Where a failed crop is recorded (TD-14). Optional so the designer and the existing tests can
    /// construct this view model directly; the container injects a real one.
    /// </param>
    public CropReceiptViewModel(
        INavigationService navigationService,
        IImageProcessor imageProcessor,
        ILogger<CropReceiptViewModel>? logger = null)
    {
        _navigationService = navigationService;
        _imageProcessor = imageProcessor;
        _logger = logger;

        WeakReferenceMessenger.Default.Register<ReceiptCapturedMessage>(this, (r, m) =>
        {
            _originalStream = m.ImageStream;
            _originalStream.Position = 0;
            PreviewImage = new Bitmap(_originalStream);
        });
    }

    public async Task ConfirmCropAsync(double tlX, double tlY, double trX, double trY, double brX, double brY, double blX, double blY)
    {
        if (_originalStream == null || PreviewImage == null) return;

        IsProcessing = true;

        try
        {
            // The points from the UI are normalized (0.0 to 1.0). 
            // We need to multiply by the original image dimensions for OpenCV.
            var imageWidth = PreviewImage.Size.Width;
            var imageHeight = PreviewImage.Size.Height;

            var quad = new Quadrilateral(
                new Point2D((float)(tlX * imageWidth), (float)(tlY * imageHeight)),
                new Point2D((float)(trX * imageWidth), (float)(trY * imageHeight)),
                new Point2D((float)(brX * imageWidth), (float)(brY * imageHeight)),
                new Point2D((float)(blX * imageWidth), (float)(blY * imageHeight))
            );

            // 1. Perspective Crop
            var croppedStream = await _imageProcessor.PerspectiveCropAsync(_originalStream, quad);

            // 2. Enhance for OCR
            var enhancedStream = await _imageProcessor.EnhanceForOcrAsync(croppedStream);

            // Navigate to ScanReceiptViewModel and send the modified stream
            _navigationService.NavigateTo<ScanReceiptViewModel>();
            
            // We send the enhanced stream for OCR, and maybe the cropped stream for UI display
            
            var croppedMem = croppedStream as MemoryStream ?? new MemoryStream();
            if (croppedMem != croppedStream)
            {
                croppedStream.Position = 0;
                croppedStream.CopyTo(croppedMem);
                croppedMem.Position = 0;
            }
            
            var enhancedMem = enhancedStream as MemoryStream ?? new MemoryStream();
            if (enhancedMem != enhancedStream)
            {
                enhancedStream.Position = 0;
                enhancedStream.CopyTo(enhancedMem);
                enhancedMem.Position = 0;
            }

            WeakReferenceMessenger.Default.Send(new ReceiptCapturedMessage(croppedMem, enhancedMem));
        }
        catch (Exception ex)
        {
            // WAS: Console.WriteLine, from a WinExe with no console attached (TD-14). The user is
            // left on the crop screen with nothing captured, so this is user-affecting: error.
            LogCropFailed(_logger, ex);
        }
        finally
        {
            IsProcessing = false;
        }
    }

    // Source-generated, because CA1848 is an error in this repository. The wrapper takes a nullable
    // logger so the designer and the existing tests can still construct this view model directly.
    [LoggerMessage(
        EventId = 1,
        EventName = "receipt.crop_failed",
        Level = LogLevel.Error,
        Message = "Perspective crop of the captured receipt failed.")]
    private static partial void LogCropFailedCore(ILogger logger, Exception exception);

    private static void LogCropFailed(ILogger? logger, Exception exception)
    {
        if (logger is not null)
        {
            LogCropFailedCore(logger, exception);
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _navigationService.NavigateTo<LiveScannerViewModel>();
        WeakReferenceMessenger.Default.Send(new RetakeRequestedMessage());
    }
}
