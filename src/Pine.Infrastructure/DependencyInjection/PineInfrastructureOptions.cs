using System;
using Pine.Infrastructure.Diagnostics;

namespace Pine.Infrastructure.DependencyInjection;

/// <summary>
/// The few decisions a composition root is still allowed to make about the infrastructure layer
/// after <c>AddPineInfrastructure</c> takes over the rest (TD-29).
/// </summary>
/// <remarks>
/// <para>
/// THE POINT OF THIS TYPE IS HOW SMALL IT IS. TD-29 exists because five roots each made twenty-odd
/// decisions independently and disagreed on four of them -- a transient DbContext on one head and a
/// scoped one on the others, an AI stack on one head and none on four. Every such decision that
/// moves into <c>AddPineInfrastructure</c> stops being a per-head choice. What is left here is the
/// set of choices that genuinely differ by platform, and each one has to justify its place.
/// </para>
/// </remarks>
public sealed class PineInfrastructureOptions
{
    /// <summary>
    /// Whether to register the on-device receipt pipeline -- YOLO detection, OpenCV geometry and
    /// PaddleOCR -- as opposed to the categorisation-only <see cref="Ai.PortableAiProvider"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS IS A FACT ABOUT THE PACKAGE GRAPH, NOT A PREFERENCE. <c>OpenCvSharp4.runtime.win</c> and
    /// <c>Sdcb.PaddleInference.runtime.win64.mkl</c> are the only native runtime packages
    /// <c>Pine.Infrastructure</c> references, and they carry win-x64 assets only. A head that sets
    /// this on any other platform does not get a slower scanner; it gets a P/Invoke into a library
    /// that was never copied to the output directory.
    /// </para>
    /// <para>
    /// Default is <c>false</c>, so a new head is portable until someone states otherwise. That
    /// direction is deliberate: the failure of forgetting to turn it on is a visible, catchable
    /// <c>AiCapabilityUnavailableException</c>; the failure of forgetting to turn it off is a native
    /// abort.
    /// </para>
    /// </remarks>
    public bool IncludeReceiptVision { get; set; }

    /// <summary>
    /// Whether the receipt pipeline may use OpenCV (document crop, enhancement, the manual crop
    /// processor and the live camera). Only consulted when <see cref="IncludeReceiptVision"/> is true.
    /// </summary>
    /// <remarks>
    /// <see langword="false"/> is the "OCR only" shape (docs/65 phase C): the container registers
    /// <see cref="Ai.Vision.SkiaReceiptDetector"/> and NOTHING that names an OpenCvSharp type, so a head
    /// without the native library (Android) never loads one. Default <see langword="true"/> keeps every
    /// existing caller on the full pipeline.
    /// </remarks>
    public bool UseOpenCvVision { get; set; } = true;

    /// <summary>
    /// Optional LayoutLMv3 field-extraction model for the on-device receipt pipeline. Off until
    /// <see cref="Ai.Extraction.LayoutLm.LayoutLmOptions.ModelPath"/> is set; the model is not shipped with the
    /// application. Only used when <see cref="IncludeReceiptVision"/> is true.
    /// </summary>
    public Ai.Extraction.LayoutLm.LayoutLmOptions LayoutLm { get; } = new();

    /// <summary>
    /// Which OCR engine the receipt pipeline uses (docs/65). Only consulted when
    /// <see cref="IncludeReceiptVision"/> is true. Default <see cref="Pine.Core.Ai.OcrBackend.Paddle"/>.
    /// </summary>
    public Pine.Core.Ai.OcrBackend OcrBackend { get; set; } = Pine.Core.Ai.OcrBackend.Paddle;

    /// <summary>Where the ONNX OCR backend looks for its models; <see langword="null"/> = the shipped assets folder.</summary>
    public string? OcrModelDirectory { get; set; }

    /// <summary>
    /// Optional hook onto <c>AddPineLogging</c>'s options -- the log directory, the minimum level,
    /// the rotation size.
    /// </summary>
    public Action<PineLoggingOptions>? ConfigureLogging { get; set; }
}
