namespace Pine.Core.Transactions;

/// <summary>
/// What a <see cref="TransactionLineItem"/> represents on the document it was taken from.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS EXISTS (TD-19). A transaction's line items must sum to its total, exactly. A real
/// receipt's product lines almost never do: tax, tip, discounts, container deposits and cash
/// rounding all sit between "what was bought" and "what was paid". Without a way to say which is
/// which, the only ways to close TD-19 would have been to tolerate a difference — which is not an
/// invariant — or to reject most real receipts.
/// </para>
/// <para>
/// So the difference is REPRESENTED rather than tolerated: every component of the total is a line,
/// and this enum says what kind of line it is. The invariant stays exact and the receipt stays
/// storable, and the user can see WHY the products do not add up to what they paid.
/// </para>
/// <para>
/// A <see cref="TransactionLineItem"/>'s kind is NULLABLE, and null is not a member of this enum on
/// purpose. Null means "written by a build that predates this model" — see
/// <see cref="Transaction.ReplaceLineItems"/> for what that buys and what it costs. Every path that
/// constructs a line item takes a non-nullable <see cref="LineItemKind"/>, so the domain cannot
/// produce a null; only EF Core materialising a legacy row can.
/// </para>
/// <para>
/// SIGN IS NOT PART OF THE KIND. A <see cref="Discount"/> is simply a line whose amount runs
/// opposite to the total's direction, and the sum invariant is what makes that work out. Encoding
/// "subtract this one" in the kind would give two places that decide a line's contribution and let
/// them disagree.
/// </para>
/// </remarks>
public enum LineItemKind
{
    /// <summary>A thing that was bought: the ordinary case, and the default.</summary>
    Product = 0,

    /// <summary>Tax charged on top of the products — VAT, IVA, sales tax.</summary>
    Tax = 1,

    /// <summary>A gratuity or service charge.</summary>
    Tip = 2,

    /// <summary>A reduction applied to the bill as a whole: a coupon, a loyalty discount.</summary>
    Discount = 3,

    /// <summary>
    /// A refundable charge that is not a product — a bottle or crate deposit. Separated from
    /// <see cref="Product"/> because it comes back to the payer and is not consumption.
    /// </summary>
    Deposit = 4,

    /// <summary>
    /// Cash rounding applied by the merchant, where the smallest coin in circulation is larger than
    /// the currency's minor unit. This is why there is no tolerance constant anywhere in this
    /// design: merchant rounding is a real, named, auditable amount, not an epsilon.
    /// </summary>
    Rounding = 5,

    /// <summary>
    /// A difference the user could see but could not explain. It is the escape hatch that keeps the
    /// invariant exact without forcing anybody to lie about what a charge was: the amount is
    /// recorded and labelled as unattributed, rather than being silently absorbed by a tolerance or
    /// silently dropped.
    /// </summary>
    Unattributed = 6,
}
