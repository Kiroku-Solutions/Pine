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

#pragma warning disable CS8618 // Required by Entity Framework Core
    private TransactionLineItem() { }
#pragma warning restore CS8618

    internal TransactionLineItem(Guid transactionId, string description, decimal quantity, Money unitPrice, Money totalPrice)
        : base(Guid.NewGuid())
    {
        TransactionId = transactionId;
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
        new Money(price.Amount, new CurrencyCode(price.Currency.Value));
}
