using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;
using Pine.Core.Ai;

namespace Pine.Infrastructure.Ai.Vision;

public sealed class OpenCvImageProcessor : IImageProcessor
{
    public async Task<Stream> PerspectiveCropAsync(Stream originalImage, Quadrilateral cropQuad, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            originalImage.Position = 0;
            using var memoryStream = new MemoryStream();
            originalImage.CopyTo(memoryStream);
            using var srcMat = Cv2.ImDecode(memoryStream.ToArray(), ImreadModes.Color);

            if (srcMat.Empty())
                throw new InvalidOperationException("Failed to decode image.");

            // Define the 4 source points
            var srcPoints = new[]
            {
                new Point2f(cropQuad.TopLeft.X, cropQuad.TopLeft.Y),
                new Point2f(cropQuad.TopRight.X, cropQuad.TopRight.Y),
                new Point2f(cropQuad.BottomRight.X, cropQuad.BottomRight.Y),
                new Point2f(cropQuad.BottomLeft.X, cropQuad.BottomLeft.Y)
            };

            // Calculate the width and height of the new image
            var widthA = Math.Sqrt(Math.Pow(cropQuad.BottomRight.X - cropQuad.BottomLeft.X, 2) + Math.Pow(cropQuad.BottomRight.Y - cropQuad.BottomLeft.Y, 2));
            var widthB = Math.Sqrt(Math.Pow(cropQuad.TopRight.X - cropQuad.TopLeft.X, 2) + Math.Pow(cropQuad.TopRight.Y - cropQuad.TopLeft.Y, 2));
            var maxWidth = Math.Max((int)widthA, (int)widthB);

            var heightA = Math.Sqrt(Math.Pow(cropQuad.TopRight.X - cropQuad.BottomRight.X, 2) + Math.Pow(cropQuad.TopRight.Y - cropQuad.BottomRight.Y, 2));
            var heightB = Math.Sqrt(Math.Pow(cropQuad.TopLeft.X - cropQuad.BottomLeft.X, 2) + Math.Pow(cropQuad.TopLeft.Y - cropQuad.BottomLeft.Y, 2));
            var maxHeight = Math.Max((int)heightA, (int)heightB);

            // Define the 4 destination points for the flattened, rectangular image
            var dstPoints = new[]
            {
                new Point2f(0, 0),
                new Point2f(maxWidth - 1, 0),
                new Point2f(maxWidth - 1, maxHeight - 1),
                new Point2f(0, maxHeight - 1)
            };

            // Compute the perspective transform matrix
            using var transformMatrix = Cv2.GetPerspectiveTransform(srcPoints, dstPoints);

            // Apply the perspective warp
            using var dstMat = new Mat();
            Cv2.WarpPerspective(srcMat, dstMat, transformMatrix, new Size(maxWidth, maxHeight));

            // Encode back to stream (Jpeg format for smaller size, high quality)
            var resultStream = new MemoryStream();
            Cv2.ImEncode(".jpg", dstMat, out var buf, new[] { (int)ImwriteFlags.JpegQuality, 95 });
            resultStream.Write(buf, 0, buf.Length);
            resultStream.Position = 0;

            return (Stream)resultStream;
        }, cancellationToken);
    }

    public async Task<Stream> EnhanceForOcrAsync(Stream originalImage, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            originalImage.Position = 0;
            using var memoryStream = new MemoryStream();
            originalImage.CopyTo(memoryStream);
            using var srcMat = Cv2.ImDecode(memoryStream.ToArray(), ImreadModes.Color);

            if (srcMat.Empty())
                throw new InvalidOperationException("Failed to decode image.");

            using var grayMat = new Mat();
            Cv2.CvtColor(srcMat, grayMat, ColorConversionCodes.BGR2GRAY);

            // Apply slight Gaussian blur to remove high frequency noise
            using var blurredMat = new Mat();
            Cv2.GaussianBlur(grayMat, blurredMat, new Size(5, 5), 0);

            // Apply Adaptive Thresholding for high contrast binarization (great for receipts)
            using var enhancedMat = new Mat();
            Cv2.AdaptiveThreshold(blurredMat, enhancedMat, 255, AdaptiveThresholdTypes.GaussianC, ThresholdTypes.Binary, 11, 2);

            var resultStream = new MemoryStream();
            Cv2.ImEncode(".png", enhancedMat, out var buf); // PNG is better for binary images (lossless)
            resultStream.Write(buf, 0, buf.Length);
            resultStream.Position = 0;

            return (Stream)resultStream;
        }, cancellationToken);
    }
}
