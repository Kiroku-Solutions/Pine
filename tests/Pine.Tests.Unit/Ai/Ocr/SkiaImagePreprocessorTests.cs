using FluentAssertions;
using Pine.Infrastructure.Ai.Vision;
using SkiaSharp;

namespace Pine.Tests.Unit.Ai.Ocr;

public sealed class SkiaImagePreprocessorTests
{
    private static BgrImage Solid(int w, int h, byte b, byte g, byte r)
    {
        var data = new byte[w * h * 3];
        for (var i = 0; i < w * h; i++)
        {
            data[i * 3] = b;
            data[(i * 3) + 1] = g;
            data[(i * 3) + 2] = r;
        }

        return new BgrImage(w, h, data);
    }

    [Fact]
    public void Decode_returns_bgr_in_the_images_own_size()
    {
        var png = File.ReadAllBytes(Path.Combine(OcrTestAssets.FixtureDirectory(), "onnx_ocr_receipt_synthetic.png"));

        var image = SkiaImagePreprocessor.Decode(png);

        image.Width.Should().Be(520);
        image.Height.Should().Be(440);
        image.Data.Should().HaveCount(520 * 440 * 3);
        image.Data[0].Should().Be(255, "the corner is white paper");
    }

    [Fact]
    public void Decode_keeps_the_channel_order_blue_green_red()
    {
        using var bitmap = new SKBitmap(2, 1, SKColorType.Rgba8888, SKAlphaType.Opaque);
        bitmap.SetPixel(0, 0, new SKColor(200, 100, 50));
        bitmap.SetPixel(1, 0, new SKColor(10, 20, 30));
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);

        var bgr = SkiaImagePreprocessor.Decode(encoded.ToArray());

