using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Pine.Core.Common.ValueObjects;

/// <summary>
/// An amount of money, denominated in a currency and held at that currency's scale.
/// </summary>
/// <remarks>
/// <para>
/// THE SCALE POLICY, AND WHY IT LIVES HERE. Before this type had one, "the sum of the splits equals
/// the total" was exact only because nothing in Pine ever rounded. The moment anything divides — a
/// bill split three ways, a percentage, a unit price times a quantity — an unrounded
/// <see cref="decimal"/> carries digits no currency can express, and the invariant becomes exact for
/// a number that is not a quantity of money. Putting the policy in the value object rather than at
/// the boundaries means there is no boundary to forget: an out-of-scale amount cannot exist inside
/// the domain, so no aggregate has to defend against one.
/// </para>
/// <para>
/// WHAT IS NORMALISED VERSUS WHAT IS REJECTED. The two are not the same thing and the distinction is
/// load-bearing.
/// </para>
/// <list type="bullet">
/// <item><description>
/// For a currency WITH a minor unit (USD, KWD — <see cref="CurrencyCode.MinorUnits"/> is greater
/// than zero), digits below that unit are sub-unit noise: representational residue from division,
/// never a sum anybody meant. They are ROUNDED AWAY, half-to-even.
/// <c>new Money(10.005m, USD).Amount</c> is <c>10.00m</c>.
/// </description></item>
/// <item><description>
/// For a currency with NO minor unit (JPY, KRW — <c>MinorUnits == 0</c>) there is no sub-unit for
/// noise to live in. A fractional yen is not residue; it is a quantity the currency cannot express,
/// which means the caller computed it somewhere it should not have. <c>new Money(10.5m, JPY)</c>
/// THROWS. Silently turning it into 10 JPY would hide the arithmetic that produced the half.
/// </description></item>
/// </list>
/// <para>
/// That asymmetry is the frontier this type draws, and it is drawn at the CONSTRUCTOR — the
/// caller-authored boundary. Results computed by this type itself (<see cref="Multiply"/>,
/// <see cref="Allocate(int)"/>) are not caller-authored, so they round through
/// <see cref="Round(decimal, CurrencyCode)"/> first and never throw. A caller that genuinely wants
/// "the nearest expressible yen" says so out loud by calling <see cref="Round"/> or
/// <see cref="FromRounded"/>.
/// </para>
/// <para>
/// HALF-TO-EVEN, NOT HALF-AWAY-FROM-ZERO. <see cref="MidpointRounding.ToEven"/> is the default of
/// <see cref="decimal"/> and of IEEE 754 because it is unbiased over many roundings: away-from-zero
/// rounds every midpoint in the same direction and accumulates a drift proportional to the number of
/// operations, which in a ledger is exactly the wrong error to have.
/// </para>
/// <para>
/// THE READ PATH DOES NOT COME THROUGH HERE. EF Core materialises via the private parameterless
/// constructor and writes the backing fields directly, so a vault written by an older Pine build can
/// hold an out-of-scale amount or an unknown currency and still open. That is deliberate — see
/// <see cref="CurrencyCode.FromStoredValue"/> — and it is pinned by an integration test that seeds
/// such rows over a raw SQLCipher connection.
/// </para>
/// <para>
/// STILL A <see cref="decimal"/> IN THE DOMAIN, AN INTEGER IN THE STORE. Since TD-31 the persistence
/// layer keeps every amount as a count of ten-thousandths in a SQLite INTEGER column, so comparisons
/// and ordering are correct if they ever reach SQL. That is a mapping decision and it is invisible
/// here: this type still holds a <see cref="decimal"/>, and the store's fixed scale of 4 is greater
/// than or equal to every currency's, so the round trip is exact. The one place it shows through is
/// <see cref="ToString"/> — see the remarks there.
/// </para>
/// </remarks>
public sealed class Money : ValueObject
{
    public decimal Amount { get; }
    public CurrencyCode Currency { get; }

