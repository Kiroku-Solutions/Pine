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

        var thread = new HandlerThread("pine-camera");
        thread.Start();
        var handler = new Handler(thread.Looper!);
        var reader = ImageReader.NewInstance(size.Width, size.Height, ImageFormatType.Yuv420888, 2)!;
        var session = new AndroidCameraSession(activity, sensor, thread, reader);
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

        var configured = new TaskCompletionSource<CameraCaptureSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        var surface = _reader.Surface ?? throw new InvalidOperationException("no surface");
#pragma warning disable CA1422, CS0618 // Validate platform compatibility; the list overload is the one that works from API 24 up.
        _device.CreateCaptureSession([surface], new SessionCallback(configured), handler);
#pragma warning restore CA1422, CS0618
        if (!configured.Task.Wait(TimeSpan.FromSeconds(8)))
        {
            throw new TimeoutException("camera session timed out");
        }

        _session = configured.Task.Result;

        var request = _device.CreateCaptureRequest(CameraTemplate.Preview)
            ?? throw new InvalidOperationException("no request");
        request.AddTarget(surface);
        request.Set(CaptureRequest.ControlAfMode!, (int)ControlAFMode.ContinuousPicture);
        _session.SetRepeatingRequest(request.Build()!, null, handler);
    }

    /// <inheritdoc />
    public ICameraFrame? ReadFrame()
    {
        if (_disposed)
        {
            return null;
        }

        using var image = _reader.AcquireLatestImage();
        return image is null ? null : Convert(image);
    }

    private AndroidCameraFrame Convert(Image image)
    {
        var width = image.Width;
        var height = image.Height;
        var planes = image.GetPlanes() ?? throw new InvalidOperationException("no planes");
        var y = Copy(planes[0]);
        var u = Copy(planes[1]);
        var v = Copy(planes[2]);
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

        var outWidth = rotation is 90 or 270 ? height : width;
        var outHeight = rotation is 90 or 270 ? width : height;
        var bgra = new byte[outWidth * outHeight * 4];
        for (var oy = 0; oy < outHeight; oy++)
        {
            for (var ox = 0; ox < outWidth; ox++)
            {
                int sx;
                int sy;
                switch (rotation)
                {
                    case 90: sx = oy; sy = height - 1 - ox; break;
                    case 180: sx = width - 1 - ox; sy = height - 1 - oy; break;
                    case 270: sx = width - 1 - oy; sy = ox; break;
                    default: sx = ox; sy = oy; break;
                }

                var yy = y[(sy * yRow) + sx];
                var ci = ((sy >> 1) * uvRow) + ((sx >> 1) * uvPixel);
                var uu = u[ci] - 128;
                var vv = v[ci] - 128;
                var o = ((oy * outWidth) + ox) * 4;
                bgra[o] = Clamp(yy + ((454 * uu) >> 8));
                bgra[o + 1] = Clamp(yy - (((88 * uu) + (183 * vv)) >> 8));
                bgra[o + 2] = Clamp(yy + ((359 * vv) >> 8));
                bgra[o + 3] = 255;
            }
        }

        return new AndroidCameraFrame(outWidth, outHeight, bgra);
    }

    private static byte Clamp(int value) => (byte)(value < 0 ? 0 : value > 255 ? 255 : value);

    private static byte[] Copy(Image.Plane plane)
    {
        var buffer = plane.Buffer ?? throw new InvalidOperationException("no plane buffer");
        var bytes = new byte[buffer.Remaining()];
        buffer.Get(bytes);
        return bytes;
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
internal sealed class AndroidCameraFrame(int width, int height, byte[] bgra) : ICameraFrame
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

    public byte[] EncodePng() => Encode(SKEncodedImageFormat.Png, 100);

    private byte[] Encode(SKEncodedImageFormat format, int quality)
    {
        var source = _bgra ?? throw new ObjectDisposedException(nameof(AndroidCameraFrame));
        using var image = SKImage.FromPixelCopy(new SKImageInfo(Width, Height, SKColorType.Bgra8888, SKAlphaType.Opaque), source, Width * 4)
            ?? throw new InvalidOperationException("frame could not be encoded");
        using var data = image.Encode(format, quality);
        return data.ToArray();
    }

    public void Dispose() => _bgra = null;
}
