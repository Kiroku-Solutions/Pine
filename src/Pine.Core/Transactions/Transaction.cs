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

    /// <summary>
    /// When this transaction was voided, or <see langword="null"/> while it is still in force.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY A VOID FLAG AND NOT A DELETE (TD-02). A ledger that can silently lose a row cannot be
    /// audited. Deleting the transaction would remove the only evidence that the money movement was
    /// ever recorded, and with it the ability to answer "why did this balance change in March?".
    /// Voiding keeps the row, keeps its <see cref="ImportKey"/>, keeps its splits, and records
    /// WHEN and WHY it stopped counting.
    /// </para>
    /// <para>
    /// WHY THERE IS NO SEPARATE REVERSAL ROW. A contra-entry would be the other classic shape, and it
    /// is the right one for a system that has already published a period. Pine has no period close, no
    /// posting concept and no statement it has committed to; a contra-entry here would double every
    /// corrected row in the list and in every sum, buying no property this flag does not already give.
    /// The balance effect is undone by the void handler in the same database transaction as the flag,
    /// so the two can never disagree.
    /// </para>
    /// <para>
    /// WHY THE IMPORT KEY SURVIVES A VOID. The key answers "was this external row already imported?",
    /// and it was. Clearing it on void would make the next import of the same statement re-create the
    /// row the user just voided, silently undoing their correction -- the exact class of defect TD-03
    /// closed.
    /// </para>
    /// </remarks>
    public DateTimeOffset? VoidedAt { get; private set; }

    /// <summary>
    /// Why this transaction was voided, or <see langword="null"/> while it is still in force.
    /// </summary>
    public string? VoidReason { get; private set; }

    /// <summary>
    /// Whether this transaction has been voided and therefore no longer contributes to its account's
    /// balance. Not mapped: it is derived from <see cref="VoidedAt"/>, which is the stored truth.
    /// </summary>
    public bool IsVoided => VoidedAt.HasValue;

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

    /// <summary>
    /// Appends one line item. Only the per-line currency is checked here; the whole-set
    /// reconciliation invariant (TD-19) is enforced at the aggregate boundary via
    /// <see cref="EnsureValid"/>, so a multi-line set can be built up incrementally.
    /// </summary>
    /// <remarks>
    /// The eager/deferred split is the TD-04 lesson applied to the second owned collection: a set
    /// that does not yet reconcile is merely INCOMPLETE, and rejecting the first line of a receipt
    /// because it is not yet the whole receipt makes the API unusable. The check therefore lives at
    /// the one choke point every write passes through.
    /// </remarks>
    public void AddLineItem(string description, decimal quantity, Money unitPrice, Money totalPrice, LineItemKind kind = LineItemKind.Product)
    {
        if (unitPrice.Currency != TotalAmount.Currency || totalPrice.Currency != TotalAmount.Currency)
            throw new ArgumentException("Line item currency must match transaction currency.");

        _lineItems.Add(new TransactionLineItem(Id, description, quantity, unitPrice, totalPrice, kind));
    }

    /// <summary>
    /// Atomically replaces the entire line item set. The candidate set is staged and fully
    /// validated first — currency per line, then the whole-set reconciliation invariant — so a
    /// rejected set leaves the aggregate completely unmutated. An empty (or null) sequence clears
    /// the line items, which is a valid state.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the whole-set writer, so validating here is not eager in the TD-04 sense: the caller
    /// has by definition finished describing the set. It mirrors <see cref="ReplaceSplits"/>
    /// exactly.
    /// </para>
    /// <para>
    /// A staged set containing a line whose <see cref="LineItemDefinition.Kind"/> is null is NOT
    /// reconciled — see <see cref="ValidateLineItemSum"/> for why that hole exists and how narrow
    /// it is.
    /// </para>
    /// </remarks>
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

                staged.Add(new TransactionLineItem(Id, definition.Description, definition.Quantity, definition.UnitPrice, definition.TotalPrice, definition.Kind));
            }
        }

        ValidateLineItemSum(staged);

        _lineItems.Clear();
        _lineItems.AddRange(staged);
    }

    /// <summary>
    /// The maximum length of <see cref="VoidReason"/>, matching the persisted column width.
    /// </summary>
    public const int MaxVoidReasonLength = 256;

    /// <summary>
    /// Marks this transaction as void, recording when and why.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method changes the transaction ONLY. Undoing the amount's effect on the account balance is
    /// the caller's job, and it is deliberately not done here: <see cref="Transaction"/> and
    /// <c>Account</c> are separate aggregate roots, and an aggregate that reached across that boundary
    /// would be doing the unit of work's job with none of its guarantees. The pairing is enforced one
    /// layer up, inside the single database transaction the command pipeline opens.
    /// </para>
    /// <para>
    /// Voiding is not idempotent on purpose. A second call means the caller believed the transaction
    /// was still in force, and silently accepting it would let a double-void adjust the balance twice
    /// -- the same class of defect as a double-posted transaction. Failing loudly makes the caller
    /// check first, which is what the command validator does.
    /// </para>
    /// </remarks>
    /// <param name="reason">Why the transaction is being voided. Required, and trimmed.</param>
    /// <param name="timeProvider">Clock used to stamp <see cref="VoidedAt"/>.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="reason"/> is null, empty, whitespace, or longer than
    /// <see cref="MaxVoidReasonLength"/> characters once trimmed.
    /// </exception>
    /// <exception cref="InvalidOperationException">The transaction is already void.</exception>
    public void Void(string reason, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        if (IsVoided)
            throw new InvalidOperationException("This transaction has already been voided.");

        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A void reason is required.", nameof(reason));

        var trimmed = reason.Trim();
        if (trimmed.Length > MaxVoidReasonLength)
            throw new ArgumentException($"Void reason cannot exceed {MaxVoidReasonLength} characters.", nameof(reason));

        VoidedAt = timeProvider.GetUtcNow();
        VoidReason = trimmed;

        AddDomainEvent(new TransactionVoidedEvent(Id, AccountId, TotalAmount, trimmed, VoidedAt.Value));
    }

    /// <summary>
    /// Validates the current split set: zero splits is valid, otherwise the splits must sum to <see cref="TotalAmount"/>.
    /// </summary>
    public void ValidateSplits() => ValidateSplitSum(_splits);

    /// <summary>
    /// Validates the current line item set: zero line items is valid, otherwise every line —
    /// products and adjustments alike — must together sum to <see cref="TotalAmount"/> (TD-19).
    /// </summary>
    public void ValidateLineItems() => ValidateLineItemSum(_lineItems);

    /// <summary>
    /// Aggregate-boundary invariant check. Every write path must call this before persisting;
    /// it is the single entry point for whole-aggregate validation.
    /// </summary>
    public void EnsureValid()
    {
        ValidateSplits();
        ValidateLineItems();
    }

    /// <summary>
    /// How far the current line item set is from reconciling: <see cref="TotalAmount"/> minus the
    /// sum of every line. Zero when the set reconciles, and zero for an empty set.
    /// </summary>
    /// <remarks>
    /// Exposed so that a caller can PRESENT the difference — and offer to record it as an
    /// adjustment line — instead of only discovering, at save time, that it exists. The receipt
    /// scanner is the reason: telling a user "this does not add up" without telling them by how much
    /// is not a usable error.
    /// </remarks>
    public Money LineItemDifference() => TotalAmount - SumOfLineItems(_lineItems, TotalAmount.Currency);

    /// <summary>
    /// Whether any line in the current set predates <see cref="LineItemKind"/> and therefore
    /// suspends the reconciliation invariant for the whole set.
    /// </summary>
    public bool HasLineItemsPredatingKinds => _lineItems.Exists(li => li.Kind is null);

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

    /// <summary>
    /// The TD-19 invariant: a non-empty line item set must sum EXACTLY to
    /// <see cref="TotalAmount"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THERE IS NO TOLERANCE, AND THE DERIVATION IS THAT NONE IS AVAILABLE. Every
    /// <see cref="Money"/> is normalised to a whole number of its currency's minor units by its own
    /// constructor (TD-18), and this sum is taken over the STORED line totals rather than
    /// recomputed from quantity times unit price. A sum of exact multiples of the minor unit is an
    /// exact multiple of the minor unit, so the largest representable residue is zero. An epsilon
    /// here would not be absorbing arithmetic noise, because there is none to absorb; it would be
    /// absorbing tax, a tip or a merchant's cash rounding — amounts one to four orders of magnitude
    /// larger than any epsilon anybody would dare write down, and amounts the user is entitled to
    /// see named. That is what <see cref="LineItemKind"/> is for.
    /// </para>
    /// <para>
    /// AN EMPTY SET IS VALID, exactly as for splits. Most transactions have no line items at all,
    /// and "I did not itemise this" is not a contradiction.
    /// </para>
    /// <para>
    /// A SET CONTAINING A LINE WITH NO KIND IS NOT VALIDATED. Such a line can only have been
    /// materialised from a vault written before this invariant existed, where a receipt's products
    /// were stored without the tax and tip lines that would have made them add up. That data is
    /// INCOMPLETE, not contradictory, and there are only three things to do with it: invent the
    /// missing adjustment line, which fabricates evidence; refuse it, which would make every
    /// pre-existing scanned receipt impossible to void or correct and would silently break the
    /// TD-02 correction path; or leave it alone and say so. This does the third. The hole is as
    /// narrow as it can be made: <see cref="AddLineItem"/> takes a non-nullable kind, so the only
    /// way to re-enter this state through the domain is to pass a null kind to
    /// <see cref="ReplaceLineItems"/> on purpose, and the only caller that does is the repository
    /// merge path preserving what it just read.
    /// </para>
    /// </remarks>
    private void ValidateLineItemSum(List<TransactionLineItem> lineItems)
    {
        if (lineItems.Count == 0)
        {
            return;
        }

        if (lineItems.Exists(li => li.Kind is null))
        {
            return;
        }

        var sum = SumOfLineItems(lineItems, TotalAmount.Currency);
        if (sum != TotalAmount)
        {
            throw new InvalidOperationException(
                $"The sum of the line items ({sum}) must equal the total transaction amount ({TotalAmount}). " +
                $"Record the {TotalAmount - sum} difference as an adjustment line — tax, tip, discount, " +
                "deposit, rounding or unattributed — rather than leaving it unexplained.");
        }
    }

    private static Money SumOfLineItems(List<TransactionLineItem> lineItems, CurrencyCode currency) =>
        lineItems.Aggregate(Money.Zero(currency), (current, lineItem) => current + lineItem.TotalPrice);
}
