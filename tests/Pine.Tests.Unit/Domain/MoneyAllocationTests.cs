using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Pine.Core.Common.ValueObjects;
using Xunit;

namespace Pine.Tests.Unit.Domain;

/// <summary>
/// TD-18, second half: <see cref="Money.Allocate(int)"/> — splitting an amount so the parts sum to
/// EXACTLY the original.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS IS THE REASON THE TD-18 ROW EXISTS. Divide-and-round is the obvious implementation and
/// it is wrong: three shares of 10.00 / 3 each round to 3.33 and sum to 9.99, so a cent of the
/// user's money disappears. Round the other way and a cent is conjured out of nothing. Either
/// outcome silently breaks <c>Transaction.ValidateSplits</c>, which is the ledger's own definition
/// of a consistent transaction. Allocation is a PARTITION of a fixed number of minor units, and its
/// correctness condition is a property, not an example — which is why most of this file is a sweep
/// rather than a table.
/// </para>
/// <para>
/// THE SWEEP IS EXHAUSTIVE OVER A DELIBERATELY NASTY GRID, not random. A seeded random generator
/// would make a failure depend on the seed; enumerating the grid means a red test names the exact
/// triple that broke.
/// </para>
/// </remarks>
public class MoneyAllocationTests
{
    private static CurrencyCode Usd => new("USD");
    private static CurrencyCode Jpy => new("JPY");
    private static CurrencyCode Kwd => new("KWD");

    // ---------------------------------------------------------------- the examples

    /// <summary>The example named in the ticket. The extra cent goes to the FIRST part.</summary>
    [Fact]
    public void Allocate_TenDollarsAcrossThree_IsThreeThirtyFourAndTwoThirtyThrees()
    {
        var parts = new Money(10.00m, Usd).Allocate(3);

        parts.Select(p => p.Amount).Should().Equal(3.34m, 3.33m, 3.33m);
    }

    [Fact]
    public void Allocate_ByRatios_FollowsTheRatios()
    {
        // 5.00 split 1:1:1 -> 500 cents / 3 = 166 each, 2 left over, to the first two.
        new Money(5.00m, Usd).Allocate(1, 1, 1)
            .Select(p => p.Amount).Should().Equal(1.67m, 1.67m, 1.66m);

        // The classic: 0.05 split 3:7.
        new Money(0.05m, Usd).Allocate(3, 7)
            .Select(p => p.Amount).Should().Equal(0.02m, 0.03m);
    }

    /// <summary>A ratio of zero earns nothing and can never be handed a leftover unit.</summary>
    [Fact]
    public void Allocate_GivesNothingToAZeroRatio()
    {
        var parts = new Money(10.00m, Usd).Allocate(1, 0, 1, 0);

        parts.Select(p => p.Amount).Should().Equal(5.00m, 0.00m, 5.00m, 0.00m);

        // ...even when there IS a leftover unit to place.
        new Money(10.01m, Usd).Allocate(1, 0, 1)
            .Select(p => p.Amount).Should().Equal(5.01m, 0.00m, 5.00m);
    }

    /// <summary>A negative amount mirrors the positive one exactly, part for part.</summary>
    [Fact]
    public void Allocate_IsSignSymmetric()
    {
        var positive = new Money(10.00m, Usd).Allocate(3).Select(p => p.Amount).ToArray();
        var negative = new Money(-10.00m, Usd).Allocate(3).Select(p => p.Amount).ToArray();

        negative.Should().Equal(positive.Select(a => -a));
        negative.Should().Equal(-3.34m, -3.33m, -3.33m);
    }

    [Fact]
    public void Allocate_WorksInAZeroMinorUnitCurrency()
    {
        new Money(10m, Jpy).Allocate(3).Select(p => p.Amount).Should().Equal(4m, 3m, 3m);
        new Money(100m, Jpy).Allocate(3).Select(p => p.Amount).Should().Equal(34m, 33m, 33m);
    }

    [Fact]
    public void Allocate_WorksInAThreeMinorUnitCurrency()
    {
        new Money(1.000m, Kwd).Allocate(3).Select(p => p.Amount).Should().Equal(0.334m, 0.333m, 0.333m);
    }

    [Fact]
    public void Allocate_OfZero_IsAllZeroes()
    {
        Money.Zero(Usd).Allocate(4).Should().AllSatisfy(p => p.Amount.Should().Be(0m));
    }

    [Fact]
    public void Allocate_AcrossOnePart_ReturnsTheOriginal()
    {
        new Money(7.77m, Usd).Allocate(1).Should().ContainSingle()
            .Which.Should().Be(new Money(7.77m, Usd));
    }

    // ---------------------------------------------------------------- the property

    /// <summary>
    /// THE PROPERTY. Over a wide sweep of amounts, currencies (2, 0 and 3 minor units), signs and
    /// part counts, the parts sum to exactly the original — and every part is itself a normalised,
    /// same-currency amount. This is the assertion the TD-18 row is asking for; the examples above
    /// only illustrate it.
    /// </summary>
    [Fact]
    public void Allocate_EqualParts_AlwaysSumsBackToTheOriginal()
    {
        var failures = new List<string>();

        foreach (var currency in new[] { Usd, Jpy, Kwd })
        {
            foreach (var magnitude in MagnitudesFor(currency))
            {
                foreach (var sign in new[] { 1m, -1m })
                {
                    var original = new Money(sign * magnitude, currency);

                    for (var parts = 1; parts <= 13; parts++)
                    {
                        var allocated = original.Allocate(parts);

                        if (allocated.Length != parts)
                        {
                            failures.Add($"{original} / {parts}: got {allocated.Length} parts");
                            continue;
                        }

                        var sum = allocated.Aggregate(Money.Zero(currency), (a, b) => a + b);

                        if (sum != original)
                        {
                            failures.Add($"{original} / {parts}: parts sum to {sum}");
                        }

                        if (allocated.Any(p => p.Currency != currency))
                        {
                            failures.Add($"{original} / {parts}: a part changed currency");
                        }

                        // No part may be out of scale, and no part may have picked up the wrong sign.
                        if (allocated.Any(p => p.Amount != Money.Round(p.Amount, currency)))
                        {
                            failures.Add($"{original} / {parts}: a part is out of scale");
                        }

                        if (allocated.Any(p => sign > 0 ? p.Amount < 0 : p.Amount > 0))
                        {
                            failures.Add($"{original} / {parts}: a part has the wrong sign");
                        }
                    }
                }
            }
        }

        failures.Should().BeEmpty();
    }

