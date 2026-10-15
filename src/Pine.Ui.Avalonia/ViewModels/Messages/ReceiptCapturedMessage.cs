using System.IO;

namespace Pine.Ui.Avalonia.ViewModels.Messages;

/// <summary>
/// A captured receipt image, handed from the sender to whoever receives it.
/// </summary>
/// <remarks>
/// OWNERSHIP TRANSFERS WITH THE MESSAGE (TD-126, decided 2026-10-07). The sender builds the streams, sends
/// the message, and forgets them: it must not read, seek or dispose them afterwards. The RECIPIENT owns
/// them from the moment <c>Receive</c> is called, uses them, and disposes them in a <c>finally</c> (or,
/// if it keeps one for later, when it is itself disposed). This is a transfer, not shared ownership: a
/// message that two recipients both dispose, or neither does, is a bug.
/// </remarks>
/// <param name="ImageStream">The captured (or cropped) image. Owned by the recipient.</param>
/// <param name="OcrImageStream">An OCR-enhanced copy, if any. Owned by the recipient.</param>
public record ReceiptCapturedMessage(MemoryStream ImageStream, MemoryStream? OcrImageStream = null);
