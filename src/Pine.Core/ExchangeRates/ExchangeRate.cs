using System;
using Pine.Core.Common;
using Pine.Core.Common.ValueObjects;

namespace Pine.Core.ExchangeRates;

/// <summary>Why a rate cannot be stored.</summary>
public enum ExchangeRateProblem
{
    /// <summary>Nothing is wrong.</summary>
    None,

    /// <summary>The rate is zero or negative: a conversion by it would erase or invert money.</summary>
    NotPositive,

    /// <summary>The rate has more decimals than <see cref="ExchangeRate.RateScale"/>; the store would have to round it.</summary>
    TooPrecise,

    /// <summary>The rate exceeds <see cref="ExchangeRate.MaximumRate"/>; almost certainly the pair typed backwards or a slipped decimal point.</summary>
    TooLarge,
}

/// <summary>
/// A manually entered conversion rate between two currencies, in force from a date.
/// </summary>
/// <remarks>
/// <para>
/// PINE IS OFFLINE-FIRST, SO NOTHING FETCHES A RATE (owner decision, 2026-10-14). A person types it; the source
/// is always <see cref="ManualSource"/>. There is no HTTP client anywhere near this type and there must not be.
/// </para>
/// <para>
/// ONE UNIT OF <see cref="FromCurrency"/> IS WORTH <see cref="Rate"/> UNITS OF <see cref="ToCurrency"/>.
/// The rate is directional and its inverse is NOT derived: an inverse of a rounded rate is a different number,
/// and a conversion that quietly used one would be using a rate nobody entered.
/// </para>
/// <para>
/// THE SCALE POLICY. A rate is stored as a count of 10^-<see cref="RateScale"/> in an INTEGER and is refused,
/// not rounded, when it has more decimals (a manual entry with a ninth decimal is a typo, not noise), when it
/// is not positive, or when it exceeds <see cref="MaximumRate"/>. Eight decimals keep 0.00000001 apart from
/// zero, which covers every pair between circulating currencies.
/// </para>
/// <para>
/// THE EFFECTIVE DATE IS A UTC DAY. It is stored as that day's midnight so that the natural key
/// (pair, day) is exact and a rate is "in force" from the first instant of its day.
/// </para>
/// </remarks>
public sealed class ExchangeRate : AggregateRoot
{
    /// <summary>Decimals a stored rate keeps.</summary>
    public const int RateScale = 8;

    /// <summary>The largest rate accepted.</summary>
    public const decimal MaximumRate = 1_000_000_000m;

    /// <summary>The only source there is: a person typed it.</summary>
    public const string ManualSource = "manual";

    /// <summary>The currency being converted from.</summary>
    public CurrencyCode FromCurrency { get; private set; }

    /// <summary>The currency being converted to.</summary>
    public CurrencyCode ToCurrency { get; private set; }

    /// <summary>Units of <see cref="ToCurrency"/> per one unit of <see cref="FromCurrency"/>.</summary>
    public decimal Rate { get; private set; }

    /// <summary>The UTC day (at midnight) from which this rate applies.</summary>
    public DateTime EffectiveDateUtc { get; private set; }

    /// <summary>Where the rate came from. Always <see cref="ManualSource"/>.</summary>
    public string Source { get; private set; }

    private ExchangeRate()
    {
        FromCurrency = null!;
        ToCurrency = null!;
        Source = ManualSource;
    }

    private ExchangeRate(Guid id, CurrencyCode from, CurrencyCode to, decimal rate, DateTime effectiveDateUtc)
    {
        Id = id;
        FromCurrency = from;
        ToCurrency = to;
        Rate = rate;
        EffectiveDateUtc = effectiveDateUtc;
        Source = ManualSource;
    }

    /// <summary>What is wrong with <paramref name="rate"/>, or <see cref="ExchangeRateProblem.None"/>.</summary>
    /// <param name="rate">The candidate rate.</param>
    public static ExchangeRateProblem Check(decimal rate)
    {
        if (rate <= 0m)
        {
            return ExchangeRateProblem.NotPositive;
        }

        if (rate > MaximumRate)
        {
            return ExchangeRateProblem.TooLarge;
        }

        return decimal.Round(rate, RateScale, MidpointRounding.ToEven) != rate
            ? ExchangeRateProblem.TooPrecise
            : ExchangeRateProblem.None;
    }

    /// <summary>The UTC day (midnight) <paramref name="instant"/> falls on.</summary>
    /// <param name="instant">Any instant; an unspecified kind is taken to be UTC.</param>
    public static DateTime DayOf(DateTime instant)
    {
        var utc = instant.Kind == DateTimeKind.Local ? instant.ToUniversalTime() : instant;
        return DateTime.SpecifyKind(utc.Date, DateTimeKind.Utc);
    }

    /// <summary>Creates a rate.</summary>
    /// <exception cref="ArgumentException">The pair is not two different currencies, or the rate breaks the scale policy.</exception>
    public static ExchangeRate Create(CurrencyCode from, CurrencyCode to, decimal rate, DateTime effectiveDateUtc)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);

        if (from == to)
        {
            throw new ArgumentException("A rate converts between two different currencies.", nameof(to));
        }

        RequireValid(rate);
        return new ExchangeRate(Guid.NewGuid(), from, to, rate, DayOf(effectiveDateUtc));
    }

    /// <summary>Replaces the rate of the same pair and day.</summary>
    /// <exception cref="ArgumentException">The rate breaks the scale policy.</exception>
    public void ChangeRate(decimal rate)
    {
        RequireValid(rate);
        Rate = rate;
    }

    private static void RequireValid(decimal rate)
    {
        var problem = Check(rate);
        if (problem != ExchangeRateProblem.None)
        {
            throw new ArgumentException($"{rate} is not an acceptable exchange rate ({problem}).", nameof(rate));
        }
    }
}
