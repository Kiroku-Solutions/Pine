using System;

namespace Pine.Core.Transactions;

/// <summary>
/// The scale policy for a line item's quantity (TD-95): at most four decimal places, strictly positive.
/// </summary>
/// <remarks>
/// <para>
/// WHY FOUR. A quantity is not money, but it shares the store's representation: it is persisted as a
/// 64-bit count of ten-thousandths (<c>ScaledMoneyConverter</c>), so it sorts and compares correctly in
/// SQL and has 15 integer digits of range. 1.545 kg and 12.301 gal fit with room to spare; a fifth
/// decimal does not exist in the store.
/// </para>
/// <para>
/// THE POLICY FORKS ON WHERE THE VALUE CAME FROM, ON PURPOSE.
/// A value a PERSON typed is REJECTED when it has more than four decimals
/// (<see cref="HasExcessScale"/>; the validator reports <c>Validation.LineItem.QuantityScale</c>): rounding
/// what someone deliberately entered would change their data without telling them.
/// A value read by OCR is ROUNDED half to even, banker's rounding (<see cref="RoundRecognised"/>), the same rule the quantity migration applies to stored legacy values, so one rounding rule governs every value fitted to four places. A recognised
/// "0.33333" is the engine's guess at a printed figure, not an entry, and refusing the whole receipt for
/// noise past the fourth digit helps nobody. Rounding to zero (0.00004) is NOT hidden: the result is then
/// rejected by the non-positive rule and the user sees the line.
/// </para>
/// <para>
/// ZERO AND NEGATIVE ARE REJECTED everywhere: a line with no quantity prices nothing, and a refund or
/// discount is expressed by the sign of its amounts and its <see cref="LineItemKind"/>, not by a negative
/// count. The domain enforces this on write; rows already in a vault are read through EF's private
/// constructor and are not re-validated.
/// </para>
/// </remarks>
public static class LineQuantity
{
    /// <summary>The number of decimal places the store keeps for a quantity.</summary>
    public const int Scale = 4;

    /// <summary>Whether the quantity carries a significant digit beyond <see cref="Scale"/> decimals.</summary>
    public static bool HasExcessScale(decimal quantity) =>
        decimal.Round(quantity, Scale, MidpointRounding.ToZero) != quantity;

    /// <summary>Whether the quantity is acceptable: strictly positive and within <see cref="Scale"/> decimals.</summary>
    public static bool IsValid(decimal quantity) => quantity > 0m && !HasExcessScale(quantity);

    /// <summary>Rounds a recognised or legacy quantity to <see cref="Scale"/> decimals, half to even.</summary>
    public static decimal RoundRecognised(decimal quantity) =>
        decimal.Round(quantity, Scale, MidpointRounding.ToEven);

    /// <summary>Throws <see cref="ArgumentException"/> unless <see cref="IsValid"/>.</summary>
    public static void EnsureValid(decimal quantity)
    {
        if (quantity <= 0m)
            throw new ArgumentException("A line item quantity must be greater than zero.", nameof(quantity));
        if (HasExcessScale(quantity))
            throw new ArgumentException($"A line item quantity cannot have more than {Scale} decimal places.", nameof(quantity));
    }
}
