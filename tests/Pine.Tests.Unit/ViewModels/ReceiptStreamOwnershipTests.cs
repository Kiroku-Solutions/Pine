using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using FluentAssertions;
using MediatR;
using NSubstitute;
using Pine.Application.Accounts.Queries;
using Pine.Application.Budgets.Queries;
using Pine.Application.Transactions.Commands;
using Pine.Core.Accounts;
using Pine.Core.Ai;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.Services;
using Pine.Ui.Avalonia.ViewModels;
using Pine.Ui.Avalonia.ViewModels.Messages;
using Xunit;

namespace Pine.Tests.Unit.ViewModels;

/// <summary>
/// A stream carried by <see cref="ReceiptCapturedMessage"/> is owned by its recipient (TD-126, decided
/// 2026-10-07): the sender hands it off and forgets it; the recipient uses it and disposes it exactly once.
/// </summary>
[Collection("ProcessWideState")]
public class ReceiptStreamOwnershipTests
{
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly IShellNavigator _navigator = Substitute.For<IShellNavigator>();
    private readonly IViewModelFactory _viewModels = Substitute.For<IViewModelFactory>();

    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    /// <summary>A memory stream that counts how many times it is disposed.</summary>
    private sealed class CountingMemoryStream : MemoryStream
    {
        private int _disposals;

        public CountingMemoryStream(byte[] bytes) : base(bytes) { }

        public int Disposals => Volatile.Read(ref _disposals);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Interlocked.Increment(ref _disposals);
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>A stream that is not a <see cref="MemoryStream"/>, as a real image processor may return.</summary>
    private sealed class CountingForeignStream : Stream
    {
        private readonly MemoryStream _inner = new(new byte[] { 1, 2, 3 });
        private int _disposals;

        public int Disposals => Volatile.Read(ref _disposals);

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => _inner.Position = value; }
        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => _inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Interlocked.Increment(ref _disposals);
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }
    }

    private ScanReceiptViewModel ScanPage()
    {
        _mediator.Send(Arg.Any<GetAccountsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Account>>(Array.Empty<Account>()));
        _mediator.Send(Arg.Any<ScanReceiptCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ReceiptExtractionResult(null, null, null, null, ReceiptConfidence.None)));
        return new ScanReceiptViewModel(_mediator, _navigator, _viewModels, new Localizer());
    }

    /// <summary>The recipient scans from the streams, then disposes each exactly once.</summary>
    [Fact]
    public async Task TheScanPage_DisposesBothStreamsOfAMessage_ExactlyOnce()
    {
        using var page = ScanPage();
        var image = new CountingMemoryStream(Png);
        var ocr = new CountingMemoryStream(Png);

        page.Receive(new ReceiptCapturedMessage(image, ocr));
        await WaitUntilAsync(() => image.Disposals > 0 && ocr.Disposals > 0);
        await Task.Delay(200);

        image.Disposals.Should().Be(1);
        ocr.Disposals.Should().Be(1);
        _ = _mediator.Received(1).Send(Arg.Any<ScanReceiptCommand>(), Arg.Any<CancellationToken>());
    }

    /// <summary>A failed scan still releases them: the disposal is in a finally.</summary>
    [Fact]
    public async Task TheScanPage_DisposesTheStreams_EvenWhenTheScanFails()
    {
        using var page = ScanPage();
        _mediator.Send(Arg.Any<ScanReceiptCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ReceiptExtractionResult>(new InvalidOperationException("boom")));
        var image = new CountingMemoryStream(Png);

        page.Receive(new ReceiptCapturedMessage(image));
        await WaitUntilAsync(() => image.Disposals > 0);
        await Task.Delay(200);

        image.Disposals.Should().Be(1);
    }

    /// <summary>The crop page parks the original, so it releases it when it is disposed, once.</summary>
    [Fact]
    public void TheCropPage_KeepsTheStreamWhileItIsOpen_AndDisposesItOnceWhenDisposed()
    {
        var crop = new CropReceiptViewModel(_navigator, _viewModels, Substitute.For<IImageProcessor>());
        var image = new CountingMemoryStream(Png);

        crop.Receive(new ReceiptCapturedMessage(image));
        image.Disposals.Should().Be(0, "the page still needs it to crop");

        crop.Dispose();
        crop.Dispose();

        image.Disposals.Should().Be(1);
    }

    /// <summary>A replaced frame is released by the page that was handed it.</summary>
    [Fact]
    public void TheCropPage_ReleasesAFrameItReplaces()
    {
        var crop = new CropReceiptViewModel(_navigator, _viewModels, Substitute.For<IImageProcessor>());
        var first = new CountingMemoryStream(Png);
        var second = new CountingMemoryStream(Png);

        crop.Receive(new ReceiptCapturedMessage(first));
        crop.Receive(new ReceiptCapturedMessage(second));

        first.Disposals.Should().Be(1);
        second.Disposals.Should().Be(0);
        crop.Dispose();
    }

    /// <summary>
    /// The sender hands off and forgets: after the send it neither disposes the streams it sent nor
    /// leaves a stream it copied undisposed.
    /// </summary>
    [Fact]
    public async Task TheCropPage_AsASender_DoesNotTouchWhatItSent_AndReleasesWhatItOnlyCopied()
    {
        var sentCrop = new CountingMemoryStream(new byte[] { 1 });
        var foreignEnhanced = new CountingForeignStream();
        var processor = Substitute.For<IImageProcessor>();
        processor.PerspectiveCropAsync(Arg.Any<Stream>(), Arg.Any<Quadrilateral>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Stream>(sentCrop));
        processor.EnhanceForOcrAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Stream>(foreignEnhanced));

        var crop = new CropReceiptViewModel(_navigator, _viewModels, processor);
        crop.Receive(new ReceiptCapturedMessage(new CountingMemoryStream(Png)));
        crop.IsActive = true;
        // Leaving the page is what navigation does; it must not be a recipient of its own message.
        _navigator.When(n => n.NavigateTo(Arg.Any<ObservableObject>())).Do(_ => crop.IsActive = false);

        var recipient = new object();
        ReceiptCapturedMessage? received = null;
        WeakReferenceMessenger.Default.Register<object, ReceiptCapturedMessage>(recipient, (_, m) => received = m);
        try
        {
            await crop.ConfirmCropAsync(0, 0, 1, 0, 1, 1, 0, 1);
        }
        finally
        {
            WeakReferenceMessenger.Default.UnregisterAll(recipient);
        }

        received.Should().NotBeNull();
        received!.ImageStream.Should().BeSameAs(sentCrop);
        sentCrop.Disposals.Should().Be(0, "it was handed to the recipient, which owns it now");
        received.OcrImageStream!.CanRead.Should().BeTrue("the copy that was sent is the recipient's");
        foreignEnhanced.Disposals.Should().Be(1, "the original was copied, never sent, so only the sender can release it");

        crop.Dispose();
    }
}
