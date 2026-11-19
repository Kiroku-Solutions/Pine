using System;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Pine.Core.Ai;
using Pine.Infrastructure.Ai;
using Pine.Infrastructure.Ai.Vision;
using Pine.Ui.Avalonia.Composition;
using Pine.Ui.Avalonia.Services;
using Pine.Ui.Avalonia.ViewModels;
using Xunit;

namespace Pine.Tests.Unit.Composition;

/// <summary>
/// The Android head's receipt scan runs the managed stack only (docs/65 phase C): resolving the AI provider and the scan
/// page must not construct anything that names an OpenCvSharp type, because OpenCV has no Android native library and
/// touching one is a process abort, not an exception.
/// </summary>
public sealed class AndroidOcrOnlyCompositionTests
{
    private static readonly string[] OpenCvBoundTypes =
    [
        nameof(ReceiptDetector),
        nameof(OpenCvImageProcessor),
        nameof(OpenCvCameraSource),
        nameof(PaddleOcrEngine),
    ];

    [Fact]
    public void TheAndroidContainer_ShouldResolveTheScanPipeline_WithoutAnyOpenCvBoundType()
    {
        using var provider = TestContainer.Build(PineHeadProfile.Android);

        var ocr = provider.GetRequiredService<IOcrEngine>();
        ocr.Should().BeOfType<OnnxOcrEngine>("the Paddle engine cannot load on Android, whatever the configuration says");

        var detector = provider.GetRequiredService<IReceiptDetector>();
        detector.Should().BeOfType<SkiaReceiptDetector>();

        var ai = provider.GetRequiredService<IAIProvider>();
        ai.Should().BeOfType<DefaultLocalAiProvider>();

        // The OpenCV-only services do not exist on this head at all.
        provider.GetService<IImageProcessor>().Should().BeNull();
        provider.GetService<ICameraSource>().Should().BeNull();

        // And nothing the provider holds is one of the OpenCV-bound implementations.
        var held = ai.GetType()
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Select(f => f.GetValue(ai))
            .Where(v => v is not null)
            .Select(v => v!.GetType().Name)
            .ToArray();
        held.Should().NotContain(OpenCvBoundTypes);
        held.Should().Contain(nameof(SkiaReceiptDetector)).And.Contain(nameof(OnnxOcrEngine));
    }

    [Fact]
    public void TheAndroidScanPage_ShouldBuild_WithoutLiveScanning()
    {
        using var provider = TestContainer.Build(PineHeadProfile.Android);
        var factory = provider.GetRequiredService<IViewModelFactory>();
        var shell = Substitute.For<IShellNavigator>();

        var scan = factory.CreateScanReceipt(shell);

        scan.Should().BeOfType<ScanReceiptViewModel>();
        scan.HasLiveScanner.Should().BeFalse("Retake opens the live scanner, which this head does not have");
        scan.RetakeCommand.CanExecute(null).Should().BeFalse();
        ((Action)(() => factory.CreateLiveScanner(shell))).Should().Throw<InvalidOperationException>();
        ((Action)(() => factory.CreateCropReceipt(shell))).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void TheWindowsContainer_ShouldKeepTheOpenCvPipeline()
    {
        using var provider = TestContainer.Build(PineHeadProfile.Windows);

        provider.GetRequiredService<IReceiptDetector>().Should().BeOfType<ReceiptDetector>();
        provider.GetService<IImageProcessor>().Should().NotBeNull();
        provider.GetService<ICameraSource>().Should().NotBeNull();
    }

    [Fact]
    public void TheAndroidScanPage_ShouldOfferTheCamera_OnlyWhenTheHeadRegistersACameraSource()
    {
        var shell = Substitute.For<IShellNavigator>();

        using (var without = TestContainer.Build(PineHeadProfile.Android))
        {
            without.GetRequiredService<IViewModelFactory>().CreateScanReceipt(shell).HasCameraCapture.Should().BeFalse();
        }

        // The Android head adds its Camera2 source through configureHeadServices; the page then offers Take Photo.
        using var with = PineCompositionRoot.Build(
            PineHeadProfile.Android,
            options => options.Storage.DataDirectory = TestContainer.DataDirectory,
            services => services.UseCameraSource<FakeCamera>());
        var scan = with.GetRequiredService<IViewModelFactory>().CreateScanReceipt(shell);

        scan.HasCameraCapture.Should().BeTrue();
        scan.HasLiveScanner.Should().BeFalse("the full live scanner page still needs OpenCV");
        scan.OpenCameraCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void TheWindowsScanPage_ShouldKeepTheLiveScanner_NotTheInlineCamera()
    {
        using var provider = TestContainer.Build(PineHeadProfile.Windows);
        var scan = provider.GetRequiredService<IViewModelFactory>().CreateScanReceipt(Substitute.For<IShellNavigator>());

        scan.HasLiveScanner.Should().BeTrue();
        scan.HasCameraCapture.Should().BeFalse();
    }

    [Fact]
    public void TheNavigationTargets_ShouldOfferScanButNotLiveScanning_OnAnOcrOnlyHead()
    {
        var targets = PineCompositionRoot.NavigationTargets(PineHeadCapabilities.For(PineHeadProfile.Android));

        targets.Should().Contain(typeof(ScanReceiptViewModel));
        targets.Should().NotContain(typeof(LiveScannerViewModel));
    }

    private sealed class FakeCamera : ICameraSource
    {
        public ICameraSession? OpenDefault() => null;
    }
}