        bgr.Data.Take(3).Should().Equal(50, 100, 200);
        bgr.Data.Skip(3).Take(3).Should().Equal(30, 20, 10);
    }

    [Fact]
    public void Decode_refuses_bytes_that_are_not_an_image()
    {
        var act = () => SkiaImagePreprocessor.Decode([1, 2, 3, 4]);

        act.Should().Throw<InvalidDataException>();
    }

    // A 2 wide x 3 tall image; pixel (x, y) holds the value 10 * y + x in the blue channel.
    private static byte[] Bgra2x3()
    {
        var data = new byte[2 * 3 * 4];
        for (var y = 0; y < 3; y++)
        {
            for (var x = 0; x < 2; x++)
            {
                data[(((y * 2) + x) * 4)] = (byte)((10 * y) + x);
                data[(((y * 2) + x) * 4) + 3] = 255;
            }
        }

        return data;
    }

    private static int[] Blue(BgrImage image) => Enumerable.Range(0, image.Width * image.Height).Select(i => (int)image.Data[i * 3]).ToArray();

    [Theory]
    [InlineData(SKEncodedOrigin.TopLeft, 2, 3, new[] { 0, 1, 10, 11, 20, 21 })]
    [InlineData(SKEncodedOrigin.TopRight, 2, 3, new[] { 1, 0, 11, 10, 21, 20 })]
    [InlineData(SKEncodedOrigin.BottomRight, 2, 3, new[] { 21, 20, 11, 10, 1, 0 })]
    [InlineData(SKEncodedOrigin.BottomLeft, 2, 3, new[] { 20, 21, 10, 11, 0, 1 })]
    [InlineData(SKEncodedOrigin.LeftTop, 3, 2, new[] { 0, 10, 20, 1, 11, 21 })]
    [InlineData(SKEncodedOrigin.RightTop, 3, 2, new[] { 20, 10, 0, 21, 11, 1 })]
    [InlineData(SKEncodedOrigin.RightBottom, 3, 2, new[] { 21, 11, 1, 20, 10, 0 })]
    [InlineData(SKEncodedOrigin.LeftBottom, 3, 2, new[] { 1, 11, 21, 0, 10, 20 })]
    public void Reorient_applies_every_exif_orientation(SKEncodedOrigin origin, int width, int height, int[] expectedBlue)
    {
        var image = SkiaImagePreprocessor.Reorient(Bgra2x3(), 2, 3, 2 * 4, origin);

        image.Width.Should().Be(width);
        image.Height.Should().Be(height);
        Blue(image).Should().Equal(expectedBlue);
    }

    [Theory]
    [InlineData(2000, 1000, 960, 480)]
    [InlineData(1000, 2000, 480, 960)]
    [InlineData(500, 300, 512, 288)]
    [InlineData(10, 10, 32, 32)]
    public void Detector_input_is_limited_to_960_and_rounded_to_multiples_of_32(int w, int h, int expectedW, int expectedH)
    {
        var input = SkiaImagePreprocessor.DetPreprocess(Solid(w, h, 0, 0, 0));

        input.Width.Should().Be(expectedW);
        input.Height.Should().Be(expectedH);
        input.Tensor.Should().HaveCount(3 * expectedW * expectedH);
    }

    [Fact]
    public void Detector_input_normalises_the_blue_channel_first_with_the_imagenet_statistics_as_is()
    {
        // Pure blue in BGR order (255, 0, 0): channel 0 uses mean 0.485 / std 0.229 even though it is blue.
        var input = SkiaImagePreprocessor.DetPreprocess(Solid(32, 32, 255, 0, 0));
        var plane = 32 * 32;

        input.Tensor[0].Should().BeApproximately((1f - 0.485f) / 0.229f, 1e-4f);
        input.Tensor[plane].Should().BeApproximately((0f - 0.456f) / 0.224f, 1e-4f);
        input.Tensor[2 * plane].Should().BeApproximately((0f - 0.406f) / 0.225f, 1e-4f);
    }

    [Fact]
    public void An_axis_aligned_crop_copies_the_pixels_it_covers()
    {
        var image = Solid(100, 60, 0, 0, 0);
        for (var y = 20; y < 40; y++)
        {
            for (var x = 10; x < 70; x++)
            {
                image.Data[((y * 100) + x) * 3 + 2] = 200;
            }
        }

        var crop = SkiaImagePreprocessor.CropLine(image, [new(10, 20), new(70, 20), new(70, 40), new(10, 40)]);

        crop.Width.Should().Be(60);
        crop.Height.Should().Be(20);
        crop.Pixels[2].Should().BeApproximately(200, 0.5f);
        crop.Pixels[((10 * 60) + 30) * 3 + 2].Should().BeApproximately(200, 0.5f);
    }

    [Fact]
    public void A_tall_crop_is_rotated_to_lie_flat()
    {
        var image = Solid(100, 100, 0, 0, 0);

        var crop = SkiaImagePreprocessor.CropLine(image, [new(10, 10), new(30, 10), new(30, 90), new(10, 90)]);

        crop.Width.Should().Be(80);
        crop.Height.Should().Be(20);
    }

    [Fact]
    public void Recogniser_input_is_48_high_and_at_least_320_wide_with_zero_padding()
    {
        var crop = new LineCrop(new float[100 * 24 * 3], 100, 24);
        Array.Fill(crop.Pixels, 255f);

        var input = SkiaImagePreprocessor.RecTensor(crop);

        input.Width.Should().Be(320);
        input.Tensor.Should().HaveCount(3 * 48 * 320);
        input.Tensor[0].Should().BeApproximately(1f, 1e-5f, "white maps to +1");
        input.Tensor[319].Should().Be(0f, "the padding is zero");
    }

    [Fact]
    public void A_long_line_widens_the_recogniser_input_up_to_the_cap()
    {
        var wide = SkiaImagePreprocessor.RecTensor(new LineCrop(new float[1000 * 20 * 3], 1000, 20));
        var huge = SkiaImagePreprocessor.RecTensor(new LineCrop(new float[8000 * 20 * 3], 8000, 20));

        wide.Width.Should().Be(2400);
        huge.Width.Should().Be(3200);
    }

    [Fact]
    public void The_homography_of_a_translation_maps_points_by_that_translation()
    {
        var h = SkiaImagePreprocessor.Homography(
            [new(0, 0), new(10, 0), new(10, 5), new(0, 5)],
            [new(3, 4), new(13, 4), new(13, 9), new(3, 9)]);

        h[0].Should().BeApproximately(1, 1e-9);
        h[2].Should().BeApproximately(3, 1e-9);
        h[5].Should().BeApproximately(4, 1e-9);
        h[6].Should().BeApproximately(0, 1e-9);
    }
}
