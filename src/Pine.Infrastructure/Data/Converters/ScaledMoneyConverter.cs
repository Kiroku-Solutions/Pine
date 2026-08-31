using System;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Pine.Infrastructure.Data.Converters;

/// <summary>
/// Persists a monetary <see cref="decimal"/> as a SQLite <c>INTEGER</c> count of ten-thousandths
/// (TD-31, TD-79).
/// </summary>
/// <remarks>
/// <para>
/// Without this, every money column is a bare <see cref="decimal"/> that the EF Core SQLite provider
/// maps to <c>TEXT</c>. Nothing was broken by that while every comparison happened in memory, but the
/// first <c>WHERE Amount &gt; 100</c> or <c>ORDER BY Amount</c> to reach SQL would have compared
/// decimal *strings* lexicographically — <c>'9.00' &gt; '100.00'</c> — and it would have looked
/// correct on small test data. Storing an integer removes the possibility rather than documenting it.
/// </para>
/// <para>
/// <b>The scale is fixed at 4 and is deliberately NOT the currency's own scale.</b> A
/// <see cref="ValueConverter"/> sees one column and nothing else, and Pine maps
/// <c>Money.Amount</c> and <c>Money.Currency</c> as two separate columns of a complex or owned
/// property. The converter therefore cannot know which currency the amount it is converting is
/// denominated in, so a per-currency scale is not merely awkward here — it is unavailable.
/// </para>
/// <para>
/// Fixing the scale at 4 costs nothing, because 4 is the largest number of minor units in ISO 4217
/// (<c>CLF</c> and <c>UYW</c>) and is already the ceiling that <c>Money.PowerOfTen</c> throws above.
/// The store's scale is therefore greater than or equal to the scale of every representable
/// <c>Money</c>, and the round trip is exact for all of them.
/// </para>
/// <para>
/// <b>The write path throws rather than rounding</b>, mirroring <c>Money.ToMinorUnits()</c> exactly.
/// <c>Money</c>'s constructor already rounds to the currency's scale, so a value arriving here with
/// more than four decimals means that normalisation was bypassed — by raw SQL, by a repair script,
/// or by an EF constructor-selection change. Silently rounding it would destroy the evidence that
/// something is writing to this vault without going through the domain.
/// </para>
/// <para>
/// <b>The read path needs no tolerance</b>, which is a simplification rather than a policy change.
/// The deliberate leniency described in the audit's §12.18 existed because a legacy <c>TEXT</c>
/// column could hold any number of decimal places; a 64-bit integer of ten-thousandths cannot, so
/// every value that can be read back is in scale by construction.
/// </para>
/// </remarks>
public sealed class ScaledMoneyConverter : ValueConverter<decimal, long>
{
    /// <summary>The number of decimal places the store keeps. See the remarks on this class.</summary>
    public const int StorageScale = 4;

    private const decimal Factor = 10_000m;

    public ScaledMoneyConverter()
        : base(value => ToStorage(value), stored => FromStorage(stored))
    {
    }

    /// <summary>
    /// Converts an in-scale amount to its count of ten-thousandths.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The amount carries more than <see cref="StorageScale"/> decimal places, which the store
    /// cannot represent. See the remarks on this class for why this throws instead of rounding.
    /// </exception>
    /// <exception cref="OverflowException">
    /// The amount is too large to be expressed as a 64-bit count of ten-thousandths — beyond roughly
    /// ±922 trillion. A ledger figure that big is a defect upstream, not a value to truncate.
    /// </exception>
    public static long ToStorage(decimal value)
    {
        var scaled = value * Factor;
        var truncated = decimal.Truncate(scaled);

        if (scaled != truncated)
        {
            throw new InvalidOperationException(
                $"{value} carries more than {StorageScale} decimal places and cannot be stored exactly; " +
                "Money's scale normalisation has been bypassed.");
        }

        return checked((long)truncated);
    }

    /// <summary>Converts a stored count of ten-thousandths back to a decimal amount.</summary>
    /// <remarks>
    /// Decimal division normalises away trailing zeros, so <c>123400</c> reads back as <c>12.34</c>
    /// and not <c>12.3400</c>. That is why <c>Money.ToString()</c> formats to the currency's scale
    /// rather than to whatever scale the CLR value happens to carry.
    /// </remarks>
    public static decimal FromStorage(long stored) => stored / Factor;
}
