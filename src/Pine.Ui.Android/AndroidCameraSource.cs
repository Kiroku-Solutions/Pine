using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Android;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Hardware.Camera2;
using Android.Hardware.Camera2.Params;
using Android.Media;
using Android.OS;
using Android.Views;
using Pine.Core.Ai;
using SkiaSharp;

namespace Pine.Ui.Android;

/// <summary>
/// The Android camera behind <see cref="ICameraSource"/> (docs/65 phase D): Camera2, back lens, a YUV preview stream
/// read into memory. No CameraX or other package is needed, so nothing new enters the dependency graph.
/// </summary>
/// <remarks>
/// <para>
/// PRIVACY. The CAMERA permission is requested the first time a person taps Take Photo, never at start-up. Frames are
/// converted to BGRA in managed memory, handed to the caller and disposed; nothing is written to disk or sent anywhere
/// (the head has no INTERNET permission). What the person decides to save is the captured receipt image, by saving the
/// transaction, exactly as for a picked file.
/// </para>
/// <para>
/// RESOLUTION. The stream is the largest YUV size not above about 2 megapixels (1920x1080 on most devices): a viewfinder
/// plus a frame that is enough for the OCR engine, with a conversion cheap enough to run several times a second on a
/// mid-range tablet. A full-resolution JPEG still capture would read small print better and is the obvious next step.
/// </para>
/// <para>
/// UNVERIFIED ON HARDWARE as of this commit: it builds and installs, but no one has yet driven the camera on a device.
/// </para>
/// </remarks>
internal sealed class AndroidCameraSource : ICameraSource
{
    private const int PermissionRequestCode = 4021;
    private const int MaxPixels = 2_100_000;
    private static readonly object PermissionLock = new();
    private static TaskCompletionSource<bool>? _permissionRequest;

    /// <inheritdoc />
    /// <remarks>Blocks while the permission prompt is up and while the camera opens; the caller runs it off the UI thread.</remarks>
    public ICameraSession? OpenDefault()
    {
        var activity = MainActivity.Current;
        if (activity is null)
        {
            return null;
        }

        if (!EnsurePermission(activity))
        {
            return null;
        }

        try
        {
            return AndroidCameraSession.Open(activity, MaxPixels);
        }
        catch (Exception ex) when (ex is Java.Lang.Exception or InvalidOperationException or TimeoutException or UnauthorizedAccessException)
        {
            global::Android.Util.Log.Warn("PineCamera", $"open failed: {ex.GetType().Name}");
            return null;
        }
    }

    internal static void OnPermissionResult(int requestCode, Permission[] grantResults)
    {
        if (requestCode != PermissionRequestCode)
        {
            return;
        }

        lock (PermissionLock)
        {
            _permissionRequest?.TrySetResult(grantResults.Length > 0 && grantResults[0] == Permission.Granted);
        }
    }

    private static bool EnsurePermission(MainActivity activity)
    {
        if (activity.CheckSelfPermission(Manifest.Permission.Camera) == Permission.Granted)
        {
            return true;
        }

        TaskCompletionSource<bool> request;
        lock (PermissionLock)
        {
            request = _permissionRequest = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        activity.RunOnUiThread(() => activity.RequestPermissions([Manifest.Permission.Camera], PermissionRequestCode));
        return request.Task.Wait(TimeSpan.FromMinutes(2)) && request.Task.Result;
    }
}

internal sealed class AndroidCameraSession : ICameraSession
{
    private readonly MainActivity _activity;
    private readonly int _sensorOrientation;
    private readonly HandlerThread _thread;
    private readonly ImageReader _reader;
    private CameraDevice? _device;
    private CameraCaptureSession? _session;
    private bool _disposed;
    private (int Width, int Height)? _stillSize;
    private ImageReader? _stillReader;
    private Handler? _handler;
    private byte[] _yBuffer = [];
    private byte[] _uBuffer = [];
    private byte[] _vBuffer = [];
    private int _frames;
    private readonly System.Collections.Concurrent.ConcurrentBag<byte[]> _pool = [];
    private long _convertTicks;
    private long _copyTicks;

    private AndroidCameraSession(MainActivity activity, int sensorOrientation, HandlerThread thread, ImageReader reader)
    {
        _activity = activity;
        _sensorOrientation = sensorOrientation;
        _thread = thread;
        _reader = reader;
    }

