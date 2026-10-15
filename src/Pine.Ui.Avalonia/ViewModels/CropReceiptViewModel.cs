using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Pine.Core.Ai;
using Pine.Core.Common;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.Services;
using Pine.Ui.Avalonia.ViewModels.Messages;
using System.Collections.Generic;

namespace Pine.Ui.Avalonia.ViewModels;

public partial class CropReceiptViewModel : ObservableRecipient, IRecipient<ReceiptCapturedMessage>, IDisposable
{
    private readonly LocalizedMessageSlot _error;

    /// <summary>
    /// The shell this page is shown in (TD-121). It was the window's navigator, so confirming or
    /// cancelling a crop replaced the shell; it is now the shell that created this page, and it is
    /// handed on to the page that comes next.
    /// </summary>
    private readonly IShellNavigator _navigationService;

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
    public partial string? ErrorMessage { get; private set; }

    /// <summary>
    /// The view model's logger, for the code-behind that reports a failure through it (TD-127). A view is built by the XAML loader with no container reach, so it borrows the logger of the view model it is the face of.
    /// </summary>
    internal Microsoft.Extensions.Logging.ILogger? Logger => _logger;

    /// <summary>
    /// Shows <paramref name="messages"/> as this page's error, in the active language, and keeps them
    /// in step with a language change. For the view's code-behind, which has no localizer (TD-139).
    /// </summary>
    /// <param name="messages">What <c>UserFacingError.Report</c> or <c>ObservedTask</c> produced.</param>
    public void ReportFailure(IReadOnlyList<UserFacingMessage> messages) => _error.Set(messages);


    /// <summary>Creates the crop screen's view model.</summary>
    /// <param name="navigationService">The shell this page is shown in; see <c>IViewModelFactory.CreateCropReceipt</c>.</param>
    /// <param name="imageProcessor">The perspective-crop and OCR-enhance pipeline.</param>
    /// <param name="logger">
    /// Where a failed crop is recorded (TD-14). Optional so the designer and the existing tests can
    /// construct this view model directly; the container injects a real one.
    /// </param>
    public CropReceiptViewModel(
        IShellNavigator navigationService,
        IViewModelFactory viewModels,
        IImageProcessor imageProcessor,
        ILogger<CropReceiptViewModel>? logger = null,
        ILocalizer? localizer = null)
    {
        _navigationService = navigationService;
        _viewModels = viewModels;
        _imageProcessor = imageProcessor;
        _logger = logger;
        _error = new LocalizedMessageSlot(localizer ?? new Localizer(), text => ErrorMessage = text);
    }

    /// <summary>Releases the localized message slot.</summary>
    /// <remarks>
    /// CA1001, for the reason every page that owns a slot gives: it subscribes to the process-lifetime
    /// localizer. <c>PageActivation.Deactivate</c> disposes a page that implements this.
    /// </remarks>
    public void Dispose()
    {
        // The parked original is ours (TD-126): it arrived in a message and nobody else will release it.
        _originalStream?.Dispose();
        _originalStream = null;
        _error.Dispose();
        GC.SuppressFinalize(this);
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

        // OWNERSHIP ARRIVED WITH THE MESSAGE (TD-126): this page releases a frame it replaces, one it cannot
        // decode, and the one it still holds when it is disposed.
        _originalStream?.Dispose();
        _originalStream = message.ImageStream;

        try
        {
            _originalStream.Position = 0;
            PreviewImage = new Bitmap(_originalStream);
        }
        catch
        {
            _originalStream.Dispose();
            _originalStream = null;
            throw;
        }
    }

    public async Task ConfirmCropAsync(double tlX, double tlY, double trX, double trY, double brX, double brY, double blX, double blY)
    {
        if (_originalStream == null || PreviewImage == null) return;

        IsProcessing = true;

        // What this method built and has not yet handed to a recipient. Whatever is still here when it
        // leaves is released here; once a message carries a stream it is the recipient's (TD-126).
        Stream? croppedStream = null;
        Stream? enhancedStream = null;
        MemoryStream? croppedMem = null;
        MemoryStream? enhancedMem = null;
        var sent = false;

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
            croppedStream = await _imageProcessor.PerspectiveCropAsync(_originalStream, quad);

            // 2. Enhance for OCR
            enhancedStream = await _imageProcessor.EnhanceForOcrAsync(croppedStream);

            // THE USER MAY HAVE LEFT WHILE THAT RAN (TD-121). The crop is shown beside the sidebar
            // now, so a click on Budgets during the two awaits above is a real possibility -- and
            // navigating here unconditionally would drag the user back onto the review page they had
            // walked away from. A page that is no longer current has no business moving the shell.
            if (!IsActive)
            {
                // Nobody will read these now; the live scanner's matching guard releases its streams
                // the same way.
                return;
            }

            // Navigate to ScanReceiptViewModel and send the modified stream
            _navigationService.NavigateTo(_viewModels.CreateScanReceipt(_navigationService));
            
            // We send the enhanced stream for OCR, and maybe the cropped stream for UI display
            
            croppedMem = croppedStream as MemoryStream ?? new MemoryStream();
            if (croppedMem != croppedStream)
            {
                croppedStream.Position = 0;
                croppedStream.CopyTo(croppedMem);
                croppedMem.Position = 0;
            }
            
            enhancedMem = enhancedStream as MemoryStream ?? new MemoryStream();
            if (enhancedMem != enhancedStream)
            {
                enhancedStream.Position = 0;
                enhancedStream.CopyTo(enhancedMem);
                enhancedMem.Position = 0;
            }

            sent = true;
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
            if (sent)
            {
                // Handed off: the copies belong to the recipient. Only a processor's own stream that was
                // COPIED into a MemoryStream above is still ours to release.
                if (!ReferenceEquals(croppedStream, croppedMem))
                {
                    croppedStream?.Dispose();
                }

                if (!ReferenceEquals(enhancedStream, enhancedMem))
                {
                    enhancedStream?.Dispose();
                }
            }
            else
            {
                croppedStream?.Dispose();
                enhancedStream?.Dispose();
                croppedMem?.Dispose();
                enhancedMem?.Dispose();
            }

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
        _navigationService.NavigateTo(_viewModels.CreateLiveScanner(_navigationService));
        WeakReferenceMessenger.Default.Send(new RetakeRequestedMessage());
    }
}
