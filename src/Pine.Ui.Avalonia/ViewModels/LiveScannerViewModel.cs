using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using OpenCvSharp;
using Pine.Core.Ai;
using Pine.Ui.Avalonia.Services;
using Pine.Ui.Avalonia.ViewModels.Messages;
using Microsoft.Extensions.Logging;
using Pine.Ui.Avalonia.Diagnostics;
using Pine.Ui.Avalonia.Localization;

namespace Pine.Ui.Avalonia.ViewModels;

/// <summary>
/// Drives a real-time webcam feed, detects receipts via YOLO,
/// then hands the captured frame to ScanReceiptViewModel for OCR.
/// </summary>
public record CameraOption(int Index, string Name);

public partial class LiveScannerViewModel : ObservableRecipient, IRecipient<RetakeRequestedMessage>, IDisposable
{
    private readonly IAIProvider _aiProvider;
    private readonly LocalizedMessageSlot _status;
    /// <summary>
    /// The shell this page is shown in (TD-121). The crop page is created with it too, so the
    /// capture flow stays beside the sidebar instead of replacing it.
    /// </summary>
    private readonly IShellNavigator _navigationService;

    /// <summary>
    /// Builds the pages this screen navigates to (TD-28). Named methods rather than a generic
    /// resolve, so a page this head cannot serve is a compile-time surface a test can enumerate
    /// instead of a throw on the user's click.
    /// </summary>
    private readonly IViewModelFactory _viewModels;
    private readonly ILogger<LiveScannerViewModel>? _logger;
    private readonly object _captureLock = new();
    private int _consecutiveDetections;
    private VideoCapture? _capture;
    private CancellationTokenSource? _cts;
    private volatile bool _isDisposed;

    public System.Collections.ObjectModel.ObservableCollection<CameraOption> Cameras { get; } = new();

    [ObservableProperty]
    public partial CameraOption? SelectedCamera { get; set; }

    [ObservableProperty]
    public partial WriteableBitmap? LiveFeed { get; set; }

