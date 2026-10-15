using System;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Pine.Core.ExchangeRates;

namespace Pine.Infrastructure.Data.Converters;

/// <summary>
/// Persists an exchange rate as a SQLite <c>INTEGER</c> count of 10^-8, the scale <see cref="ExchangeRate.RateScale"/>
/// fixes. Like <see cref="ScaledMoneyConverter"/>, the write path throws instead of rounding: a rate with a ninth
/// decimal reached the store only by bypassing the domain.
/// </summary>
public sealed class ScaledRateConverter : ValueConverter<decimal, long>
{
    private const decimal Factor = 100_000_000m;

    public ScaledRateConverter()
        : base(value => ToStorage(value), stored => FromStorage(stored))
    {
    }

    /// <summary>The rate as a count of 10^-8.</summary>
    /// <exception cref="InvalidOperationException">The rate has more than eight decimals.</exception>
    public static long ToStorage(decimal value)
    {
        var scaled = value * Factor;
        var truncated = decimal.Truncate(scaled);
        if (scaled != truncated)
        {
            throw new InvalidOperationException(
                $"{value} carries more than {ExchangeRate.RateScale} decimal places and cannot be stored exactly.");
        }

        return checked((long)truncated);
    }

    /// <summary>The rate a stored count stands for.</summary>
    public static decimal FromStorage(long stored) => stored / Factor;
}
