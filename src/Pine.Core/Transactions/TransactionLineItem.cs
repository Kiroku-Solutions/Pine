using System;
using Pine.Core.Common;
using Pine.Core.Common.ValueObjects;

namespace Pine.Core.Transactions;

public sealed class TransactionLineItem : Entity
{
    public Guid TransactionId { get; private set; }
    public string Description { get; private set; }
    public decimal Quantity { get; private set; }
    public Money UnitPrice { get; private set; }
    public Money TotalPrice { get; private set; }

    /// <summary>
    /// What this line represents on the source document, or <see langword="null"/> for a line
    /// written before <see cref="LineItemKind"/> existed.
    /// </summary>
    /// <remarks>
    /// NULL IS A LEGACY MARKER, NOT A KIND. The only constructor that takes a kind takes a
    /// non-nullable one, so nothing in the domain can produce a null; EF Core materialises legacy
    /// rows through the private parameterless constructor and writes this backing field directly,
    /// which is the same read-path seam <c>Money</c> and <c>CurrencyCode</c> rely on for legacy
    /// vaults. <see cref="Transaction.ReplaceLineItems"/> documents why the reconciliation invariant
    /// steps aside for a set that contains one.
    /// </remarks>
    public LineItemKind? Kind { get; private set; }

#pragma warning disable CS8618 // Required by Entity Framework Core
    private TransactionLineItem() { }
#pragma warning restore CS8618

    internal TransactionLineItem(Guid transactionId, string description, decimal quantity, Money unitPrice, Money totalPrice, LineItemKind? kind)
        : base(Guid.NewGuid())
    {
        TransactionId = transactionId;
        Kind = kind;
        Description = description ?? string.Empty;
        Quantity = quantity;
        UnitPrice = CopyOf(unitPrice);
        TotalPrice = CopyOf(totalPrice);
    }

    /// <summary>
    /// Deep copy of the incoming value object. Line items are persisted as owned entities, so two
    /// children sharing one <see cref="Money"/> (or its nested <see cref="CurrencyCode"/>) instance
    /// would be tracked as a single owner and corrupt the write. Note that a single line item's own
    /// unit price and total price are equally at risk when the caller passes the same instance twice.
    /// </summary>
    private static Money CopyOf(Money price) =>
        new Money(price.Amount, CurrencyCode.FromStoredValue(price.Currency.Value));
}
