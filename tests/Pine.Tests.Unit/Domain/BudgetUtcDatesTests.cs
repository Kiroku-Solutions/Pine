using System;
using FluentAssertions;
using Pine.Core.Budgets;
using Pine.Core.Common.ValueObjects;
using Xunit;

namespace Pine.Tests.Unit.Domain;

/// <summary>TD-96: the UTC twins are derived from the dates in the only place they are assigned.</summary>
public class BudgetUtcDatesTests
{
    private static readonly Money Limit = new(100m, CurrencyCode.FromStoredValue("USD"));

    [Fact]
    public void Create_WithOffsetDates_DerivesUtcInstants_AndKeepsTheOriginalOffsets()
    {
        var start = new DateTimeOffset(2026, 5, 1, 23, 0, 0, TimeSpan.FromHours(2));
        var end = new DateTimeOffset(2026, 5, 20, 18, 0, 0, TimeSpan.FromHours(-5));

        var budget = Budget.Create("Food", Limit, BudgetPeriod.Custom, start, end);

        budget.StartDateUtc.Should().Be(new DateTime(2026, 5, 1, 21, 0, 0, DateTimeKind.Utc));
        budget.EndDateUtc.Should().Be(new DateTime(2026, 5, 20, 23, 0, 0, DateTimeKind.Utc));
        budget.StartDate.Should().Be(start);
        budget.StartDate!.Value.Offset.Should().Be(TimeSpan.FromHours(2));
    }

    [Fact]
    public void Create_WithoutDates_LeavesTheUtcTwinsNull()
    {
        var budget = Budget.Create("Food", Limit, BudgetPeriod.Monthly);

        budget.StartDateUtc.Should().BeNull();
        budget.EndDateUtc.Should().BeNull();
    }
}
