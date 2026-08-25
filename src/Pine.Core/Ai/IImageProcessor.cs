using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Pine.Core.Ai;

public struct Point2D
{
    public float X { get; set; }
    public float Y { get; set; }

    public Point2D(float x, float y)
    {
        X = x;
        Y = y;
    }
}

public struct Quadrilateral
{
    public Point2D TopLeft { get; set; }
    public Point2D TopRight { get; set; }
    public Point2D BottomRight { get; set; }
    public Point2D BottomLeft { get; set; }

    public Quadrilateral(Point2D topLeft, Point2D topRight, Point2D bottomRight, Point2D bottomLeft)
    {
        TopLeft = topLeft;
        TopRight = topRight;
        BottomRight = bottomRight;
        BottomLeft = bottomLeft;
    }
}

public interface IImageProcessor
{
    /// <summary>
    /// Performs a perspective transformation (warp) to extract a rectangular image from a skewed quadrilateral region.
    /// </summary>
    Task<Stream> PerspectiveCropAsync(Stream originalImage, Quadrilateral cropQuad, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enhances the image for OCR (e.g., adaptive thresholding, noise reduction).
    /// </summary>
    Task<Stream> EnhanceForOcrAsync(Stream originalImage, CancellationToken cancellationToken = default);
}
