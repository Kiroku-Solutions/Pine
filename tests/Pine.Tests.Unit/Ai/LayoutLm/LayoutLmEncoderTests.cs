using FluentAssertions;
using Pine.Core.Ai;
using Pine.Infrastructure.Ai.Extraction.LayoutLm;

namespace Pine.Tests.Unit.Ai.LayoutLm;

public sealed class LayoutLmEncoderTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "pine-layoutlm-" + Guid.NewGuid().ToString("N"));
    private readonly LayoutLmTokenizer _tokenizer;

    public LayoutLmEncoderTests()
    {
        Directory.CreateDirectory(_directory);
        _tokenizer = LayoutLmTokenizer.Load(LayoutLmTestAssets.WriteSyntheticTokenizer(_directory));
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Theory]
    [InlineData(0f, 0f, 100f, 50f, 200, 100, 0, 0, 500, 500)]
    [InlineData(-5f, 0f, 400f, 120f, 200, 100, 0, 0, 1000, 1000)]
    [InlineData(100f, 50f, 0f, 0f, 200, 100, 0, 0, 500, 500)]
    // Python round() is half-to-even: 0.5 -> 0, 1.5 -> 2, 2.5 -> 2.
    [InlineData(0.5f, 1.5f, 2.5f, 3f, 1000, 1000, 0, 2, 2, 3)]
    public void NormalizeBox_matches_the_training_rules(float x0, float y0, float x1, float y1, int w, int h, int ex0, int ey0, int ex1, int ey1)
    {
        LayoutLmEncoder.NormalizeBox(x0, y0, x1, y1, w, h).Should().Equal(ex0, ey0, ex1, ey1);
    }

    [Fact]
    public void A_short_document_is_one_window_with_special_tokens_and_zero_boxes()
    {
        var words = new List<OcrWord>
        {
            LayoutLmTestAssets.Word("AB", 10, 20, 100, 30),
            LayoutLmTestAssets.Word("   ", 0, 0, 1, 1),
            LayoutLmTestAssets.Word("C", 10, 60, 50, 30),
        };

        var chunks = LayoutLmEncoder.Encode(words, 200, 100, _tokenizer, out var kept);

        kept.Should().Equal(0, 2);
        chunks.Should().ContainSingle();
        var c = chunks[0];
        c.InputIds[0].Should().Be(0);
        c.InputIds[^1].Should().Be(2);
        c.Boxes[0].Should().Equal(0, 0, 0, 0);
        c.Boxes[^1].Should().Equal(0, 0, 0, 0);
        // " AB" = 3 byte tokens, " C" = 2: word ids and first-sub-token flags.
        c.WordIds.Should().Equal(-1, 0, 0, 0, 1, 1, -1);
        c.IsFirstSubtoken.Should().Equal(false, true, false, false, true, false, false);
        c.Boxes[1].Should().Equal(50, 200, 550, 500);
    }

    [Fact]
    public void Empty_input_gives_no_windows()
    {
        LayoutLmEncoder.Encode([], 100, 100, _tokenizer, out _).Should().BeEmpty();
    }

    [Fact]
    public void A_long_document_is_split_with_stride_128_and_every_word_is_covered()
    {
        // 700 words of 2 tokens each (" a" -> 2 byte tokens) = 1400 content tokens.
        var words = Enumerable.Range(0, 700).Select(i => LayoutLmTestAssets.Word("a", 0, i, 10, 5)).ToList();

        var chunks = LayoutLmEncoder.Encode(words, 100, 1000, _tokenizer, out _);

        chunks.Should().HaveCount(4);
        chunks.Take(3).Should().OnlyContain(c => c.InputIds.Length == 512);
        // Windows advance by 510 - 128 = 382 content tokens (191 words), so each shares 128 tokens with the next.
        chunks[1].WordIds[1].Should().Be(191);
        chunks.SelectMany(c => c.WordIds).Where(w => w >= 0).Distinct().Should().HaveCount(700);
    }

    [Fact]
    public void The_number_of_windows_is_capped()
    {
        var words = Enumerable.Range(0, 5000).Select(i => LayoutLmTestAssets.Word("a", 0, i, 10, 5)).ToList();

        LayoutLmEncoder.Encode(words, 100, 5000, _tokenizer, out _).Should().HaveCount(LayoutLmEncoder.MaxChunks);
    }
}
