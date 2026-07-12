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

namespace Pine.Ui.Avalonia.ViewModels;

/// <summary>
/// Drives a real-time webcam feed, detects receipts via YOLO,
/// then hands the captured frame to ScanReceiptViewModel for OCR.
/// </summary>
public record CameraOption(int Index, string Name);

public partial class LiveScannerViewModel : ObservableObject, IDisposable
{
    private readonly IAIProvider _aiProvider;
    private readonly INavigationService _navigationService;
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

    public LiveScannerViewModel(IAIProvider aiProvider, INavigationService navigationService)
    {
        _aiProvider = aiProvider;
        _navigationService = navigationService;

        // Populate mock cameras (OpenCV index based)
        for (int i = 0; i < 4; i++)
        {
            Cameras.Add(new CameraOption(i, $"Camera {i}"));
        }
        SelectedCamera = Cameras[0];

        WeakReferenceMessenger.Default.Register<RetakeRequestedMessage>(this, (r, m) =>
        {
            StartScanning();
        });
    }

    [RelayCommand]
    private void StartScanning()
    {
        if (IsScanning || _isDisposed) return;

        _cts = new CancellationTokenSource();
        IsScanning = true;
        StatusMessage = "Opening camera...";

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
        StatusMessage = "Camera stopped";
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
                    PostToUI(() => StatusMessage = "Manual capture! Extracting text...");
                    _ = CaptureAndNavigateAsync(frame);
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
                PostToUI(() => StatusMessage = "Failed to open camera.");
                return;
            }

            // Store for shared access
            lock (_captureLock) { _capture = localCapture; }

            PostToUI(() => StatusMessage = "Looking for receipt...");

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
                            PostToUI(() => StatusMessage = $"Receipt detected! Hold still... ({_consecutiveDetections}/5)");
                            
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
                                PostToUI(() => StatusMessage = $"⚠️ {result.StatusMessage}");
                            }
                            else
                            {
                                PostToUI(() => StatusMessage = $"Scanning... (Max Conf: {result.Confidence:P1})");
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
            PostToUI(() => StatusMessage = $"Camera error: {ex.Message}");
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
            PostToUI(() => StatusMessage = $"AI Error: {ex.Message}");
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
            _navigationService.NavigateTo<ScanReceiptViewModel>();
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

            // Navigate first so ScanReceiptViewModel is created and registers the message handler
            _navigationService.NavigateTo<ScanReceiptViewModel>();

            // Deliver the already cropped image
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

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        StopScanning();
        GC.SuppressFinalize(this);
    }
}
