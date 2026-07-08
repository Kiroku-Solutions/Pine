using System;
using System.Collections.Generic;
using System.Linq;
using Pine.Core.Common;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Events;

namespace Pine.Core.Transactions;

/// <summary>
/// Transaction aggregate root.
/// </summary>
public sealed class Transaction : AggregateRoot
{
    public Guid AccountId { get; private set; }
    public DateTimeOffset Date { get; private set; }
    public string Payee { get; private set; }
    public Money TotalAmount { get; private set; }
    public string Notes { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private readonly List<TransactionSplit> _splits = new();
    public IReadOnlyCollection<TransactionSplit> Splits => _splits.AsReadOnly();

#pragma warning disable CS8618 // Required by Entity Framework Core
    private Transaction() { }
#pragma warning restore CS8618

    private Transaction(Guid id, Guid accountId, DateTimeOffset date, string payee, Money totalAmount, string notes, TimeProvider timeProvider)
        : base(id)
    {
        AccountId = accountId;
        Date = date;
        Payee = payee;
        TotalAmount = totalAmount;
        Notes = notes;
        CreatedAt = timeProvider.GetUtcNow();

        AddDomainEvent(new TransactionRegisteredEvent(Id, AccountId, TotalAmount, CreatedAt));
    }

    public static Transaction Create(Guid accountId, DateTimeOffset date, string payee, Money totalAmount, TimeProvider timeProvider, string notes = "")
    {
        if (accountId == Guid.Empty)
            throw new ArgumentException("Account ID is required", nameof(accountId));

        return new Transaction(Guid.NewGuid(), accountId, date, payee, totalAmount, notes, timeProvider);
    }

    public void AddSplit(Money amount, string category, string notes = "")
    {
        if (amount.Currency != TotalAmount.Currency)
            throw new ArgumentException("Split currency must match transaction currency.", nameof(amount));

        _splits.Add(new TransactionSplit(Id, amount, category, notes));
        ValidateSplits();
    }

    public void ValidateSplits()
    {
        if (_splits.Count > 0)
        {
            var sum = _splits.Aggregate(Money.Zero(TotalAmount.Currency), (current, split) => current + split.Amount);
            if (sum != TotalAmount)
            {
                throw new InvalidOperationException("The sum of splits must equal the total transaction amount.");
            }
        }
    }
}
