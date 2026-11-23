using System;
using System.IO;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Pine.Core.Ai;
using Pine.Infrastructure.Ai;
using Pine.Infrastructure.Ai.Extraction;
using Pine.Infrastructure.Ai.Extraction.LayoutLm;
using Pine.Infrastructure.Ai.Vision;
using Pine.Ui.Avalonia.Composition;
using Xunit;

namespace Pine.Tests.Unit.Composition;

/// <summary>
/// The receipt pipeline must run the same stages on every head that scans (detector, OCR, heuristics, LayoutLM overlay), and a head
/// that swaps one stage for another must do so on purpose. These tests look INSIDE the resolved <see cref="DefaultLocalAiProvider"/>,
/// because "the container resolves an IAIProvider" is true even when LayoutLM, the YOLO model or the right OCR engine was silently left out.
/// </summary>
public sealed class ReceiptPipelineParityTests
{
    private static string FakeModelFile()
    {
        // LayoutLM is registered lazily, so a path that exists is enough to see the wiring; nothing loads it here.
        var directory = Path.Combine(TestContainer.DataDirectory, "parity-layoutlm");
        Directory.CreateDirectory(directory);
        var model = Path.Combine(directory, "model.onnx");
        File.WriteAllBytes(model, [0]);
        return model;
    }

    private static T Field<T>(object owner, string name)
    {
        var field = owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"{owner.GetType().Name} has no field {name}; the pipeline was restructured, update this test.");
        return (T)field.GetValue(owner)!;
    }

    private static DefaultLocalAiProvider ProviderOn(PineHeadProfile head, Action<Pine.Core.Configuration.PineOptions>? configure = null, bool withModel = true)
    {
        var model = withModel ? FakeModelFile() : null;
        var container = PineCompositionRoot.Build(head, options =>
        {
            options.Storage.DataDirectory = TestContainer.DataDirectory;
            options.Ai.LayoutLmModelPath = model;
            configure?.Invoke(options);
        });
        return (DefaultLocalAiProvider)container.GetRequiredService<IAIProvider>();
    }

    [Theory]
    [InlineData(PineHeadProfile.Windows)]
    [InlineData(PineHeadProfile.Android)]
    public void EveryScanningHead_ShouldRunHeuristicsAndTheLayoutLmOverlay_AfterOcr(PineHeadProfile head)
    {
        var provider = ProviderOn(head);

        Field<object>(provider, "_extractor").Should().BeOfType<HeuristicFieldExtractor>();

        var guard = Field<VisualExtractionGuard>(provider, "_visualGuard");
        var extractor = Field<IVisualFieldExtractor?>(guard, "_extractor");
        extractor.Should().BeOfType<LayoutLmFieldExtractor>($"the {head} head configured a LayoutLM model and must not drop the KIE stage");
        guard.LastOutcome.Should().Be("off", "nothing has been scanned yet");
    }

    [Theory]
    [InlineData(PineHeadProfile.Windows)]
    [InlineData(PineHeadProfile.Android)]
    public void WithoutAModelPath_NoHeadInventsALayoutLmStage(PineHeadProfile head)
    {
        var provider = ProviderOn(head, withModel: false);

        Field<IVisualFieldExtractor?>(Field<VisualExtractionGuard>(provider, "_visualGuard"), "_extractor")
            .Should().BeNull("the overlay is optional and off until a model path is configured");
    }

    [Fact]
    public void Windows_ShouldDetectWithOpenCv_AndReadWithPaddleUnlessConfigured()
    {
        var paddle = ProviderOn(PineHeadProfile.Windows);
        Field<IReceiptDetector>(paddle, "_detector").Should().BeOfType<ReceiptDetector>();
        Field<IOcrEngine>(paddle, "_ocr").Should().BeOfType<PaddleOcrEngine>();

        var onnx = ProviderOn(PineHeadProfile.Windows, o => o.Ai.OcrBackend = OcrBackend.Onnx);
        Field<IOcrEngine>(onnx, "_ocr").Should().BeOfType<OnnxOcrEngine>("Pine:Ai:OcrBackend=Onnx must reach the Windows provider");
    }

    [Fact]
    public void Android_ShouldDetectWithTheYoloModelFirst_AndAlwaysReadWithOnnx_WhateverTheSettingSays()
    {
        var provider = ProviderOn(PineHeadProfile.Android, o => o.Ai.OcrBackend = OcrBackend.Paddle);

        Field<IOcrEngine>(provider, "_ocr").Should().BeOfType<OnnxOcrEngine>();
        var detector = Field<IReceiptDetector>(provider, "_detector").Should().BeOfType<SkiaReceiptDetector>().Subject;

        // The fallback order is YOLO, then the classical finder, then EXIF + downscale (FindQuadWithModel / Normalise). The first link
        // must exist: the YOLO model ships in every head's output, and a null here means every scan silently skips it.
        Field<YoloReceiptSegmenter?>(detector, "_yolo").Should().NotBeNull(
            "yolo-receipt-nano.onnx is copied beside the head; without it the Android scan runs the classical finder only");
    }

    private sealed class FakeVisual(bool fail) : IVisualFieldExtractor
    {
        public ReceiptExtractionResult ExtractFields(System.Collections.Generic.IReadOnlyList<OcrWord> ocrWords, ReadOnlyMemory<byte> encodedImage)
            => fail
                ? throw new InvalidOperationException("model broke")
                : new ReceiptExtractionResult("Shop", 5m, null, null, ReceiptConfidence.None);
    }

    [Fact]
    public async System.Threading.Tasks.Task TheGuard_ShouldReportWhetherTheModelContributed_SoALogLineCanTellAWorkingModelFromARetiredOne()
    {
        var heuristic = new ReceiptExtractionResult("Unknown", 0m, null, null, ReceiptConfidence.None);
        var words = new System.Collections.Generic.List<OcrWord>();

        var off = new VisualExtractionGuard(null, null);
        await off.OverlayAsync(heuristic, words, new MemoryStream([1]), default);
        off.LastOutcome.Should().Be("off");

        var good = new VisualExtractionGuard(new FakeVisual(false), null);
        await good.OverlayAsync(heuristic, words, new MemoryStream([1]), default);
        good.LastOutcome.Should().Be("ok");

        var bad = new VisualExtractionGuard(new FakeVisual(true), null);
        await bad.OverlayAsync(heuristic, words, new MemoryStream([1]), default);
        bad.LastOutcome.Should().Be("failed");
        for (var i = 1; i < VisualExtractionGuard.MaxConsecutiveFailures; i++)
        {
            await bad.OverlayAsync(heuristic, words, new MemoryStream([1]), default);
        }

        await bad.OverlayAsync(heuristic, words, new MemoryStream([1]), default);
        bad.LastOutcome.Should().Be("retired");
    }
}