    /// <summary>
    /// The number of decimal digits this amount is held at — its currency's minor units. 2 for USD,
    /// 0 for JPY, 3 for KWD.
    /// </summary>
    public int Scale => Currency.MinorUnits;

    /// <summary>
    /// Constructs an amount, normalised to <paramref name="currency"/>'s scale.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="currency"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="currency"/> has no minor unit and <paramref name="amount"/> has a fractional
    /// part. See the type remarks: that is a caller error, not sub-unit noise.
    /// </exception>
    public Money(decimal amount, CurrencyCode currency)
    {
        Currency = currency ?? throw new ArgumentNullException(nameof(currency));

        var scale = currency.MinorUnits;

        if (scale == 0 && amount != decimal.Truncate(amount))
        {
            throw new ArgumentException(
                $"{amount} is not expressible in {currency.Value}, which has no minor unit. " +
                "Round it deliberately with Money.Round or Money.FromRounded if that is what you mean.",
                nameof(amount));
        }

        Amount = Math.Round(amount, scale, MidpointRounding.ToEven);
    }

    /// <summary>
    /// Rounds <paramref name="amount"/> to <paramref name="currency"/>'s scale, half-to-even. Never
    /// throws for an out-of-scale value — this is the explicit way to say "round this".
    /// </summary>
    /// <remarks>
    /// Use at a boundary that genuinely produces an unrounded figure: a percentage, an FX
    /// conversion, a parsed external file. Inside the domain it is redundant, because no
    /// <see cref="Money"/> can be out of scale in the first place.
    /// </remarks>
    public static decimal Round(decimal amount, CurrencyCode currency)
    {
        ArgumentNullException.ThrowIfNull(currency);
        return Math.Round(amount, currency.MinorUnits, MidpointRounding.ToEven);
    }

    /// <summary>Constructs an amount by rounding rather than rejecting. Never throws on scale.</summary>
    public static Money FromRounded(decimal amount, CurrencyCode currency) =>
        new Money(Round(amount, currency), currency);

    public static Money Zero(CurrencyCode currency) => new Money(0, currency);

    public Money Add(Money other)
    {
        ArgumentNullException.ThrowIfNull(other);

        if (Currency != other.Currency)
            throw new InvalidOperationException($"Cannot add money of different currencies: {Currency} and {other.Currency}");

        return new Money(Amount + other.Amount, Currency);
    }

    public Money Subtract(Money other)
    {
        ArgumentNullException.ThrowIfNull(other);

        if (Currency != other.Currency)
            throw new InvalidOperationException($"Cannot subtract money of different currencies: {Currency} and {other.Currency}");

        return new Money(Amount - other.Amount, Currency);
    }

    /// <summary>
    /// Scales this amount by <paramref name="factor"/>, normalising the result to the currency's
    /// scale, half-to-even.
    /// </summary>
    /// <remarks>
    /// The result is rounded, not rejected, even for a zero-minor-unit currency: the product is
    /// computed by this type, so a fractional intermediate is arithmetic, not a caller error. Note
    /// that multiplication does NOT distribute over a set of parts without loss — to split an amount
    /// use <see cref="Allocate(int[])"/>, which is exact by construction.
    /// </remarks>
    public Money Multiply(decimal factor) => FromRounded(Amount * factor, Currency);

    /// <summary>Same amount, opposite sign.</summary>
    public Money Negate() => new Money(-Amount, Currency);

    /// <summary>
    /// Splits this amount into <paramref name="parts"/> equal shares whose sum is EXACTLY this
    /// amount.
    /// </summary>
    public Money[] Allocate(int parts)
    {
        if (parts <= 0)
            throw new ArgumentOutOfRangeException(nameof(parts), parts, "An amount must be allocated across at least one part.");

        var ratios = new int[parts];
        Array.Fill(ratios, 1);
        return Allocate(ratios);
    }

