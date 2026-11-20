using System;

namespace Pine.Core.Ai;

/// <summary>
/// The camera the live receipt scanner reads from (TD-120).
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS SEAM EXISTS. <c>LiveScannerViewModel</c> opened the webcam through OpenCvSharp itself —
/// <c>VideoCapture</c>, <c>Mat</c> and <c>Cv2</c> in a view model — so the architecture tests' clean
/// bill of health ("no view model names a <c>Pine.Infrastructure</c> type") stopped at Pine's own types
/// while a third-party native wrapper reached the UI through the same project reference. The UI now asks
/// for frames and receives frames; how a camera is opened, and with which native library, is the
/// infrastructure layer's business.
/// </para>
/// <para>
/// WHAT IT DOES NOT DO YET: CHOOSE A CAMERA. Only the default camera can be opened, because the only
/// implementation is OpenCV's and OpenCV cannot enumerate devices — learning whether a second camera
/// exists means opening it, lens and indicator light included (post-audit plan item 1.5). A platform API
/// that can enumerate would add a method here; nothing above this interface would change.
/// </para>
/// </remarks>
public interface ICameraSource
{
    /// <summary>
    /// Opens the system's default camera.
    /// </summary>
    /// <returns>The open session, or <see langword="null"/> when no camera could be opened.</returns>
    /// <remarks>Blocking, and it can take a second or more: call it off the UI thread.</remarks>
    ICameraSession? OpenDefault();
}

/// <summary>
/// An open camera. Disposing it releases the device.
/// </summary>
/// <remarks>Not thread-safe; the caller serialises reads against disposal.</remarks>
public interface ICameraSession : IDisposable
{
    /// <summary>
    /// Reads the next frame.
    /// </summary>
    /// <returns>The frame, owned by the caller, or <see langword="null"/> when none was available yet.</returns>
    ICameraFrame? ReadFrame();
}

/// <summary>
/// One captured image. Disposing it releases its pixels.
/// </summary>
public interface ICameraFrame : IDisposable
{
    /// <summary>Width in pixels.</summary>
    int Width { get; }

    /// <summary>Height in pixels.</summary>
    int Height { get; }

    /// <summary>
    /// Copies the frame as 8-bit BGRA, fully opaque, row by row.
    /// </summary>
    /// <param name="destination">At least <paramref name="destinationStride"/> × <see cref="Height"/> bytes.</param>
    /// <param name="destinationStride">Bytes per row in <paramref name="destination"/>; at least 4 × <see cref="Width"/>.</param>
    void CopyBgraTo(Span<byte> destination, int destinationStride);

    /// <summary>Encodes the frame as a JPEG, for detection.</summary>
    /// <returns>The encoded bytes.</returns>
    byte[] EncodeJpeg();

    /// <summary>
    /// Encodes the frame as a JPEG whose longer side is at most <paramref name="maxSide"/>, for a cheap analysis pass.
    /// The default ignores the limit; a camera that can shrink while encoding overrides it.
    /// </summary>
    /// <param name="maxSide">Longest side in pixels.</param>
    /// <returns>The encoded bytes.</returns>
    byte[] EncodeJpeg(int maxSide) => EncodeJpeg();

    /// <summary>Encodes the frame as a PNG, lossless, for the crop and OCR screens.</summary>
    /// <returns>The encoded bytes.</returns>
    byte[] EncodePng();
}
