using System;
using Pine.Core.Common;
using Pine.Core.Common.ValueObjects;

namespace Pine.Core.Transactions;

public sealed class TransactionSplit : Entity
{
    public Guid TransactionId { get; private set; }
    public Money Amount { get; private set; }
    public string Category { get; private set; }
    public string Notes { get; private set; }

#pragma warning disable CS8618 // Required by Entity Framework Core
    private TransactionSplit() { }
#pragma warning restore CS8618

    internal TransactionSplit(Guid transactionId, Money amount, string category, string notes)
        : base(Guid.NewGuid())
    {
        TransactionId = transactionId;
        Amount = amount;
        Category = category;
        Notes = notes;
    }
}
