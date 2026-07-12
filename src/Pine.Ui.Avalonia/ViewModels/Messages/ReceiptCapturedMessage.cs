using System.IO;

namespace Pine.Ui.Avalonia.ViewModels.Messages;

public record ReceiptCapturedMessage(MemoryStream ImageStream, MemoryStream? OcrImageStream = null);