    [ObservableProperty]
    public partial bool IsScanning { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "Press Start to activate camera";

    public LiveScannerViewModel(
        IAIProvider aiProvider,
        IShellNavigator navigationService,
        IViewModelFactory viewModels,
        ILocalizer localizer,
        ILogger<LiveScannerViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        _aiProvider = aiProvider;
        _navigationService = navigationService;
        _viewModels = viewModels;
        _status = new LocalizedMessageSlot(localizer, text => PostToUI(() => StatusMessage = text ?? string.Empty));
        _logger = logger;

        // Populate mock cameras (OpenCV index based)
        for (int i = 0; i < 4; i++)
        {
            Cameras.Add(new CameraOption(i, $"Camera {i}"));
        }
        SelectedCamera = Cameras[0];
    }

    /// <summary>
    /// The user rejected the photo on the crop or scan screen and wants another go.
    /// </summary>
    /// <remarks>
    /// THE OLD REGISTRATION CAPTURED <c>this</c> BY CALLING <see cref="StartScanning"/> (TD-34),
    /// which handed the process-wide messenger a strong reference to a view model that owns a
    /// CAMERA. Every visit to this screen left one behind, subscribed and holding a
    /// <c>VideoCapture</c> that only <see cref="Dispose"/> releases. Registration now follows
    /// <c>IsActive</c>, and navigation both unregisters and disposes.
    /// </remarks>
    /// <param name="message">The retake request. It carries no payload.</param>
    public void Receive(RetakeRequestedMessage message) => StartScanning();

    [RelayCommand]
    private void StartScanning()
    {
        if (IsScanning || _isDisposed) return;

        _cts = new CancellationTokenSource();
        IsScanning = true;
        _status.Set("LiveScanner.Status.OpeningCamera");

        // Fire-and-forget on the thread pool; errors are caught internally
        _ = Task.Run(() => CaptureLoopAsync(_cts.Token));
    }

    [RelayCommand]
    private void StopScanning()
    {
        // Cancel the background loop first
        _cts?.Cancel();

        // Release the camera under lock so the background thread doesn't race
        lock (_captureLock)
        {
            _capture?.Release();
            _capture?.Dispose();
            _capture = null;
        }

        _cts?.Dispose();
        _cts = null;
        IsScanning = false;
        _status.Set("LiveScanner.Status.CameraStopped");
    }

    [RelayCommand]
    private void CaptureManually()
    {
        if (!IsScanning || _isDisposed) return;
        
        // Grab the last frame from the feed
        lock (_captureLock)
        {
            if (_capture != null && _capture.IsOpened())
            {
                using var frame = new Mat();
                if (_capture.Read(frame) && !frame.Empty())
                {
                    _status.Set("LiveScanner.Status.ManualCapture");

                    // OBSERVED, AND THE DISCARD HERE WAS THE ODD ONE OUT (TD-35).
                    // CaptureAndNavigateAsync is NOT `async` -- it returns Task.CompletedTask -- so
                    // a throw from frame.ImEncode(".png") inside it propagated SYNCHRONOUSLY out of
                    // this relay command, while this thread still held _captureLock. The
                    // Func<Task> overload is the one that matters: it catches a synchronous throw
                    // from the factory as well as a faulted task, which a plain discard could not.
                    ObservedTask.Run(
                        () => CaptureAndNavigateAsync(frame),
                        _logger,
                        "camera-manual-capture",
                        "That frame could not be captured. Try again.",
                        message => PostToUI(() => StatusMessage = message));
                }
            }
        }
    }

    partial void OnSelectedCameraChanged(CameraOption? value)
    {
        if (IsScanning)
        {
            // Restart the camera feed with the new camera
            StopScanning();
            StartScanning();
        }
    }

    // ────────────────────────────────────────────────────────────
    //  Background capture loop
    // ────────────────────────────────────────────────────────────

    private async Task CaptureLoopAsync(CancellationToken token)
    {
        VideoCapture? localCapture = null;
        try
        {
            int cameraIndex = SelectedCamera?.Index ?? 0;
            
            // Use DirectShow for much faster camera initialization on Windows
            localCapture = new VideoCapture(cameraIndex, VideoCaptureAPIs.DSHOW);
            if (!localCapture.IsOpened())
            {
                // Fallback to default if DSHOW fails
                localCapture = new VideoCapture(cameraIndex);
            }
            
            if (!localCapture.IsOpened())
            {
                _status.Set("LiveScanner.Status.CameraFailed");
                return;
            }

            // Store for shared access
            lock (_captureLock) { _capture = localCapture; }

            _status.Set("LiveScanner.Status.LookingForReceipt");

            using var frame = new Mat();
            int frameCounter = 0;

            while (!token.IsCancellationRequested)
            {
                bool readOk;
                lock (_captureLock)
                {
                    if (_capture == null || !_capture.IsOpened()) break;
                    readOk = _capture.Read(frame);
                }

                if (!readOk || frame.Empty())
                {
                    await Task.Delay(50, token);
                    continue;
                }

                // Render every frame to the UI
                RenderFrameToUI(frame);

                // Run AI detection every 10 frames (~300 ms) to avoid overwhelming the CPU
                frameCounter++;
                if (frameCounter % 10 == 0)
                {
                    var result = await DetectReceiptInFrameAsync(frame, token);
                    if (result != null)
                    {
                        if (result.PassedFilters && result.CroppedImageStream != null)
                        {
                            _consecutiveDetections++;
                            _status.Set("LiveScanner.Status.HoldStillFormat", _consecutiveDetections);
                            
                            if (_consecutiveDetections >= 5)
                            {
                                await CaptureAndNavigateCroppedAsync(result.CroppedImageStream, result.OcrImageStream);
                                return; // Exit the loop cleanly
                            }
                        }
                        else
                        {
                            _consecutiveDetections = 0; // Reset if confidence drops or filters fail
                            
                            // If confidence is high but filters failed, show the specific reason to the user
                            if (result.Confidence > 0.85f && !string.IsNullOrEmpty(result.StatusMessage))
                            {
                                _status.Set("LiveScanner.Status.ProviderWarningFormat", result.StatusMessage);
                            }
                            else
                            {
                                _status.Set("LiveScanner.Status.ScanningFormat", result.Confidence);
                            }
                        }
                    }
                }

                // Cap at ~30 fps
                await Task.Delay(33, token);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation — nothing to log
        }
        catch (Exception ex)
        {
            // WAS: $"Camera error: {ex.Message}" (TD-24). OpenCV and the DirectShow backend report
            // failures as native strings naming device paths and library entry points; none of it is
            // actionable and all of it is now in the log instead.
            var message = UserFacingError.Report(
                _logger,
                ex,
                "camera-capture-loop",
                "The camera could not be read. Check that it is connected and not in use by another application, then try again.");
            PostToUI(() => StatusMessage = message);
        }
        finally
        {
            // Guarantee camera is released even on unexpected exit
            lock (_captureLock)
            {
                if (_capture != null)
                {
                    _capture.Release();
                    _capture.Dispose();
                    _capture = null;
                }
            }
            PostToUI(() => IsScanning = false);
        }
    }

    // ────────────────────────────────────────────────────────────
    //  Frame → Avalonia WriteableBitmap (thread-safe)
    // ────────────────────────────────────────────────────────────

    /// <summary>
    /// Converts an OpenCV Mat to an Avalonia WriteableBitmap.
    /// Uses the constructor overload that copies from an IntPtr,
    /// so all pixel data is copied BEFORE we post to the UI thread.
    /// This avoids the use-after-free that occurs when a `using` Mat
    /// is disposed before the Dispatcher callback executes.
    /// </summary>
    private void RenderFrameToUI(Mat frame)
    {
        if (_isDisposed) return;

        // Convert BGR → BGRA (Avalonia expects 4-channel premultiplied alpha)
        using var bgra = new Mat();
        Cv2.CvtColor(frame, bgra, ColorConversionCodes.BGR2BGRA);

        int width = bgra.Width;
        int height = bgra.Height;
        int stride = (int)(bgra.Step());

        // Build the bitmap on this thread — the constructor copies the data immediately
        var bitmap = new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);

        using (var buf = bitmap.Lock())
        {
            unsafe
            {
                Buffer.MemoryCopy(
                    (void*)bgra.Data, 
                    (void*)buf.Address, 
                    buf.RowBytes * height, 
                    stride * height);
            }
        }

        // Now post the already-populated bitmap to the UI thread
        PostToUI(() =>
        {
            if (_isDisposed) return;
            LiveFeed = bitmap;
        });
    }

    // ────────────────────────────────────────────────────────────
    //  AI detection helper
    // ────────────────────────────────────────────────────────────

    private async Task<Pine.Core.Ai.ReceiptAnalysisResult?> DetectReceiptInFrameAsync(Mat frame, CancellationToken token)
    {
        try
        {
            byte[] jpg = frame.ImEncode(".jpg");
            using var ms = new MemoryStream(jpg);
            return await _aiProvider.AnalyzeAndCropReceiptAsync(ms, token);
        }
        catch (Exception ex)
        {
            // If detection fails for any reason, keep scanning
            // WAS: $"AI Error: {ex.Message}" (TD-24). A per-frame detection failure is not the
            // user's problem to diagnose, and the frame loop keeps running, so the sentence says what
            // it costs them rather than which model threw.
            var message = UserFacingError.Report(
                _logger,
                ex,
                "camera-detect-receipt",
                "Pine could not find a receipt in the camera image. Move the receipt into frame, or take the photo manually.");
            PostToUI(() => StatusMessage = message);
            return null;
        }
    }

    // ────────────────────────────────────────────────────────────
    //  Capture & Navigate
    // ────────────────────────────────────────────────────────────

    private Task CaptureAndNavigateAsync(Mat frame)
    {
        byte[] png = frame.ImEncode(".png");
        var imageStream = new MemoryStream(png);

        PostToUI(() =>
        {
            StopScanning();

            // Posted, so it runs later -- possibly after the user left this page (TD-121). See the
            // same guard in CaptureAndNavigateCroppedAsync.
            if (_isDisposed || !IsActive)
            {
                imageStream.Dispose();
                return;
            }

            _navigationService.NavigateTo(_viewModels.CreateCropReceipt(_navigationService));
            WeakReferenceMessenger.Default.Send(new ReceiptCapturedMessage(imageStream));
        });

        return Task.CompletedTask;
    }

    private Task CaptureAndNavigateCroppedAsync(Stream croppedImageStream, Stream? ocrImageStream = null)
    {
        var memStream = croppedImageStream as MemoryStream;
        if (memStream == null)
        {
            memStream = new MemoryStream();
            croppedImageStream.CopyTo(memStream);
            memStream.Position = 0;
        }

        MemoryStream? ocrMemStream = null;
        if (ocrImageStream != null)
        {
            ocrMemStream = ocrImageStream as MemoryStream;
            if (ocrMemStream == null)
            {
                ocrMemStream = new MemoryStream();
                ocrImageStream.CopyTo(ocrMemStream);
                ocrMemStream.Position = 0;
            }
        }

        PostToUI(() =>
        {
            StopScanning();

            // THE FRAME WAS POSTED, SO THIS RUNS LATER (TD-121). By then the user may have clicked
            // another page, or the vault may have locked. Navigating anyway would pull the user back
            // onto the crop screen from wherever they went -- it sits beside the sidebar now, so
            // leaving mid-capture is a real gesture -- and a disposed scanner must not act at all.
            if (_isDisposed || !IsActive)
            {
                memStream.Dispose();
                ocrMemStream?.Dispose();
                return;
            }

            // Navigate first so CropReceiptViewModel is created and registers the message handler
            _navigationService.NavigateTo(_viewModels.CreateCropReceipt(_navigationService));

            // Deliver the already cropped image (user can adjust further if needed)
            WeakReferenceMessenger.Default.Send(new ReceiptCapturedMessage(memStream, ocrMemStream));
        });

        return Task.CompletedTask;
    }

    // ────────────────────────────────────────────────────────────
    //  Helpers
    // ────────────────────────────────────────────────────────────

    private static void PostToUI(Action action)
    {
        global::Avalonia.Threading.Dispatcher.UIThread.Post(action);
    }

    /// <summary>
    /// Releases the camera. Idempotent -- <c>_isDisposed</c> makes a second call a no-op, which is
    /// what lets navigation and <c>LiveScannerView.OnUnloaded</c> both call it safely.
    /// </summary>
    public void Dispose()
    {
        _status.Dispose();
        if (_isDisposed) return;
        _isDisposed = true;

        // TD-34: unregister here as well as from the navigator. Dispose is reachable from
        // LiveScannerView.OnUnloaded, which can fire without any navigation having happened, and a
        // disposed scanner that still receives RetakeRequestedMessage would call StartScanning on
        // a camera it has already put down. StartScanning does check _isDisposed, so this is
        // defence in depth rather than the only guard -- but it is the one that stops the messenger
        // holding a dead recipient at all.
        IsActive = false;

        StopScanning();
        GC.SuppressFinalize(this);
    }
}
