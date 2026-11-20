using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Pine.Core.Ai;

/// <summary>A point in an image as a fraction of its width and height (0 to 1).</summary>
/// <param name="X">Left to right.</param>
/// <param name="Y">Top to bottom.</param>
public readonly record struct NormalizedPoint(double X, double Y);

/// <summary>The four corners of a receipt, clockwise from the top-left, in the upright picture's own space.</summary>
/// <param name="TopLeft">Top-left corner.</param>
/// <param name="TopRight">Top-right corner.</param>
/// <param name="BottomRight">Bottom-right corner.</param>
/// <param name="BottomLeft">Bottom-left corner.</param>
public sealed record ReceiptQuad(NormalizedPoint TopLeft, NormalizedPoint TopRight, NormalizedPoint BottomRight, NormalizedPoint BottomLeft);

/// <summary>A picture made upright (EXIF applied, size capped) with the receipt's edges found in it, if any.</summary>
/// <param name="UprightJpeg">The picture every later call works on; corner coordinates refer to it.</param>
/// <param name="Quad">The detected corners, or <see langword="null"/> when no receipt was found.</param>
/// <param name="Source">Whether the edges were found (<c>found</c>) or not (<c>none</c>). For diagnostics, never receipt text.</param>
public sealed record ReceiptEdgeResult(byte[] UprightJpeg, ReceiptQuad? Quad, string Source);

/// <summary>
/// Finding a receipt's edges, cropping to them with perspective correction, and turning a picture, as separate steps so a
/// screen can show the edges and let a person move them before anything is read (docs/65 section 13). Everything runs on
/// the device; nothing is stored.
/// </summary>
public interface IReceiptEdgeService
{
    /// <summary>The corners in a picture, quickly and without returning pixels: for a live view that asks again and again.</summary>
    /// <param name="image">An encoded picture, already upright (a camera frame).</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The corners, or <see langword="null"/> when no receipt was found.</returns>
    Task<ReceiptQuad?> DetectEdgesAsync(Stream image, CancellationToken cancellationToken = default);

    /// <summary>Makes the picture upright and finds the receipt in it.</summary>
    /// <param name="image">An encoded picture, possibly with an EXIF orientation.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The upright picture and the corners (if found).</returns>
    Task<ReceiptEdgeResult> FindEdgesAsync(Stream image, CancellationToken cancellationToken = default);

    /// <summary>Crops to <paramref name="quad"/> and straightens it.</summary>
    /// <param name="uprightImage">The <see cref="ReceiptEdgeResult.UprightJpeg"/> (or a rotated copy of it).</param>
    /// <param name="quad">Corners in that picture, possibly moved by the person.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The cropped picture as a JPEG.</returns>
    Task<Stream> CropAsync(Stream uprightImage, ReceiptQuad quad, CancellationToken cancellationToken = default);

    /// <summary>Turns the picture a quarter turn.</summary>
    /// <param name="uprightImage">The picture to turn.</param>
    /// <param name="clockwise">Direction.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The turned picture as a JPEG.</returns>
    Task<Stream> RotateAsync(Stream uprightImage, bool clockwise, CancellationToken cancellationToken = default);
}
