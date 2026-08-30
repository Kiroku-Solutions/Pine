using System;
using System.Globalization;
using FluentAssertions;
using Pine.Core.Ai;
using Xunit;

namespace Pine.Tests.Unit.Ai;

public class ReceiptDateParserTests
{
    private static readonly CultureInfo DayFirst = new("es-EC");
    private static readonly CultureInfo MonthFirst = new("en-US");
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_day_first_culture_reads_an_ambiguous_date_day_first()
    {
        var candidate = ReceiptDateParser.Parse("FECHA: 05/03/2026", DayFirst, Now);

        candidate.Should().NotBeNull();
        candidate!.Value.Value.Month.Should().Be(3);
        candidate.Value.Value.Day.Should().Be(5);
    }

    [Fact]
    public void A_month_first_culture_reads_the_same_string_month_first()
    {
        var candidate = ReceiptDateParser.Parse("DATE 05/03/2026", MonthFirst, Now);

        candidate!.Value.Value.Month.Should().Be(5);
        candidate.Value.Value.Day.Should().Be(3);
    }

    [Fact]
    public void An_ambiguous_date_carries_the_other_reading()
    {
        var candidate = ReceiptDateParser.Parse("05/03/2026", DayFirst, Now);

        candidate!.Value.IsAmbiguous.Should().BeTrue();
        candidate.Value.Alternative.Should().NotBeNull();
        candidate.Value.Alternative!.Value.Month.Should().Be(5);
        candidate.Value.Alternative!.Value.Day.Should().Be(3);
    }

    [Fact]
    public void A_day_above_twelve_is_not_ambiguous()
    {
        var candidate = ReceiptDateParser.Parse("23/03/2026", DayFirst, Now);

        candidate!.Value.IsAmbiguous.Should().BeFalse();
        candidate.Value.Alternative.Should().BeNull();
        candidate.Value.Value.Day.Should().Be(23);
    }

    [Fact]
    public void An_iso_date_is_read_as_iso_whatever_the_culture()
    {
        var candidate = ReceiptDateParser.Parse("2026-03-05", MonthFirst, Now);

        candidate!.Value.Value.Month.Should().Be(3);
        candidate.Value.Value.Day.Should().Be(5);
        candidate.Value.IsAmbiguous.Should().BeFalse();
    }

    [Fact]
    public void Single_digit_components_are_accepted()
    {
        var candidate = ReceiptDateParser.Parse("5/3/2026", DayFirst, Now);

        candidate.Should().NotBeNull();
        candidate!.Value.Value.Day.Should().Be(5);
    }

    [Fact]
    public void A_two_digit_year_is_accepted()
    {
        var candidate = ReceiptDateParser.Parse("05/03/26", DayFirst, Now);

        candidate!.Value.Value.Year.Should().Be(2026);
    }

    [Fact]
    public void A_dot_separator_is_accepted()
    {
        var candidate = ReceiptDateParser.Parse("05.03.2026", DayFirst, Now);

        candidate.Should().NotBeNull();
    }

    [Fact]
    public void An_implausible_year_is_rejected_rather_than_returned()
    {
        ReceiptDateParser.Parse("05/03/1998", DayFirst, Now).Should().BeNull();
    }

    [Fact]
    public void The_alternative_is_dropped_when_it_is_implausible()
    {
        // 13 cannot be a month, so 13/07/2026 has exactly one reading.
        var candidate = ReceiptDateParser.Parse("13/07/2026", DayFirst, Now);

        candidate!.Value.IsAmbiguous.Should().BeFalse();
        candidate.Value.Alternative.Should().BeNull();
    }

    [Fact]
    public void Text_with_no_date_returns_null()
    {
        ReceiptDateParser.Parse("TOTAL 24.50", DayFirst, Now).Should().BeNull();
    }

    [Theory]
    [InlineData("es-EC", true)]
    [InlineData("en-GB", true)]
    [InlineData("en-US", false)]
    public void The_culture_decides_the_component_order(string cultureName, bool dayFirst)
    {
        ReceiptDateParser.PrefersDayFirst(new CultureInfo(cultureName)).Should().Be(dayFirst);
    }
}
