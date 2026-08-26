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
    public TransactionType Type { get; private set; }
    public Guid? LinkedTransactionId { get; private set; }
    public string Notes { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private readonly List<TransactionSplit> _splits = new();
    public IReadOnlyCollection<TransactionSplit> Splits => _splits.AsReadOnly();

    private readonly List<TransactionLineItem> _lineItems = new();
    public IReadOnlyCollection<TransactionLineItem> LineItems => _lineItems.AsReadOnly();

#pragma warning disable CS8618 // Required by Entity Framework Core
    private Transaction() { }
#pragma warning restore CS8618

    private Transaction(Guid id, Guid accountId, DateTimeOffset date, string payee, Money totalAmount, TransactionType type, Guid? linkedTransactionId, string notes, TimeProvider timeProvider)
        : base(id)
    {
        AccountId = accountId;
        
        // Security & Domain validation: Date cannot be more than 1 day in the future (allowing for timezone mismatches)
        var now = timeProvider.GetUtcNow();
        if (date > now.AddDays(1))
            throw new ArgumentException("Transaction date cannot be in the future.", nameof(date));
        
        Date = date;
        Payee = payee;
        
        // Validate Amount vs Type
        if (type == TransactionType.Expense && totalAmount.Amount > 0)
            throw new ArgumentException("Expense transactions must have a negative or zero amount.", nameof(totalAmount));
        if (type == TransactionType.Income && totalAmount.Amount < 0)
            throw new ArgumentException("Income transactions must have a positive or zero amount.", nameof(totalAmount));

        TotalAmount = totalAmount;
        Type = type;
        LinkedTransactionId = linkedTransactionId;
        Notes = notes;
        CreatedAt = now;

        AddDomainEvent(new TransactionRegisteredEvent(Id, AccountId, TotalAmount, CreatedAt));
    }

    public static Transaction Create(Guid accountId, DateTimeOffset date, string payee, Money totalAmount, TransactionType type, TimeProvider timeProvider, string notes = "", Guid? linkedTransactionId = null)
    {
        if (accountId == Guid.Empty)
            throw new ArgumentException("Account ID is required", nameof(accountId));

        return new Transaction(Guid.NewGuid(), accountId, date, payee, totalAmount, type, linkedTransactionId, notes, timeProvider);
    }

    public void AddSplit(Money amount, string category, string notes = "")
    {
        if (amount.Currency != TotalAmount.Currency)
            throw new ArgumentException("Split currency must match transaction currency.", nameof(amount));

        _splits.Add(new TransactionSplit(Id, amount, category, notes));
        ValidateSplits();
    }

    public void AddLineItem(string description, decimal quantity, Money unitPrice, Money totalPrice)
    {
        if (unitPrice.Currency != TotalAmount.Currency || totalPrice.Currency != TotalAmount.Currency)
            throw new ArgumentException("Line item currency must match transaction currency.");

        _lineItems.Add(new TransactionLineItem(Id, description, quantity, unitPrice, totalPrice));
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