    internal static AndroidCameraSession Open(MainActivity activity, int maxPixels)
    {
        var manager = (CameraManager?)activity.GetSystemService(Context.CameraService)
            ?? throw new InvalidOperationException("no camera service");

        string? id = null;
        CameraCharacteristics? characteristics = null;
        foreach (var candidate in manager.GetCameraIdList())
        {
            var c = manager.GetCameraCharacteristics(candidate);
            var facing = (Java.Lang.Integer?)c.Get(CameraCharacteristics.LensFacing);
            if (facing is not null && facing.IntValue() == (int)LensFacing.Back)
            {
                id = candidate;
                characteristics = c;
                break;
            }
        }

        if (id is null)
        {
            id = manager.GetCameraIdList().FirstOrDefault() ?? throw new InvalidOperationException("no camera");
            characteristics = manager.GetCameraCharacteristics(id);
        }

        var map = (StreamConfigurationMap?)characteristics!.Get(CameraCharacteristics.ScalerStreamConfigurationMap)
            ?? throw new InvalidOperationException("no stream map");
        var size = (map.GetOutputSizes((int)ImageFormatType.Yuv420888) ?? [])
            .Where(s => (long)s.Width * s.Height <= maxPixels)
            .OrderByDescending(s => (long)s.Width * s.Height)
            .FirstOrDefault() ?? throw new InvalidOperationException("no yuv size");

        var sensor = ((Java.Lang.Integer?)characteristics.Get(CameraCharacteristics.SensorOrientation))?.IntValue() ?? 90;

        // The still: the largest JPEG the camera offers up to ~13 MP (memory on a 4 GB device); null when it offers none.
        var still = (map.GetOutputSizes((int)ImageFormatType.Jpeg) ?? [])
            .Where(s => (long)s.Width * s.Height <= 13_000_000)
            .OrderByDescending(s => (long)s.Width * s.Height)
            .FirstOrDefault();

        var thread = new HandlerThread("pine-camera");
        thread.Start();
        var handler = new Handler(thread.Looper!);
        var reader = ImageReader.NewInstance(size.Width, size.Height, ImageFormatType.Yuv420888, 3)!;
        var session = new AndroidCameraSession(activity, sensor, thread, reader);
        session._stillSize = still is null ? null : (still.Width, still.Height);
        try
        {
            session.Start(manager, id, handler);
            return session;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    private void Start(CameraManager manager, string id, Handler handler)
    {
        var opened = new TaskCompletionSource<CameraDevice>(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.OpenCamera(id, new DeviceCallback(opened), handler);
        if (!opened.Task.Wait(TimeSpan.FromSeconds(8)))
        {
            throw new TimeoutException("camera open timed out");
        }

        _device = opened.Task.Result;

        _handler = handler;
        var surface = _reader.Surface ?? throw new InvalidOperationException("no surface");

        // Preview + a full-size JPEG still in one session. Not every device guarantees that combination, so a failure falls back to
        // the preview alone (stills then come from the viewfinder frame).
        if (_stillSize is { } size)
        {
            _stillReader = ImageReader.NewInstance(size.Width, size.Height, ImageFormatType.Jpeg, 1);
            try
            {
                _session = ConfigureSession([surface, _stillReader!.Surface!], handler);
            }
            catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or Java.Lang.Exception)
            {
                global::Android.Util.Log.Warn("PineCamera", $"preview+still session refused ({ex.GetType().Name}); preview only");
                _stillReader?.Close();
                _stillReader = null;
            }
        }

        _session ??= ConfigureSession([surface], handler);

        var request = _device.CreateCaptureRequest(CameraTemplate.Preview)
            ?? throw new InvalidOperationException("no request");
        request.AddTarget(surface);
        request.Set(CaptureRequest.ControlAfMode!, (int)ControlAFMode.ContinuousPicture);
        _session.SetRepeatingRequest(request.Build()!, null, handler);
    }

    private CameraCaptureSession ConfigureSession(List<Surface> surfaces, Handler handler)
    {
        var configured = new TaskCompletionSource<CameraCaptureSession>(TaskCreationOptions.RunContinuationsAsynchronously);
#pragma warning disable CA1422, CS0618 // Validate platform compatibility; the list overload is the one that works from API 24 up.
        _device!.CreateCaptureSession(surfaces, new SessionCallback(configured), handler);
#pragma warning restore CA1422, CS0618
        if (!configured.Task.Wait(TimeSpan.FromSeconds(8)))
        {
            throw new TimeoutException("camera session timed out");
        }

        return configured.Task.Result;
    }

    /// <inheritdoc />
    /// <remarks>One still at the camera's own size, JPEG-compressed by the camera with the orientation set so the picture decodes upright.</remarks>
    public byte[]? CaptureStill()
    {
        if (_disposed || _stillReader is null || _device is null || _session is null || _handler is null)
        {
            return null;
        }

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var received = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var listener = new StillListener(received);
        _stillReader.SetOnImageAvailableListener(listener, _handler);
        var request = _device.CreateCaptureRequest(CameraTemplate.StillCapture) ?? throw new InvalidOperationException("no still request");
        request.AddTarget(_stillReader.Surface!);
        request.Set(CaptureRequest.ControlAfMode!, (int)ControlAFMode.ContinuousPicture);
        request.Set(CaptureRequest.JpegOrientation!, UprightRotation());
        _session.Capture(request.Build()!, null, _handler);
        if (!received.Task.Wait(TimeSpan.FromSeconds(6)))
        {
            global::Android.Util.Log.Warn("PineCamera", "still capture timed out; using the viewfinder frame");
            return null;
        }

        var bytes = received.Task.Result;
        global::Android.Util.Log.Info("PineCamera", $"still bytes={bytes.Length} size={_stillSize?.Width}x{_stillSize?.Height} ms={clock.ElapsedMilliseconds}");
        return bytes;
    }

    private int UprightRotation()
    {
#pragma warning disable CA1422
        var display = (_activity.WindowManager?.DefaultDisplay?.Rotation ?? SurfaceOrientation.Rotation0) switch
        {
            SurfaceOrientation.Rotation90 => 90,
            SurfaceOrientation.Rotation180 => 180,
            SurfaceOrientation.Rotation270 => 270,
            _ => 0,
        };
#pragma warning restore CA1422
        return (_sensorOrientation - display + 360) % 360;
    }

    private sealed class StillListener(TaskCompletionSource<byte[]> received) : Java.Lang.Object, ImageReader.IOnImageAvailableListener
    {
        public void OnImageAvailable(ImageReader? reader)
        {
            var image = reader?.AcquireLatestImage();
            if (image is null)
            {
                return;
            }

            try
            {
                var buffer = image.GetPlanes()![0].Buffer!;
                var bytes = new byte[buffer.Remaining()];
                buffer.Get(bytes);
                received.TrySetResult(bytes);
            }
            finally
            {
                image.Close();
                image.Dispose();
            }
        }
    }

    /// <inheritdoc />
    public ICameraFrame? ReadFrame()
    {
        if (_disposed)
        {
            return null;
        }

        var image = _reader.AcquireLatestImage();
        if (image is null)
        {
            return null;
        }

        try
        {
            return Convert(image);
        }
        finally
        {
            // Close(), NOT Dispose(): disposing the Java peer only drops the managed handle, the buffer stays acquired.
            // With maxImages = 2 the third read then fails ("acquired more than maxImages") and the producer times out
            // on dequeueBuffer about 1.5 s after opening: the bug seen on the tablet (acquiredCount: 2).
            image.Close();
            image.Dispose();
        }
    }

    private AndroidCameraFrame Convert(Image image)
    {
        var width = image.Width;
        var height = image.Height;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var planes = image.GetPlanes() ?? throw new InvalidOperationException("no planes");
        var y = Copy(planes[0], ref _yBuffer);
        var u = Copy(planes[1], ref _uBuffer);
        var v = Copy(planes[2], ref _vBuffer);
        var yRow = planes[0].RowStride;
        var uvRow = planes[1].RowStride;
        var uvPixel = planes[1].PixelStride;

        // Clockwise turn that makes the picture upright for the current screen orientation.
#pragma warning disable CA1422 // WindowManager.DefaultDisplay is deprecated from API 30 but is the one available from API 24.
        var display = (_activity.WindowManager?.DefaultDisplay?.Rotation ?? SurfaceOrientation.Rotation0) switch
        {
            SurfaceOrientation.Rotation90 => 90,
            SurfaceOrientation.Rotation180 => 180,
            SurfaceOrientation.Rotation270 => 270,
            _ => 0,
        };
#pragma warning restore CA1422
        var rotation = (_sensorOrientation - display + 360) % 360;

        // With a camera still available the viewfinder and the analysis need only a quarter of the pixels (YOLO letterboxes to 640 anyway),
        // so every second pixel of every second row is converted; the full-size capture is the still. Without one the frame stays full size.
        var step = _stillReader is not null ? 2 : 1;
        var outWidth = (rotation is 90 or 270 ? height : width) / step;
        var outHeight = (rotation is 90 or 270 ? width : height) / step;
        // Frames are 8 MB each; reusing their buffers keeps the garbage collector (and its pauses) out of the viewfinder.
        var needed = outWidth * outHeight * 4;
        if (!_pool.TryTake(out var bgra) || bgra.Length != needed)
        {
            bgra = new byte[needed];
        }

        // Source position of output pixel (ox, oy) is (x0 + ox*dxOx + oy*dxOy, y0 + ox*dyOx + oy*dyOy): the rotation is
        // resolved once here, so the inner loop is straight arithmetic.
        int x0, y0, dxOx, dxOy, dyOx, dyOy;
        switch (rotation)
        {
            case 90: x0 = 0; y0 = height - 1; dxOx = 0; dxOy = 1; dyOx = -1; dyOy = 0; break;
            case 180: x0 = width - 1; y0 = height - 1; dxOx = -1; dxOy = 0; dyOx = 0; dyOy = -1; break;
            case 270: x0 = width - 1; y0 = 0; dxOx = 0; dxOy = -1; dyOx = 1; dyOy = 0; break;
            default: x0 = 0; y0 = 0; dxOx = 1; dxOy = 0; dyOx = 0; dyOy = 1; break;
        }

        dxOx *= step;
        dxOy *= step;
        dyOx *= step;
        dyOy *= step;
        var copyTicks = clock.ElapsedTicks;
        var o = 0;
        for (var oy = 0; oy < outHeight; oy++)
        {
            var sx = x0 + (oy * dxOy);
            var sy = y0 + (oy * dyOy);
            for (var ox = 0; ox < outWidth; ox++)
            {
                var yy = y[(sy * yRow) + sx];
                var ci = ((sy >> 1) * uvRow) + ((sx >> 1) * uvPixel);
                var uu = u[ci] - 128;
                var vv = v[ci] - 128;
                bgra[o] = Clamp(yy + ((454 * uu) >> 8));
                bgra[o + 1] = Clamp(yy - (((88 * uu) + (183 * vv)) >> 8));
                bgra[o + 2] = Clamp(yy + ((359 * vv) >> 8));
                bgra[o + 3] = 255;
                o += 4;
                sx += dxOx;
                sy += dyOx;
            }
        }

        _convertTicks += clock.ElapsedTicks;
        _copyTicks += copyTicks;
        if (++_frames % 60 == 0)
        {
            global::Android.Util.Log.Info("PineCamera", $"frames={_frames} convert_ms_avg={_convertTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency / 60:F1} (of which plane copy {_copyTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency / 60:F1}) step={step} size={outWidth}x{outHeight}");
            _convertTicks = 0;
            _copyTicks = 0;
        }

        return new AndroidCameraFrame(outWidth, outHeight, bgra, buffer => { if (!_disposed) { _pool.Add(buffer); } });
    }

    private static byte Clamp(int value) => (byte)(value < 0 ? 0 : value > 255 ? 255 : value);

    private static byte[] Copy(Image.Plane plane, ref byte[] reuse)
    {
        var buffer = plane.Buffer ?? throw new InvalidOperationException("no plane buffer");
        var length = buffer.Remaining();
        if (reuse.Length < length)
        {
            reuse = new byte[length];
        }

        buffer.Get(reuse, 0, length);
        return reuse;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        TryRelease(() => _session?.StopRepeating());
        TryRelease(() => _session?.Close());
        TryRelease(() => _device?.Close());
        TryRelease(() => _stillReader?.Close());
        TryRelease(() => _reader.Close());
        TryRelease(() => _thread.QuitSafely());
    }

    private static void TryRelease(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is Java.Lang.Exception or InvalidOperationException or ObjectDisposedException)
        {
            // Releasing a camera that is already gone is not a failure.
        }
    }

    private sealed class DeviceCallback(TaskCompletionSource<CameraDevice> opened) : CameraDevice.StateCallback
    {
        public override void OnOpened(CameraDevice camera) => opened.TrySetResult(camera);

        public override void OnDisconnected(CameraDevice camera)
        {
            camera.Close();
            opened.TrySetException(new InvalidOperationException("camera disconnected"));
        }

        public override void OnError(CameraDevice camera, CameraError error)
        {
            camera.Close();
            opened.TrySetException(new InvalidOperationException("camera error " + error));
        }
    }

    private sealed class SessionCallback(TaskCompletionSource<CameraCaptureSession> configured) : CameraCaptureSession.StateCallback
    {
        public override void OnConfigured(CameraCaptureSession session) => configured.TrySetResult(session);

        public override void OnConfigureFailed(CameraCaptureSession session)
            => configured.TrySetException(new InvalidOperationException("camera session failed"));
    }
}

/// <summary>One upright frame as BGRA bytes in managed memory.</summary>
internal sealed class AndroidCameraFrame(int width, int height, byte[] bgra, Action<byte[]>? release = null) : ICameraFrame
{
    private byte[]? _bgra = bgra;