    /// <summary>
    /// The same property under ratios, including ratios containing zeroes, wildly unequal ratios and
    /// ratios that divide evenly. The sum must still be exact, and a zero ratio must still get zero.
    /// </summary>
    [Fact]
    public void Allocate_ByRatios_AlwaysSumsBackToTheOriginal()
    {
        int[][] ratioSets =
        [
            [1], [1, 1], [1, 2], [2, 1], [1, 1, 1], [3, 7], [7, 3],
            [1, 0], [0, 1], [1, 0, 1], [0, 0, 1], [1, 0, 0, 5],
            [1, 2, 3, 4, 5], [100, 1], [1, 100], [999, 1, 1],
            [2, 2, 2, 2], [5, 5], [1, 1, 1, 1, 1, 1, 1],
        ];

        var failures = new List<string>();

        foreach (var currency in new[] { Usd, Jpy, Kwd })
        {
            foreach (var magnitude in MagnitudesFor(currency))
            {
                foreach (var sign in new[] { 1m, -1m })
                {
                    var original = new Money(sign * magnitude, currency);

                    foreach (var ratios in ratioSets)
                    {
                        var allocated = original.Allocate(ratios);
                        var sum = allocated.Aggregate(Money.Zero(currency), (a, b) => a + b);

                        if (sum != original)
                        {
                            failures.Add($"{original} / [{string.Join(",", ratios)}]: parts sum to {sum}");
                        }

                        for (var i = 0; i < ratios.Length; i++)
                        {
                            if (ratios[i] == 0 && allocated[i].Amount != 0m)
                            {
                                failures.Add($"{original} / [{string.Join(",", ratios)}]: zero ratio got {allocated[i]}");
                            }
                        }
                    }
                }
            }
        }

        failures.Should().BeEmpty();
    }

    /// <summary>
    /// Allocation is deterministic: the same input yields the same parts in the same order. A
    /// tie-broken-by-position rule that relied on an unstable sort would fail here intermittently,
    /// which is why the comparison key carries the position explicitly.
    /// </summary>
    [Fact]
    public void Allocate_IsDeterministic()
    {
        var original = new Money(1.00m, Usd);

        var first = original.Allocate(7).Select(p => p.Amount).ToArray();

        for (var attempt = 0; attempt < 20; attempt++)
        {
            original.Allocate(7).Select(p => p.Amount).Should().Equal(first);
        }

        // 100 cents over 7 -> 14 each (98), 2 left over, to the first two by position.
        first.Should().Equal(0.15m, 0.15m, 0.14m, 0.14m, 0.14m, 0.14m, 0.14m);
    }

    /// <summary>
    /// A leftover unit goes to the LARGEST remainder, not simply to the earliest part. With ratios
    /// 1:1:4 over 7 cents the remainders differ, so position is not what decides.
    /// </summary>
    [Fact]
    public void Allocate_PlacesLeftoversByRemainder_NotMerelyByPosition()
    {
        // 7 cents, ratios 1:1:4 (sum 6): shares 7/6=1 r1, 1 r1, 28/6=4 r4. One cent left, and the
        // r=4 part is third. A position-first rule would hand it to the first part instead.
        new Money(0.07m, Usd).Allocate(1, 1, 4)
            .Select(p => p.Amount).Should().Equal(0.01m, 0.01m, 0.05m);
    }

    // ---------------------------------------------------------------- rejections

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Allocate_Rejects_ANonPositivePartCount(int parts)
    {
        Action act = () => _ = new Money(1.00m, Usd).Allocate(parts);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Allocate_Rejects_AnEmptyOrAllZeroOrNegativeRatioSet()
    {
        var money = new Money(1.00m, Usd);

        ((Action)(() => _ = money.Allocate([]))).Should().Throw<ArgumentException>();
        ((Action)(() => _ = money.Allocate(0, 0))).Should().Throw<ArgumentException>()
            .WithMessage("*must not all be zero*");
        ((Action)(() => _ = money.Allocate(1, -1))).Should().Throw<ArgumentException>()
            .WithMessage("*cannot be negative*");
    }

    private static IEnumerable<decimal> MagnitudesFor(CurrencyCode currency) =>
        currency.MinorUnits == 0
            ? [0m, 1m, 2m, 3m, 7m, 10m, 99m, 100m, 101m, 1_000m, 123_456m]
            : currency.MinorUnits == 3
                ? [0m, 0.001m, 0.007m, 0.010m, 1.000m, 1.001m, 9.999m, 1234.567m]
                : [0m, 0.01m, 0.02m, 0.03m, 0.05m, 0.07m, 0.10m, 0.99m, 1.00m,
                   1.01m, 3.33m, 9.99m, 10.00m, 10.01m, 99.99m, 100.00m, 12345.67m];
}
