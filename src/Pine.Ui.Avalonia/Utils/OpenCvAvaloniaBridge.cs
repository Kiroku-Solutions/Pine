using System;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using OpenCvSharp;

namespace Pine.Ui.Avalonia.Utils;

public static class OpenCvAvaloniaBridge
{
    /// <summary>
    /// Fast conversion from OpenCvSharp Mat to Avalonia WriteableBitmap without Garbage Collection overhead.
    /// Uses Buffer.MemoryCopy to blit the native memory directly to the Avalonia Framebuffer.
    /// </summary>
    public static unsafe WriteableBitmap ToWriteableBitmap(this Mat mat)
    {
        if (mat == null || mat.Empty())
            throw new ArgumentException("Mat is null or empty");

        // Convert color space if necessary so it matches Avalonia's BGRA8888 or BGR888
        // Avalonia usually uses Bgra8888 on most platforms for optimal rendering.
        using var convertedMat = new Mat();
        if (mat.Channels() == 1)
        {
            Cv2.CvtColor(mat, convertedMat, ColorConversionCodes.GRAY2BGRA);
        }
        else if (mat.Channels() == 3)
        {
            Cv2.CvtColor(mat, convertedMat, ColorConversionCodes.BGR2BGRA);
        }
        else
        {
            mat.CopyTo(convertedMat); // Assume it's already 4 channels
        }

        var bitmap = new WriteableBitmap(
            new PixelSize(convertedMat.Width, convertedMat.Height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Opaque);

        using (var framebuffer = bitmap.Lock())
        {
            var srcPtr = (byte*)convertedMat.DataPointer;
            var dstPtr = (byte*)framebuffer.Address;

            long bytesToCopy = convertedMat.Rows * convertedMat.Step();
            
            // Fast memory copy
            Buffer.MemoryCopy(srcPtr, dstPtr, bytesToCopy, bytesToCopy);
        }

        return bitmap;
    }
}
