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
        UnitPrice = unitPrice;
        TotalPrice = totalPrice;
    }
}
