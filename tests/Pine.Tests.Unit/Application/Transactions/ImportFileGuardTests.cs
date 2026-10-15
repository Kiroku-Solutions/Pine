using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using Pine.Application.Transactions.Commands;
using Pine.Core.Accounts;
using Pine.Core.Importing;
using Xunit;

namespace Pine.Tests.Unit.Application.Transactions;

/// <summary>
/// The pre-parse file check on an import: size cap and content signature (TD-50).
/// </summary>
public class ImportFileGuardTests
{
    private static MemoryStream Bytes(params byte[] bytes) => new(bytes);

    private static MemoryStream Text(string text) => new(System.Text.Encoding.UTF8.GetBytes(text));

    [Fact]
    public void ADelimitedTextFile_IsAccepted() =>
        ImportFileGuard.Inspect(Text("Date,Amount\n2026-01-01,4.50\n")).Should().BeNull();

    [Fact]
    public void AnEmptyFile_IsLeftToTheImporter() =>
        ImportFileGuard.Inspect(new MemoryStream()).Should().BeNull();

    [Fact]
    public void AModernWorkbook_IsAccepted() =>
        ImportFileGuard.Inspect(Bytes(0x50, 0x4B, 0x03, 0x04, 0x00, 0x00, 0x01)).Should().BeNull();

    [Fact]
    public void ALegacyWorkbook_IsAccepted() =>
        ImportFileGuard.Inspect(Bytes(0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, 0x00)).Should().BeNull();

    [Fact]
    public void AUtf16TextFile_IsAccepted() =>
        ImportFileGuard.Inspect(Bytes(0xFF, 0xFE, 0x44, 0x00, 0x61, 0x00)).Should().BeNull();

    [Theory]
    [InlineData("%PDF-1.7 not a table")]
    [InlineData("MZ this is an executable")]
    [InlineData("GIF89a")]
    public void AKnownBinaryFormat_IsRefused(string content)
    {
        var error = ImportFileGuard.Inspect(Text(content));

        error.Should().NotBeNull();
        error!.UserFacingMessage.Key.Should().Be("Validation.Import.FileTypeNotSupported");
    }

    [Fact]
    public void APngAndAJpeg_AreRefused()
    {
        ImportFileGuard.Inspect(Bytes(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A)).Should().NotBeNull();
        ImportFileGuard.Inspect(Bytes(0xFF, 0xD8, 0xFF, 0xE0, 0x41)).Should().NotBeNull();
    }

    [Fact]
    public void AFileWithANulByteInItsHead_IsRefused() =>
        ImportFileGuard.Inspect(Bytes(0x41, 0x2C, 0x00, 0x42)).Should().NotBeNull();

    [Fact]
    public void AFileOverTheCap_IsRefusedWithTheLimitAsAnArgument()
    {
        using var stream = new SparseStream((long)ImportFileGuard.MaximumMegabytes * 1024 * 1024 + 1, (byte)'a');

        var error = ImportFileGuard.Inspect(stream);

        error.Should().NotBeNull();
        error!.UserFacingMessage.Key.Should().Be("Validation.Import.FileTooLarge");
        error.UserFacingMessage.Arguments.Should().Equal(ImportFileGuard.MaximumMegabytes);
    }

    [Fact]
    public void AFileExactlyAtTheCap_IsAccepted()
    {
        using var stream = new SparseStream((long)ImportFileGuard.MaximumMegabytes * 1024 * 1024, (byte)'a');

        ImportFileGuard.Inspect(stream).Should().BeNull();
    }

    [Fact]
    public void Inspecting_RestoresTheStreamPosition()
    {
        var stream = Text("Date,Amount\n");
        stream.Position = 5;

        ImportFileGuard.Inspect(stream);

        stream.Position.Should().Be(5);
    }

    [Fact]
    public async Task TheValidator_ReportsTheFileError_AlongsideAMissingAccount()
    {
        var accounts = Substitute.For<IAccountRepository>();
        var validator = new ImportTransactionsCommandValidator(accounts);
        var command = new ImportTransactionsCommand(
            Guid.NewGuid(), Text("%PDF-1.4"), Substitute.For<IImporter>(), ImportProfile.Create("p", TimeProvider.System));

        var errors = await validator.ValidateAsync(command);

        errors.Select(e => e.UserFacingMessage.Key).Should().BeEquivalentTo(
            "Validation.AccountNotFound", "Validation.Import.FileTypeNotSupported");
    }

    /// <summary>A seekable stream of a stated length that allocates nothing: reads return <c>fill</c>.</summary>
    private sealed class SparseStream(long length, byte fill) : Stream
    {
        private long _position;

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get => _position; set => _position = value; }
        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = (int)Math.Min(count, length - _position);
            Array.Fill(buffer, fill, offset, n);
            _position += n;
            return n;
        }

        public override long Seek(long offset, SeekOrigin origin) => _position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            _ => length + offset,
        };

        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
