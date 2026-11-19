using FluentAssertions;
using Pine.Infrastructure.Ai.Extraction.LayoutLm;

namespace Pine.Tests.Unit.Ai.LayoutLm;

public sealed class LayoutLmDecoderTests
{
    private const int O = 0, BM = 1, IM = 2, BD = 3, ID = 4, BT = 5, IT = 6, BX = 7;

    private static float[] Hot(int label, float p = 0.9f)
    {
        var v = new float[9];
        Array.Fill(v, (1f - p) / 8f);
        v[label] = p;
        return v;
    }

    [Fact]
    public void Softmax_sums_to_one_and_is_stable_for_large_logits()
    {
        var p = LayoutLmDecoder.Softmax([1000f, 1001f, 999f, 0, 0, 0, 0, 0, 0]);

        p.Sum().Should().BeApproximately(1f, 1e-5f);
        p[1].Should().BeGreaterThan(p[0]);
        p.Should().OnlyContain(x => !float.IsNaN(x));
    }

    [Fact]
    public void Decodes_one_span_per_field_with_mean_probability()
    {
        string[] words = ["SUPER", "MART", "x", "12/03/2026", "TOTAL", "$", "9.50", "TAX", "0.90"];
        var probs = new List<float[]?>
        {
            Hot(BM, 0.8f), Hot(IM, 0.6f), Hot(O), Hot(BD, 0.99f), Hot(O), Hot(O), Hot(BT, 0.7f), Hot(O), Hot(BX, 0.5f),
        };

        var spans = LayoutLmDecoder.Decode(words, probs);

        spans[LayoutLmField.Merchant].Text.Should().Be("SUPER MART");
        spans[LayoutLmField.Merchant].Probability.Should().BeApproximately(0.7, 1e-6);
        spans[LayoutLmField.Date].Text.Should().Be("12/03/2026");
        spans[LayoutLmField.Total].Text.Should().Be("9.50");
        spans[LayoutLmField.Tax].Text.Should().Be("0.90");
    }

    [Fact]
    public void Tolerates_an_I_tag_without_a_B_tag()
    {
        var spans = LayoutLmDecoder.Decode(["a", "b"], [Hot(IT), Hot(IT)]);

        spans[LayoutLmField.Total].Text.Should().Be("a b");
        spans[LayoutLmField.Total].WordCount.Should().Be(2);
    }

    [Fact]
    public void A_B_tag_starts_a_new_span_and_the_most_probable_span_wins()
    {
        var spans = LayoutLmDecoder.Decode(["weak", "o", "strong"], [Hot(BT, 0.55f), Hot(O), Hot(BT, 0.95f)]);

        spans[LayoutLmField.Total].Text.Should().Be("strong");
    }

    [Fact]
    public void An_I_tag_of_another_field_does_not_extend_the_open_span()
    {
        var spans = LayoutLmDecoder.Decode(["a", "b"], [Hot(BM), Hot(ID)]);

        spans[LayoutLmField.Merchant].Text.Should().Be("a");
        spans[LayoutLmField.Date].Text.Should().Be("b");
    }

    [Fact]
    public void An_unscored_word_ends_the_span_and_nothing_found_gives_an_empty_result()
    {
        var spans = LayoutLmDecoder.Decode(["a", "gap", "b"], [Hot(BM), null, Hot(IM)]);

        spans[LayoutLmField.Merchant].WordCount.Should().Be(1);
        LayoutLmDecoder.Decode(["x"], [Hot(O)]).Should().BeEmpty();
    }

    [Fact]
    public void Mismatched_lengths_are_rejected()
    {
        var act = () => LayoutLmDecoder.Decode(["a"], new List<float[]?>());

        act.Should().Throw<ArgumentException>();
    }
}
