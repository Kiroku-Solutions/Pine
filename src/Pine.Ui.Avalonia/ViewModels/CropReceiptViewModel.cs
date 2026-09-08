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

public partial class CropReceiptViewModel : ObservableRecipient, IRecipient<ReceiptCapturedMessage>
{
    private readonly INavigationService _navigationService;

    /// <summary>
    /// Builds the pages this screen navigates to (TD-28). Named methods rather than a generic
    /// resolve, so a page this head cannot serve is a compile-time surface a test can enumerate
    /// instead of a throw on the user's click.
    /// </summary>
    private readonly IViewModelFactory _viewModels;
    private readonly IImageProcessor _imageProcessor;
    private readonly ILogger<CropReceiptViewModel>? _logger;
    private Stream? _originalStream;

    [ObservableProperty]
    public partial Bitmap? PreviewImage { get; set; }

    [ObservableProperty]
    public partial bool IsProcessing { get; set; }

    /// <summary>
    /// What the user reads when confirming the crop fails.
    /// </summary>
    /// <remarks>
    /// This view model previously had no error surface at all (TD-35). Its own
    /// <c>ConfirmCropAsync</c> catches and calls <c>LogCropFailed</c> -- log only -- so a failed
    /// crop left the user on the crop screen with the button pressed and nothing happening, and the
    /// code-behind that called it was <c>async void</c> with no <c>try</c> at all.
    /// </remarks>
    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    /// <summary>Creates the crop screen's view model.</summary>
    /// <param name="navigationService">Where the confirmed crop navigates to.</param>
    /// <param name="imageProcessor">The perspective-crop and OCR-enhance pipeline.</param>
    /// <param name="logger">
    /// Where a failed crop is recorded (TD-14). Optional so the designer and the existing tests can
    /// construct this view model directly; the container injects a real one.
    /// </param>
    public CropReceiptViewModel(
        INavigationService navigationService,
        IViewModelFactory viewModels,
        IImageProcessor imageProcessor,
        ILogger<CropReceiptViewModel>? logger = null)
    {
        _navigationService = navigationService;
        _viewModels = viewModels;
        _imageProcessor = imageProcessor;
        _logger = logger;
    }

    /// <summary>
    /// Takes delivery of the frame the live scanner just captured.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHAT THIS REPLACED CAPTURED <c>this</c> (TD-34). The registration lambda read
    /// <c>_originalStream</c> and wrote <c>PreviewImage</c>, so the messenger held the delegate --
    /// and through it this view model -- strongly, for the life of the process. Every visit to the
    /// crop screen left another immortal instance subscribed to <c>ReceiptCapturedMessage</c>. A
    /// method on an <see cref="ObservableRecipient"/> cannot make that mistake: the toolkit
    /// registers the interface, not a closure.
    /// </para>
    /// <para>
    /// The body is unchanged and is deliberately synchronous. The decode is the only work, and the
    /// sender (<c>LiveScannerViewModel</c>) is already on the UI thread when it sends.
    /// </para>
    /// </remarks>
    /// <param name="message">The captured frame, and optionally an OCR-enhanced copy of it.</param>
    public void Receive(ReceiptCapturedMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        _originalStream = message.ImageStream;
        _originalStream.Position = 0;
        PreviewImage = new Bitmap(_originalStream);
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
            _navigationService.NavigateTo(_viewModels.CreateScanReceipt());
            
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
        _navigationService.NavigateTo(_viewModels.CreateLiveScanner());
        WeakReferenceMessenger.Default.Send(new RetakeRequestedMessage());
    }
}
