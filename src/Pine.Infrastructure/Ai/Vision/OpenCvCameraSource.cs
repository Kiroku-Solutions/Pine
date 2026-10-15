using System;
using OpenCvSharp;
using Pine.Core.Ai;

namespace Pine.Infrastructure.Ai.Vision;

/// <summary>
/// The default camera, opened through OpenCV (TD-120).
/// </summary>
/// <remarks>
/// Everything OpenCV-shaped the live scanner used to do itself lives here now: the backend choice, the
/// fallback, the handle released between attempts, and the conversion to the BGRA the screen paints.
/// </remarks>
public sealed class OpenCvCameraSource : ICameraSource
{
    /// <summary>
    /// The camera this opens: the one the operating system calls the default.
    /// </summary>
    /// <remarks>
    /// THERE WAS A PICKER, AND EVERY ENTRY IN IT WAS INVENTED (post-audit plan item 1.5). OpenCV exposes
    /// no device enumeration — the only way to learn whether index 2 exists is to open it, camera light
    /// and all — so the scanner uses index 0, which OpenCV defines as the system default. Choosing
    /// between cameras needs an implementation of <see cref="ICameraSource"/> that can enumerate them.
    /// </remarks>
    public const int DefaultCameraIndex = 0;

    /// <inheritdoc />
    public ICameraSession? OpenDefault()
    {
        // DirectShow first: on Windows it opens a webcam in a fraction of the default backend's time.
        var capture = new VideoCapture(DefaultCameraIndex, VideoCaptureAPIs.DSHOW);
        if (!capture.IsOpened())
        {
            // THE FIRST HANDLE IS RELEASED BEFORE THE SECOND IS TAKEN (found by the independent review of
            // plan item 1.5, when this lived in the view model). Reassigning without disposing leaked the
            // DirectShow instance, and a user pressing Start again could be blocked by Pine's own handle.
            capture.Dispose();
            capture = new VideoCapture(DefaultCameraIndex);
        }

        if (!capture.IsOpened())
        {
            capture.Dispose();
            return null;
        }

        return new Session(capture);
    }

    private sealed class Session(VideoCapture capture) : ICameraSession
    {
        private bool _disposed;

        public ICameraFrame? ReadFrame()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var mat = new Mat();
            if (!capture.IsOpened() || !capture.Read(mat) || mat.Empty())
            {
                mat.Dispose();
                return null;
            }

            return new Frame(mat);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            capture.Release();
            capture.Dispose();
        }
    }

    /// <summary>A frame over an OpenCV image it owns. Internal so its pixel copy can be tested without a camera.</summary>
    internal sealed class Frame(Mat mat) : ICameraFrame
    {
        public int Width => mat.Width;

        public int Height => mat.Height;

        public void CopyBgraTo(Span<byte> destination, int destinationStride)
        {
            var width = mat.Width;
            var height = mat.Height;
            var rowBytes = width * 4;

            if (destinationStride < rowBytes)
            {
                throw new ArgumentOutOfRangeException(nameof(destinationStride), destinationStride, "A row of this frame needs 4 bytes per pixel.");
            }

            if (destination.Length < ((height - 1) * destinationStride) + rowBytes)
            {
                throw new ArgumentException("The destination is smaller than the frame.", nameof(destination));
            }

            using var bgra = new Mat();
            switch (mat.Channels())
            {
                case 1:
                    Cv2.CvtColor(mat, bgra, ColorConversionCodes.GRAY2BGRA);
                    break;
                case 3:
                    Cv2.CvtColor(mat, bgra, ColorConversionCodes.BGR2BGRA);
                    break;
                case 4:
                    mat.CopyTo(bgra);
                    break;
                default:
                    throw new NotSupportedException($"A camera frame with {mat.Channels()} channels cannot be shown.");
            }

            var sourceStride = (int)bgra.Step();

            // Row by row, because the two strides need not agree: OpenCV may pad a row, and so may the
            // bitmap the screen hands in. The whole frame is copied before this returns, so the caller may
            // dispose the frame and post the pixels to another thread (the use-after-free the scanner's
            // comment used to warn about).
            unsafe
            {
                var source = (byte*)bgra.Data;
                for (var row = 0; row < height; row++)
                {
                    new ReadOnlySpan<byte>(source + ((long)row * sourceStride), rowBytes)
                        .CopyTo(destination.Slice(row * destinationStride, rowBytes));
                }
            }
        }

        public byte[] EncodeJpeg() => mat.ImEncode(".jpg");

        public byte[] EncodePng() => mat.ImEncode(".png");

        public void Dispose() => mat.Dispose();
    }
}