    /// <summary>
    /// Splits this amount in proportion to <paramref name="ratios"/> so that the parts sum to
    /// EXACTLY this amount — no minor unit lost, none invented.
    /// </summary>
    /// <remarks>
    /// <para>
    /// LARGEST REMAINDER. Each part gets the truncated integer number of minor units its ratio
    /// earns; the units left over — always fewer than the number of parts — go one each to the parts
    /// with the largest truncated remainders, ties broken by position, so the earliest part is
    /// favoured. <c>new Money(10.00m, USD).Allocate(3)</c> is <c>[3.34, 3.33, 3.33]</c>.
    /// </para>
    /// <para>
    /// WHY NOT DIVIDE AND ROUND. Rounding each share independently is the defect this method exists
    /// to prevent: three shares of 3.3333... each round to 3.33 and sum to 9.99, and a cent of the
    /// user's money has vanished into nothing. Allocation is a partition, not a division.
    /// </para>
    /// <para>
    /// SIGN. A negative amount is allocated by magnitude and re-signed, so <c>Allocate</c> of
    /// <c>-10.00</c> mirrors <c>Allocate</c> of <c>10.00</c> exactly. A ratio of zero yields a zero
    /// part and can never receive a remainder unit, because its remainder is zero and therefore
    /// ranks last.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// <paramref name="ratios"/> is empty, contains a negative, or sums to zero.
    /// </exception>
    public Money[] Allocate(params int[] ratios)
    {
        ArgumentNullException.ThrowIfNull(ratios);

        if (ratios.Length == 0)
            throw new ArgumentException("An amount must be allocated across at least one part.", nameof(ratios));

        long ratioSum = 0;
        foreach (var ratio in ratios)
        {
            if (ratio < 0)
                throw new ArgumentException("Allocation ratios cannot be negative.", nameof(ratios));

            ratioSum += ratio;
        }

        if (ratioSum == 0)
            throw new ArgumentException("Allocation ratios must not all be zero.", nameof(ratios));

        var totalMinor = ToMinorUnits();
        var sign = totalMinor < 0 ? -1 : 1;
        var magnitude = totalMinor < 0 ? -(Int128)totalMinor : (Int128)totalMinor;

        var shares = new Int128[ratios.Length];
        var remainders = new Int128[ratios.Length];
        Int128 assigned = 0;

        for (var i = 0; i < ratios.Length; i++)
        {
            var numerator = magnitude * ratios[i];
            shares[i] = numerator / ratioSum;
            remainders[i] = numerator % ratioSum;
            assigned += shares[i];
        }

        var leftover = (int)(magnitude - assigned);

        // Rank by remainder descending, position ascending. Array.Sort is not stable, so position is
        // part of the comparison key rather than left to the sort to preserve.
        var order = Enumerable.Range(0, ratios.Length).ToArray();
        Array.Sort(order, (left, right) =>
        {
            var byRemainder = remainders[right].CompareTo(remainders[left]);
            return byRemainder != 0 ? byRemainder : left.CompareTo(right);
        });

        for (var i = 0; i < leftover; i++)
        {
            shares[order[i]] += 1;
        }

        var unit = MinorUnit(Scale);
        var result = new Money[ratios.Length];
        for (var i = 0; i < ratios.Length; i++)
        {
            result[i] = new Money(sign * (decimal)shares[i] * unit, Currency);
        }

        return result;
    }

    /// <summary>
    /// This amount expressed as a whole number of minor units — cents for USD, yen for JPY.
    /// </summary>
    /// <remarks>
    /// Exact by construction: the constructor already normalised to the currency's scale, so the
    /// scaled value has no fractional part. This is a projection for arithmetic, NOT a change of
    /// representation; storing money as scaled integers is TD-31 and is not what this is.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The amount is not a whole number of minor units, which can only happen if the constructor's
    /// normalisation was bypassed — for example on a value EF materialised out of a legacy vault.
    /// </exception>
    public long ToMinorUnits()
    {
        var scaled = Amount * PowerOfTen(Scale);
        var truncated = decimal.Truncate(scaled);

        if (scaled != truncated)
        {
            throw new InvalidOperationException(
                $"{Amount} {Currency} is not a whole number of minor units; Money's scale normalisation has been bypassed.");
        }

        return (long)truncated;
    }

