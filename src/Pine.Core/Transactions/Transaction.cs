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

    /// <summary>
    /// The stable key identifying the external row this transaction was imported from, or
    /// <see langword="null"/> for a transaction that was not imported.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is what makes import de-duplication correct. Matching on <c>(Date.Date, TotalAmount.Amount)</c>
    /// treats two genuinely distinct transactions with the same date and amount as one and drops the
    /// second, which is silent data loss. Keys are produced by <c>Pine.Core.Importing.ImportKeyFactory</c>.
    /// </para>
    /// <para>
    /// WHY IT LIVES HERE AND NOT IN A SEPARATE TABLE: a dedicated <c>ImportedRow</c> table would need
    /// its own repository, its own write path, and a foreign key the schema does not currently have
    /// (TD-30 records the missing foreign keys), and would buy no guarantee this does not already
    /// give — the transaction itself is the thing that either is or is not a duplicate.
    /// </para>
    /// <para>
    /// WHY IT IS A STRING: SQLite stores <see cref="decimal"/> as TEXT (TD-31), so any comparison that
    /// reaches SQL is lexicographic. A string key can safely be pushed down into a SQL predicate and
    /// compares identically in memory and in the database; a numeric one could not.
    /// </para>
    /// <para>
    /// NO BACKFILL, DELIBERATELY: transactions imported before this property existed keep
    /// <see langword="null"/> and therefore never match an incoming row. Backfilling them would mean
    /// inventing a source name and a row index that never existed, producing keys that look
    /// authoritative but are fabricated — and a fabricated key that happens to collide would drop a
    /// real transaction, which is the exact defect being fixed. Those rows are re-importable, and a
    /// visible duplicate is recoverable in a way a silently dropped transaction is not.
    /// </para>
    /// </remarks>
    public string? ImportKey { get; private set; }

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

    /// <summary>
    /// Records which external row this transaction was imported from.
    /// </summary>
    /// <remarks>
    /// Assignment is a domain operation rather than a settable property so the invariants below hold
    /// for every key that ever reaches the database. The length limit matches the persisted column
    /// width exactly: a longer key would otherwise be truncated by the store and silently start
    /// matching a different row.
    /// </remarks>
    /// <param name="key">The import key, as produced by <c>Pine.Core.Importing.ImportKeyFactory</c>.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="key"/> is null, empty, whitespace, or longer than 128 characters once trimmed.
    /// </exception>
    public void AssignImportKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Import key cannot be empty.", nameof(key));

        var trimmed = key.Trim();
        if (trimmed.Length > MaxImportKeyLength)
            throw new ArgumentException($"Import key cannot exceed {MaxImportKeyLength} characters.", nameof(key));

        ImportKey = trimmed;
    }

    /// <summary>
    /// The maximum length of <see cref="ImportKey"/>, matching the persisted column width.
    /// </summary>
    public const int MaxImportKeyLength = 128;

    /// <summary>
    /// Appends one split. Only the per-split currency is checked here; the whole-set sum
    /// invariant is enforced at the aggregate boundary via <see cref="EnsureValid"/>,
    /// so a multi-split set can be built up incrementally.
    /// </summary>
    public void AddSplit(Money amount, string category, string notes = "")
    {
        if (amount.Currency != TotalAmount.Currency)
            throw new ArgumentException("Split currency must match transaction currency.", nameof(amount));

        _splits.Add(new TransactionSplit(Id, amount, category, notes));
    }

    /// <summary>
    /// Atomically replaces the entire split set. The candidate set is staged and fully
    /// validated first, so a rejected set leaves the aggregate completely unmutated.
    /// An empty (or null) sequence clears the splits, which is a valid state.
    /// </summary>
    public void ReplaceSplits(IEnumerable<SplitDefinition> splits)
    {
        var staged = new List<TransactionSplit>();

        if (splits is not null)
        {
            foreach (var definition in splits)
            {
                if (definition.Amount is null)
                    throw new ArgumentException("Split amount is required.", nameof(splits));

                if (definition.Amount.Currency != TotalAmount.Currency)
                    throw new ArgumentException("Split currency must match transaction currency.", nameof(splits));

                staged.Add(new TransactionSplit(Id, definition.Amount, definition.Category, definition.Notes));
            }
        }

        ValidateSplitSum(staged);

        _splits.Clear();
        _splits.AddRange(staged);
    }

    public void AddLineItem(string description, decimal quantity, Money unitPrice, Money totalPrice)
    {
        if (unitPrice.Currency != TotalAmount.Currency || totalPrice.Currency != TotalAmount.Currency)
            throw new ArgumentException("Line item currency must match transaction currency.");

        _lineItems.Add(new TransactionLineItem(Id, description, quantity, unitPrice, totalPrice));
    }

    /// <summary>
    /// Atomically replaces the entire line item set. The candidate set is staged and fully
    /// validated first, so a rejected set leaves the aggregate completely unmutated.
    /// An empty (or null) sequence clears the line items.
    /// Validation matches <see cref="AddLineItem"/> exactly: currency only. Reconciling line item
    /// totals against <see cref="TotalAmount"/> is deliberately not done here.
    /// </summary>
    public void ReplaceLineItems(IEnumerable<LineItemDefinition> lineItems)
    {
        var staged = new List<TransactionLineItem>();

        if (lineItems is not null)
        {
            foreach (var definition in lineItems)
            {
                if (definition.UnitPrice is null || definition.TotalPrice is null)
                    throw new ArgumentException("Line item unit price and total price are required.", nameof(lineItems));

                if (definition.UnitPrice.Currency != TotalAmount.Currency || definition.TotalPrice.Currency != TotalAmount.Currency)
                    throw new ArgumentException("Line item currency must match transaction currency.", nameof(lineItems));

                staged.Add(new TransactionLineItem(Id, definition.Description, definition.Quantity, definition.UnitPrice, definition.TotalPrice));
            }
        }

        _lineItems.Clear();
        _lineItems.AddRange(staged);
    }

    /// <summary>
    /// Validates the current split set: zero splits is valid, otherwise the splits must sum to <see cref="TotalAmount"/>.
    /// </summary>
    public void ValidateSplits() => ValidateSplitSum(_splits);

    /// <summary>
    /// Aggregate-boundary invariant check. Every write path must call this before persisting;
    /// it is the single entry point for whole-aggregate validation.
    /// </summary>
    public void EnsureValid() => ValidateSplits();

    private void ValidateSplitSum(List<TransactionSplit> splits)
    {
        if (splits.Count > 0)
        {
            var sum = splits.Aggregate(Money.Zero(TotalAmount.Currency), (current, split) => current + split.Amount);
            if (sum != TotalAmount)
            {
                throw new InvalidOperationException("The sum of splits must equal the total transaction amount.");
            }
        }
    }
}
