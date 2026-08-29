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
        Amount = CopyOf(amount);
        Category = category;
        Notes = notes;
    }

    /// <summary>
    /// Deep copy of the incoming value object. Splits are persisted as owned entities, so two
    /// splits sharing one <see cref="Money"/> (or its nested <see cref="CurrencyCode"/>) instance
    /// would be tracked as a single owner and corrupt the write. Copying keeps every child distinct.
    /// </summary>
    private static Money CopyOf(Money amount) =>
        new Money(amount.Amount, CurrencyCode.FromStoredValue(amount.Currency.Value));
}