    private static decimal PowerOfTen(int scale) => scale switch
    {
        0 => 1m,
        1 => 10m,
        2 => 100m,
        3 => 1000m,
        4 => 10000m,
        _ => throw new InvalidOperationException($"Unsupported currency scale {scale}."),
    };

    private static decimal MinorUnit(int scale) => scale switch
    {
        0 => 1m,
        1 => 0.1m,
        2 => 0.01m,
        3 => 0.001m,
        4 => 0.0001m,
        _ => throw new InvalidOperationException($"Unsupported currency scale {scale}."),
    };

    public static Money operator +(Money a, Money b) => a.Add(b);
    public static Money operator -(Money a, Money b) => a.Subtract(b);

    /// <summary>Unary negation. Replaces the hand-rolled <c>new Money(-x.Amount, x.Currency)</c>.</summary>
    public static Money operator -(Money a) => a.Negate();

    public static Money operator *(Money a, decimal factor) => a.Multiply(factor);

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Amount;
        yield return Currency;
    }

    /// <summary>
    /// Renders the amount at its currency's scale, followed by the currency code.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The number of decimals shown comes from the currency, not from the scale the CLR
    /// <see cref="decimal"/> happens to be carrying. Interpolating <see cref="Amount"/> directly
    /// made the display depend on how the value was produced: an amount typed as <c>12</c> rendered
    /// as <c>12 USD</c> while the same amount arriving from a rounding operation rendered as
    /// <c>12.00 USD</c>. Several views bind a whole <see cref="Money"/> and show this string, so
    /// that inconsistency was visible in the transaction list.
    /// </para>
    /// <para>
    /// It became load-bearing with TD-31: storing amounts as integers of ten-thousandths means the
    /// read path reconstructs them by decimal division, which normalises trailing zeros away. Fixing
    /// the presentation here is what keeps a storage-format change from altering what a user reads
    /// on screen.
    /// </para>
    /// <para>
    /// A value carrying MORE decimals than its currency allows is shown in full rather than rounded
    /// down to the currency's scale. Such a value can only exist in a vault written outside the
    /// domain, and rounding it for display would hide exactly the discrepancy worth seeing.
    /// </para>
    /// </remarks>
    public override string ToString() => $"{ToAmountString()} {Currency}";

    /// <summary>
    /// Renders the amount alone, at its currency's scale and in the current culture — no currency
    /// code, no symbol.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This exists because the views need both shapes. Some show a whole <see cref="Money"/> and want
    /// the code with it; others print the code in a separate control and want only the figure. Before
    /// this, the second kind hardcoded <c>{0:N2}</c> in the markup (TD-75), which is two decimals for
    /// a currency the domain knows has none — <c>1500 JPY</c> rendered as <c>1,500.00</c>, and a
    /// three-decimal KWD amount was rendered with its last digit silently dropped. The rule now lives
    /// once, here, next to the scale it depends on.
    /// </para>
    /// <para>
    /// <b>Group separators are included and the current culture decides them</b>, which is what the
    /// <c>{0:N2}</c> bindings this replaces already did for the amounts they got right. A ledger
    /// figure without them is harder to read at exactly the magnitudes where being able to read it
    /// matters.
    /// </para>
    /// <para>
    /// A value carrying MORE decimals than its currency allows is shown in full rather than rounded
    /// down to the currency's scale. Such a value can only exist in a vault written outside the
    /// domain, and rounding it for display would hide exactly the discrepancy worth seeing.
    /// </para>
    /// </remarks>
    public string ToAmountString()
    {
        var digits = Math.Max(Scale, Amount.Scale);

        // The format specifier is built invariantly — it is a token, not a number to render — while
        // the amount itself is rendered in the current culture, which is what the user's decimal and
        // group separators require.
        var format = string.Create(CultureInfo.InvariantCulture, $"N{digits}");
        return Amount.ToString(format, CultureInfo.CurrentCulture);
    }

    // EF Core constructor
    private Money() { Currency = null!; }
}
