using System;
using FluentAssertions;
using OpenCvSharp;
using Pine.Infrastructure.Ai.Vision;
using Xunit;

namespace Pine.Tests.Unit.Infrastructure.Ai;

/// <summary>
/// The pixel copy the live scanner paints from, checked against synthetic images (TD-120).
/// </summary>
/// <remarks>
/// This conversion used to be inline in the view model, copying <c>stride × height</c> bytes in one
/// block into a bitmap whose own stride it never compared. It is tested here because no test may turn a
/// real camera on.
/// </remarks>
public class OpenCvCameraFrameTests
{
    [Fact]
    public void AColourFrame_IsCopiedAsOpaqueBgra_RowByRow_IntoAPaddedDestination()
    {
        using var mat = new Mat(2, 3, MatType.CV_8UC3);
        for (var row = 0; row < 2; row++)
        {
            for (var column = 0; column < 3; column++)
            {
                mat.Set(row, column, new Vec3b((byte)(10 * row + column), (byte)(100 + column), (byte)(200 + row)));
            }
        }

        using var frame = new OpenCvCameraSource.Frame(mat.Clone());
        const int Stride = 16; // 4 bytes of padding per row, as a bitmap may have
        var destination = new byte[Stride * 2];
        Array.Fill(destination, (byte)0xAB);

        frame.CopyBgraTo(destination, Stride);

        for (var row = 0; row < 2; row++)
        {
            for (var column = 0; column < 3; column++)
            {
                var at = (row * Stride) + (column * 4);
                destination[at..(at + 4)].Should().Equal(
                    [(byte)(10 * row + column), (byte)(100 + column), (byte)(200 + row), (byte)255],
                    $"pixel ({row},{column}) is B, G, R and an opaque alpha, at its row's own stride");
            }

            destination[((row * Stride) + 12)..((row * Stride) + 16)].Should().OnlyContain(b => b == 0xAB,
                "the padding at the end of a row belongs to the destination and is not written");
        }
    }

    [Fact]
    public void AGreyFrame_IsCopiedAsGreyInEveryChannel()
    {
        using var mat = new Mat(1, 2, MatType.CV_8UC1, new Scalar(77));
        using var frame = new OpenCvCameraSource.Frame(mat.Clone());
        var destination = new byte[8];

        frame.CopyBgraTo(destination, 8);

        destination.Should().Equal(77, 77, 77, 255, 77, 77, 77, 255);
    }

    [Fact]
    public void ADestinationTooSmall_IsRefused_BeforeAnythingIsWritten()
    {
        using var mat = new Mat(2, 2, MatType.CV_8UC3, new Scalar(1, 2, 3));
        using var frame = new OpenCvCameraSource.Frame(mat.Clone());

        var tooShort = () => frame.CopyBgraTo(new byte[15], 8);
        var narrowStride = () => frame.CopyBgraTo(new byte[64], 7);

        tooShort.Should().Throw<ArgumentException>();
        narrowStride.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void TheEncodings_AreRealImages()
    {
        using var mat = new Mat(4, 4, MatType.CV_8UC3, new Scalar(0, 128, 255));
        using var frame = new OpenCvCameraSource.Frame(mat.Clone());

        frame.EncodePng().Should().StartWith(new byte[] { 0x89, 0x50, 0x4E, 0x47 });
        frame.EncodeJpeg().Should().StartWith(new byte[] { 0xFF, 0xD8, 0xFF });
    }
}
