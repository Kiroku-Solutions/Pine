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
    /// Re-describes this split in place, keeping its <see cref="Entity.Id"/>.
    /// </summary>
    /// <remarks>
    /// THE FIRST MUTATOR EITHER OWNED CHILD HAS EVER HAD, and it exists to close TD-60. Until now a
    /// changed split was a deleted row and an inserted one carrying a brand-new <see cref="Guid"/>,
    /// so a split's identity did not survive its own edit; anything that ever referred to a split —
    /// an audit trail, a merge deciding whether two rows are the same row — would have been left
    /// pointing at nothing. Editing in place is what makes the id mean something.
    /// <para>
    /// <c>internal</c> on purpose: only <see cref="Transaction.ReplaceSplits"/> may call this, and
    /// it calls it only after the whole staged set has passed the split-sum invariant. A public
    /// mutator would let a caller change one split's amount and leave the aggregate not adding up.
    /// </para>
    /// </remarks>
    internal void Update(Money amount, string category, string notes)
    {
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