    public int Width { get; } = width;

    public int Height { get; } = height;

    public void CopyBgraTo(Span<byte> destination, int destinationStride)
    {
        var source = _bgra ?? throw new ObjectDisposedException(nameof(AndroidCameraFrame));
        var rowBytes = Width * 4;
        for (var row = 0; row < Height; row++)
        {
            source.AsSpan(row * rowBytes, rowBytes).CopyTo(destination.Slice(row * destinationStride, rowBytes));
        }
    }

    public byte[] EncodeJpeg() => Encode(SKEncodedImageFormat.Jpeg, 92);

    /// <inheritdoc />
    public byte[] EncodeJpeg(int maxSide)
    {
        var longSide = Math.Max(Width, Height);
        if (longSide <= maxSide)
        {
            return EncodeJpeg();
        }

        var source = _bgra ?? throw new ObjectDisposedException(nameof(AndroidCameraFrame));
        var ratio = (double)maxSide / longSide;
        // Wrap the frame's own pixels (no 8 MB copy) for the resize.
        var pin = System.Runtime.InteropServices.GCHandle.Alloc(source, System.Runtime.InteropServices.GCHandleType.Pinned);
        SKBitmap? small;
        try
        {
            using var full = new SKBitmap();
            full.InstallPixels(new SKImageInfo(Width, Height, SKColorType.Bgra8888, SKAlphaType.Opaque), pin.AddrOfPinnedObject(), Width * 4);
            small = full.Resize(
                new SKImageInfo((int)Math.Round(Width * ratio), (int)Math.Round(Height * ratio), SKColorType.Bgra8888, SKAlphaType.Opaque),
                new SKSamplingOptions(SKFilterMode.Linear))
                ?? throw new InvalidOperationException("frame could not be resized");
        }
        finally
        {
            pin.Free();
        }

        using var smallBitmap = small;
        using var image = SKImage.FromBitmap(smallBitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 85);
        return data.ToArray();
    }

    public byte[] EncodePng() => Encode(SKEncodedImageFormat.Png, 100);

    private byte[] Encode(SKEncodedImageFormat format, int quality)
    {
        var source = _bgra ?? throw new ObjectDisposedException(nameof(AndroidCameraFrame));
        using var image = SKImage.FromPixelCopy(new SKImageInfo(Width, Height, SKColorType.Bgra8888, SKAlphaType.Opaque), source, Width * 4)
            ?? throw new InvalidOperationException("frame could not be encoded");
        using var data = image.Encode(format, quality);
        return data.ToArray();
    }

    public void Dispose()
    {
        var buffer = _bgra;
        _bgra = null;
        if (buffer is not null)
        {
            release?.Invoke(buffer);
        }
    }
}
