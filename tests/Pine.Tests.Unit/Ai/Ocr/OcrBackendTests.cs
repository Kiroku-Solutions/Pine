using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Pine.Core.Ai;
using Pine.Core.Configuration;
using Pine.Infrastructure.Ai;
using Pine.Infrastructure.Ai.Categorisation;
using Pine.Infrastructure.Ai.Vision;
using Pine.Tests.Unit.Composition;
using Pine.Ui.Avalonia.Composition;

namespace Pine.Tests.Unit.Ai.Ocr;

/// <summary>The OCR seam (docs/65 section 6.1): the provider takes its engine, and Pine:Ai:OcrBackend chooses it.</summary>
public sealed class OcrBackendTests
{
    private sealed class FakeOcrEngine : IOcrEngine, IDisposable
    {
        public int Calls { get; private set; }

        public bool Disposed { get; private set; }

        public Task<IReadOnlyList<OcrWord>> ExtractTextAsync(Stream imageStream, CancellationToken cancellationToken)
        {
            Calls++;
            IReadOnlyList<OcrWord> words =
            [
                new OcrWord { Text = "TOTAL 12.50", Confidence = 0.9f, BoundingBox = new BoundingBox { X = 10, Y = 200, Width = 120, Height = 20 } },
                new OcrWord { Text = "SUPER MARKET", Confidence = 0.9f, BoundingBox = new BoundingBox { X = 10, Y = 10, Width = 160, Height = 24 } },
            ];
            return Task.FromResult(words);
        }

        public void Dispose() => Disposed = true;
    }

    private static IConfiguration Config(params (string Key, string Value)[] entries) =>
        new ConfigurationBuilder().AddInMemoryCollection(entries.ToDictionary(e => e.Key, e => (string?)e.Value)).Build();

    [Fact]
    public void The_default_backend_is_paddle_and_no_model_directory_is_set()
    {
        var bound = PineConfiguration.Bind(Config());

        bound.Ai.OcrBackend.Should().Be(OcrBackend.Paddle);
        bound.Ai.OcrModelDirectory.Should().BeNull();
    }

    [Theory]
    [InlineData("Onnx", OcrBackend.Onnx)]
    [InlineData("onnx", OcrBackend.Onnx)]
    [InlineData("Paddle", OcrBackend.Paddle)]
    public void The_backend_binds_from_Pine_Ai_OcrBackend(string value, OcrBackend expected)
    {
        var bound = PineConfiguration.Bind(Config(("Pine:Ai:OcrBackend", value), ("Pine:Ai:OcrModelDirectory", "/models/ocr")));

        bound.Ai.OcrBackend.Should().Be(expected);
        bound.Ai.OcrModelDirectory.Should().Be("/models/ocr");
        PineConfiguration.FindUnknownKeys(Config(("Pine:Ai:OcrBackend", value))).Should().BeEmpty();
    }

    [Fact]
    public async Task The_provider_uses_the_engine_it_is_given_and_does_not_dispose_it()
    {
        var engine = new FakeOcrEngine();
        var provider = new DefaultLocalAiProvider(Substitute.For<IMlNetCategorizer>(), ocrEngine: engine);
        await using var image = File.OpenRead(Path.Combine(OcrTestAssets.FixtureDirectory(), "onnx_ocr_receipt_synthetic.png"));

        var result = await provider.ExtractReceiptDataAsync(image);

        engine.Calls.Should().Be(1);
        result.Should().NotBeNull();

        provider.Dispose();
        engine.Disposed.Should().BeFalse("the container owns an injected engine");
    }

    [Fact]
    public void The_onnx_backend_registers_the_managed_engine_on_the_windows_head()
    {
        using var provider = PineCompositionRoot.Build(
            PineHeadProfile.Windows,
            options =>
            {
                options.Storage.DataDirectory = TestContainer.DataDirectory;
                options.Ai.OcrBackend = OcrBackend.Onnx;
            });

        provider.GetRequiredService<IOcrEngine>().Should().BeOfType<OnnxOcrEngine>();
        provider.GetRequiredService<IAIProvider>().Should().BeOfType<DefaultLocalAiProvider>();
    }
}
