using System;
using System.Collections.Generic;
using FluentAssertions;
using Pine.Core.Ai;
using Xunit;

namespace Pine.Tests.Unit.Ai;

public class ReceiptFieldConfidenceTests
{
    [Theory]
    [InlineData(0.95, ConfidenceBand.High)]
    [InlineData(0.90, ConfidenceBand.High)]
    [InlineData(0.899, ConfidenceBand.Medium)]
    [InlineData(0.70, ConfidenceBand.Medium)]
    [InlineData(0.699, ConfidenceBand.Low)]
    [InlineData(0.0, ConfidenceBand.Low)]
    public void The_band_is_a_fixed_function_of_the_score(double score, ConfidenceBand expected)
    {
        FieldConfidence.BandFor(score).Should().Be(expected);
    }

    [Fact]
    public void A_field_is_worth_its_worst_read_word()
    {
        var confidence = FieldConfidence.FromWordScores([0.99f, 0.62f, 0.97f]);

        confidence.Score.Should().BeApproximately(0.62, 0.0001);
        confidence.Band.Should().Be(ConfidenceBand.Low);
    }

    [Fact]
    public void A_field_no_word_produced_is_NotFound_and_not_a_zero_score()
    {
        var confidence = FieldConfidence.FromWordScores([]);

        confidence.Band.Should().Be(ConfidenceBand.NotFound);
        confidence.Should().Be(FieldConfidence.NotFound);
    }

    [Fact]
    public void NotFound_is_distinguishable_from_a_badly_read_field()
    {
        FieldConfidence.NotFound.Band.Should().NotBe(FieldConfidence.FromWordScores([0.01f]).Band);
    }

    [Fact]
    public void An_empty_extraction_reports_every_field_NotFound()
    {
        ReceiptConfidence.None.Merchant.Band.Should().Be(ConfidenceBand.NotFound);
        ReceiptConfidence.None.Total.Band.Should().Be(ConfidenceBand.NotFound);
        ReceiptConfidence.None.Date.Band.Should().Be(ConfidenceBand.NotFound);
        ReceiptConfidence.None.LineItems.Should().BeEmpty();
    }

    [Fact]
    public void A_score_outside_zero_to_one_is_rejected()
    {
        var act = () => FieldConfidence.FromWordScores([1.4f]);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
