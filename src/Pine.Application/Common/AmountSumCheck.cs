using System;
using System.Collections.Generic;
using System.Linq;
using Pine.Core.Common.ValueObjects;

namespace Pine.Application.Common;

/// <summary>
/// The arithmetic behind the TD-19/TD-68 sum invariant, shared by every validator that has to check
/// it before <c>Transaction.EnsureValid</c> ever runs.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS EXISTS. <c>AddTransactionCommandValidator</c> already checked "do the line items sum to
/// the total" so the failure could be phrased for a human instead of leaking out of
/// <c>Transaction.ValidateLineItemSum</c> as a domain-internal sentence (TD-24). Building
/// <c>UpdateTransactionCommandValidator</c>'s identical check for splits by copying that block would
/// have been a second place this exact arithmetic could drift from itself. This is the one place it
/// lives; both validators call it and only differ in which <see cref="Core.Common.UserFacingMessage"/>
/// key and field name they attach to the result.
/// </para>
/// <para>
/// ROUNDED, NOT REJECTED. Amounts arrive here as raw <see cref="decimal"/> from a command, before any
/// <see cref="Money"/> has been constructed, so <see cref="Money.FromRounded"/> is used rather than
/// <see cref="Money"/>'s throwing constructor -- a validator's job is to produce a clean refusal, not
/// to throw the very domain exception it exists to avoid leaking.
/// </para>
/// </remarks>
public static class AmountSumCheck
{
    /// <summary>Sums <paramref name="amounts"/>, each rounded to <paramref name="currency"/>'s scale.</summary>
    public static Money Sum(IEnumerable<decimal> amounts, CurrencyCode currency)
    {
        ArgumentNullException.ThrowIfNull(amounts);
        ArgumentNullException.ThrowIfNull(currency);

        return amounts.Aggregate(Money.Zero(currency), (running, amount) => running + Money.FromRounded(amount, currency));
    }

    /// <summary>
    /// Whether <paramref name="amounts"/> sum to <paramref name="total"/> once both are rounded to
    /// <paramref name="currency"/>'s scale.
    /// </summary>
    /// <param name="amounts">The candidate parts, in the same sign convention as <paramref name="total"/>.</param>
    /// <param name="total">The figure the parts must add up to.</param>
    /// <param name="currency">The currency both sides are compared at.</param>
    /// <param name="sum">The rounded sum of <paramref name="amounts"/>.</param>
    /// <param name="totalMoney">The rounded <paramref name="total"/>.</param>
    /// <returns><see langword="true"/> when the two are equal.</returns>
    public static bool Matches(
        IEnumerable<decimal> amounts,
        decimal total,
        CurrencyCode currency,
        out Money sum,
        out Money totalMoney)
    {
        sum = Sum(amounts, currency);
        totalMoney = Money.FromRounded(total, currency);
        return sum == totalMoney;
    }
}
