using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using Pine.Core.Ai;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.Services;
using Pine.Ui.Avalonia.ViewModels;
using Xunit;

namespace Pine.Tests.Unit.ViewModels;

/// <summary>
/// The live scanner drives the camera through <see cref="ICameraSource"/> and releases what it opens
/// (TD-120).
/// </summary>
/// <remarks>
/// <para>
/// WHAT THE SEAM BOUGHT, BEYOND THE ARCHITECTURE RULE. While the view model opened the webcam through
/// OpenCvSharp itself, none of its device handling could be tested without a webcam: every release path
/// the independent review of plan item 1.5 fixed — the handle leaked between the two backends, the one
/// opened but never stored — was fixed by reading, and nothing ran it. With a fake camera it runs.
/// </para>
/// </remarks>
public class LiveScannerCameraSessionTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private static LiveScannerViewModel Scanner(ICameraSource camera) => new(
        Substitute.For<IAIProvider>(),
        camera,
        Substitute.For<IShellNavigator>(),
        Substitute.For<IViewModelFactory>(),
        new Localizer());

    [Fact]
    public async Task StoppingTheScanner_ReleasesTheCamera_AndEveryFrameItRead()
    {
        var camera = new FakeCamera();
        var scanner = Scanner(camera);

        scanner.StartScanningCommand.Execute(null);
        await Until(() => camera.Session?.FramesRead >= 3);

        scanner.StopScanningCommand.Execute(null);
        await Until(() => camera.Session!.Frames.All(frame => frame.Disposed));

        camera.Session!.Disposed.Should().BeTrue("stopping the scanner is putting the camera down");
        camera.Session.Frames.Should().OnlyContain(frame => frame.Disposed, "a frame holds native pixels");
        camera.Session.Frames.Should().OnlyContain(frame => frame.CopiedTo, "every frame read is painted");

        scanner.Dispose();
    }

    [Fact]
    public async Task DisposingTheScanner_ReleasesTheCamera()
    {
        var camera = new FakeCamera();
        var scanner = Scanner(camera);

        scanner.StartScanningCommand.Execute(null);
        await Until(() => camera.Session?.FramesRead >= 1);

        scanner.Dispose();

        camera.Session!.Disposed.Should().BeTrue("leaving the screen must not leave the camera light on");
    }

    /// <summary>A camera that fails mid-read is released by the loop's own cleanup, not left open.</summary>
    [Fact]
    public async Task ACameraThatFailsWhileReading_IsStillReleased()
    {
        var camera = new FakeCamera { ThrowOnRead = 2 };
        var scanner = Scanner(camera);

        scanner.StartScanningCommand.Execute(null);

        await Until(() => camera.Session?.Disposed == true);
        camera.Session!.Frames.Should().OnlyContain(frame => frame.Disposed);

        scanner.Dispose();
    }

    /// <summary>No camera to open: nothing is read, nothing is left to release, and the refusal is the documented one.</summary>
    [Fact]
    public async Task NoCameraToOpen_ReadsNothing()
    {
        var camera = new FakeCamera { Opens = false };
        var scanner = Scanner(camera);

        scanner.StartScanningCommand.Execute(null);
        await Until(() => camera.OpenAttempts == 1);
        await Task.Delay(100);

        camera.Session.Should().BeNull();
        scanner.Dispose();
    }

    /// <summary>A manual capture reads one frame, encodes it losslessly for the crop screen, and releases it.</summary>
    [Fact]
    public async Task AManualCapture_EncodesAPng_AndReleasesTheFrame()
    {
        var camera = new FakeCamera();
        var scanner = Scanner(camera);

        scanner.StartScanningCommand.Execute(null);
        await Until(() => camera.Session?.FramesRead >= 1);
        await Until(() => scanner.IsScanning);

        scanner.CaptureManuallyCommand.Execute(null);

        await Until(() => camera.Session!.Frames.Any(frame => frame.EncodedPng));
        var captured = camera.Session!.Frames.Single(frame => frame.EncodedPng);
        captured.Disposed.Should().BeTrue("the PNG is encoded before the frame is let go, and then it is let go");

        scanner.Dispose();
    }

    private static async Task Until(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.Elapsed > Patience)
            {
                throw new TimeoutException("The scanner did not reach the expected state.");
            }

            await Task.Delay(10);
        }
    }

    private sealed class FakeCamera : ICameraSource
    {
        private int _openAttempts;

        public bool Opens { get; init; } = true;

        public int ThrowOnRead { get; init; }

        public int OpenAttempts => _openAttempts;

        public FakeSession? Session { get; private set; }

        public ICameraSession? OpenDefault()
        {
            Interlocked.Increment(ref _openAttempts);
            if (!Opens)
            {
                return null;
            }

            Session = new FakeSession(ThrowOnRead);
            return Session;
        }
    }

    private sealed class FakeSession(int throwOnRead) : ICameraSession
    {
        private int _framesRead;

        public ConcurrentQueue<FakeFrame> Frames { get; } = new();

        public int FramesRead => _framesRead;

        public bool Disposed { get; private set; }

        public ICameraFrame? ReadFrame()
        {
            ObjectDisposedException.ThrowIf(Disposed, this);

            var count = Interlocked.Increment(ref _framesRead);
            if (throwOnRead > 0 && count >= throwOnRead)
            {
                throw new InvalidOperationException("The camera was unplugged.");
            }

            var frame = new FakeFrame();
            Frames.Enqueue(frame);
            return frame;
        }

        public void Dispose() => Disposed = true;
    }

    private sealed class FakeFrame : ICameraFrame
    {
        public int Width => 4;

        public int Height => 2;

        public bool Disposed { get; private set; }

        public bool CopiedTo { get; private set; }

        public bool EncodedPng { get; private set; }

        public void CopyBgraTo(Span<byte> destination, int destinationStride)
        {
            ObjectDisposedException.ThrowIf(Disposed, this);
            destination[..(destinationStride * Height)].Fill(0xFF);
            CopiedTo = true;
        }

        public byte[] EncodeJpeg()
        {
            ObjectDisposedException.ThrowIf(Disposed, this);
            return [0xFF, 0xD8];
        }

        public byte[] EncodePng()
        {
            ObjectDisposedException.ThrowIf(Disposed, this);
            EncodedPng = true;
            return [0x89, 0x50];
        }

        public void Dispose() => Disposed = true;
    }
}
