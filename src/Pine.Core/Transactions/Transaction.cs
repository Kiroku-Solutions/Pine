using System;
using System.Collections.Generic;
using System.Linq;
using Pine.Core.Common;
using Pine.Core.Common.ValueObjects;

namespace Pine.Core.Transactions;

/// <summary>
/// Transaction aggregate root.
/// </summary>
public sealed class Transaction : AggregateRoot
{
    public Guid AccountId { get; private set; }
    public DateTimeOffset Date { get; private set; }

    /// <summary>
    /// The same instant as <see cref="Date"/>, in UTC. This is the column the store sorts and
    /// indexes on; <see cref="Date"/> remains the value shown to the user.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Date"/> is a <see cref="DateTimeOffset"/> and the SQLite provider persists it as
    /// text with the offset appended — <c>2026-08-20 23:00:00+02:00</c>. Ordering that text
    /// lexicographically orders by local wall clock, not by instant, and Pine writes both kinds of
    /// offset: the add-transaction screen defaults to <c>DateTimeOffset.Now</c> and the CSV importer
    /// parses without one, while other paths write UTC. Two transactions two hours apart were
    /// therefore returned in the wrong order by the only server-side <c>ORDER BY</c> in the
    /// application, which is the transaction list's.
    /// </para>
    /// <para>
    /// Storing the instant separately fixes the ordering without changing any date a user sees.
    /// Normalising <see cref="Date"/> to UTC instead would have been simpler and wrong: it moves a
    /// 23:00 purchase in Madrid to 21:00, and a 22:00 purchase in Santiago onto the following day.
    /// The offset is information about when the money was actually spent, and discarding it to make
    /// a sort work is the wrong trade.
    /// </para>
    /// <para>
    /// It is derived here, in the only place <see cref="Date"/> is ever assigned, so the two cannot
    /// drift apart. There is no edit path that could change one without the other (TD-68).
    /// </para>
    /// </remarks>
    public DateTime DateUtc { get; private set; }
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
    /// its own repository, its own write path and its own foreign key, and would buy no guarantee
    /// this does not already give — the transaction itself is the thing that either is or is not a
    /// duplicate.
    /// </para>
    /// <para>
    /// WHY IT IS A STRING: when this was written SQLite stored every <see cref="decimal"/> as TEXT,
    /// so a numeric key pushed into a SQL predicate would have compared lexicographically and matched
    /// the wrong rows. TD-31 has since moved money to scaled integers, so that particular hazard is
    /// gone — but the reason to keep a string stands on its own: the key is an identity, not a
    /// quantity, and it compares identically in memory and in the database.
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
    /// Gets the key the pre-h2 derivation (scale-sensitive, TD-102) would have given this row, so a
    /// file imported before that fix is still recognised as already imported. Never persisted; only
    /// set by importers, only read by the import de-duplication probe.
    /// </summary>
    public string? LegacyImportKey { get; private set; }

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

    /// <summary>
    /// Whether this transaction was brought in by a file import with an amount of zero and has not been
    /// dealt with (TD-62, owner decision).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A zero-amount row is almost always a bank "authorization" line. The import neither drops nor rejects
    /// it -- the user decides -- so it is saved and TAGGED here, and the import review list shows it with
    /// the option to hide it (void it). The tag is persisted (<c>IsZeroAmountImport</c>), not derived from
    /// the amount, so it records that the row ARRIVED as a zero import: a later edit to a non-zero amount
    /// does not need to clear it for the review list to stay truthful about how the row got there.
    /// </para>
    /// <para>
    /// MANUAL ENTRY OF ZERO STAYS REJECTED. Nothing but <see cref="FlagAsZeroAmountImport"/> sets this, and
    /// the only caller is the import handler; <c>AddTransactionCommandValidator</c> is untouched and still
    /// refuses a zero amount. Such a row has no effect on a balance, so the handler skips
    /// <c>Account.Deposit</c>/<c>Withdraw</c> (which stay strictly positive) rather than weakening them.
    /// It keeps its <see cref="ImportKey"/>, so re-importing the same file still deduplicates it.
    /// </para>
    /// </remarks>
    public bool IsZeroAmountImport { get; private set; }

    /// <summary>Tags this transaction as a zero-amount import. Only a transaction whose amount is zero may be.</summary>
    /// <exception cref="InvalidOperationException">The amount is not zero.</exception>
    public void FlagAsZeroAmountImport()
    {
        if (TotalAmount.Amount != 0m)
            throw new InvalidOperationException("Only a zero-amount transaction can be flagged as a zero-amount import.");

        IsZeroAmountImport = true;
    }

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
        DateUtc = date.UtcDateTime;
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
    }

    /// <summary>
    /// Creates a single, unlinked transaction.
    /// </summary>
    /// <remarks>
    /// THERE IS DELIBERATELY NO <c>linkedTransactionId</c> PARAMETER (TD-26). A link is only ever
    /// meaningful in pairs -- a transaction that points at a partner which does not point back is not
    /// a transfer, it is a half-transfer, and <c>VoidTransactionCommandHandler</c> follows the link
    /// to decide what else has to be voided. This factory used to accept the identifier of the other
    /// leg, which meant a caller could establish one direction and forget the other. The only
    /// legitimate use of that parameter was building a transfer, and building a transfer now has its
    /// own operation: <see cref="CreateTransferPair"/>, which cannot produce a one-way link because
    /// it writes both directions itself. Removing the parameter is what turns "please remember to
    /// link both ways" into something the compiler enforces.
    /// </remarks>
    /// <param name="accountId">The account the transaction belongs to. Required.</param>
    /// <param name="date">When the money moved, as the user understands it.</param>
    /// <param name="payee">Who the money was paid to, or received from.</param>
    /// <param name="totalAmount">The signed amount, whose sign must agree with <paramref name="type"/>.</param>
    /// <param name="type">Expense, income or transfer.</param>
    /// <param name="timeProvider">Clock used to stamp <see cref="CreatedAt"/> and to reject a future date.</param>
    /// <param name="notes">Free text the user attached to the transaction.</param>
    /// <returns>The new transaction, with a freshly generated identifier.</returns>
    /// <exception cref="ArgumentException"><paramref name="accountId"/> is empty.</exception>
    public static Transaction Create(Guid accountId, DateTimeOffset date, string payee, Money totalAmount, TransactionType type, TimeProvider timeProvider, string notes = "")
    {
        if (accountId == Guid.Empty)
            throw new ArgumentException("Account ID is required", nameof(accountId));

        return new Transaction(Guid.NewGuid(), accountId, date, payee, totalAmount, type, null, notes, timeProvider);
    }

    /// <summary>
    /// Creates both legs of a transfer in one operation, each already linked to the other.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY THIS EXISTS AT ALL (TD-26). A transfer is two transactions that must know about each
    /// other, and the handler that built them used to establish the two directions by two DIFFERENT
    /// mechanisms: the receiving leg was linked through the creating factory's
    /// <c>linkedTransactionId</c> parameter, and the sending leg was linked afterwards by reaching
    /// through <see cref="LinkedTransactionId"/>'s private setter with reflection, because at the
    /// point the first leg was constructed the second one did not yet have an identifier. Two
    /// mechanisms means two things that can break independently, and they are not equally harmless
    /// when they do.
    /// </para>
    /// <para>
    /// WHY LOSING THE SOURCE-TO-DESTINATION DIRECTION IS THE EXPENSIVE ONE.
    /// <c>VoidTransactionCommandHandler</c> follows <see cref="LinkedTransactionId"/> to find the far
    /// leg. A user voids the transfer from the account it left -- the sending leg -- so it is that
    /// leg's link the void depends on. Delete the reflection line and everything still compiles,
    /// every transfer still shows two rows and both balances still move; the damage only appears
    /// later, when voiding that transfer takes the money back out of one account and leaves it in
    /// the other. That is a silent rows-versus-balances disagreement produced by a missing line
    /// nothing was watching.
    /// </para>
    /// <para>
    /// WHY A PAIR FACTORY RATHER THAN A TIDIER SETTER. Making the link publicly settable, or adding
    /// a <c>LinkTo</c> method, would replace reflection with a supported call and change nothing
    /// about the failure: both directions would still be two separate steps a caller can half
    /// perform. Constructing both legs here -- inside the class, where the private setter is simply
    /// in scope -- makes a half-linked transfer UNCONSTRUCTIBLE rather than merely discouraged. With
    /// <see cref="Create"/> no longer accepting a link, this is the only way to produce a linked
    /// transaction at all, so there is no second path left to forget.
    /// </para>
    /// <para>
    /// The guards below duplicate rules <c>TransferFundsCommandValidator</c> already applies, and
    /// that is intended: the validator exists to turn them into messages a UI can bind to before any
    /// work starts, while these exist so the invariant holds for every caller, including one that
    /// does not go through the command pipeline.
    /// </para>
    /// </remarks>
    /// <param name="sourceAccountId">The account the money leaves. Must differ from <paramref name="destinationAccountId"/>.</param>
    /// <param name="destinationAccountId">The account the money arrives on.</param>
    /// <param name="date">When the transfer happened, applied identically to both legs.</param>
    /// <param name="sourcePayee">The payee recorded on the sending leg -- conventionally "Transfer to {destination}".</param>
    /// <param name="destinationPayee">The payee recorded on the receiving leg -- conventionally "Transfer from {source}".</param>
    /// <param name="amount">The amount transferred, strictly positive. The sending leg is given its negation.</param>
    /// <param name="timeProvider">Clock used to stamp <see cref="CreatedAt"/> and to reject a future date.</param>
    /// <param name="notes">Free text applied identically to both legs.</param>
    /// <returns>The two legs, each carrying the other's identifier in <see cref="LinkedTransactionId"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="amount"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="sourceAccountId"/> or <paramref name="destinationAccountId"/> is empty, the two
    /// account identifiers are equal, or <paramref name="amount"/> is not strictly positive.
    /// </exception>
    public static TransferLegs CreateTransferPair(
        Guid sourceAccountId,
        Guid destinationAccountId,
        DateTimeOffset date,
        string sourcePayee,
        string destinationPayee,
        Money amount,
        TimeProvider timeProvider,
        string notes = "")
    {
        if (sourceAccountId == Guid.Empty)
            throw new ArgumentException("Account ID is required", nameof(sourceAccountId));

        if (destinationAccountId == Guid.Empty)
            throw new ArgumentException("Account ID is required", nameof(destinationAccountId));

        if (sourceAccountId == destinationAccountId)
            throw new ArgumentException("A transfer must move money between two different accounts.", nameof(destinationAccountId));

        ArgumentNullException.ThrowIfNull(amount);

        if (amount.Amount <= 0)
            throw new ArgumentException("Transfer amount must be greater than zero.", nameof(amount));

        // Both identifiers are generated up front so each leg can be constructed already knowing the
        // other one. That is the whole trick: it is the reason the second link no longer has to be
        // written after the fact, and therefore the reason it can no longer be omitted.
        var sourceId = Guid.NewGuid();
        var destinationId = Guid.NewGuid();

        var source = new Transaction(
            sourceId, sourceAccountId, date, sourcePayee, -amount, TransactionType.Transfer, destinationId, notes, timeProvider);

        var destination = new Transaction(
            destinationId, destinationAccountId, date, destinationPayee, amount, TransactionType.Transfer, sourceId, notes, timeProvider);

        return new TransferLegs(source, destination);
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
    /// <param name="legacyKey">The pre-h2 key, probed (never stored) by import de-duplication (TD-102).</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="key"/> is null, empty, whitespace, or longer than 128 characters once trimmed.
    /// </exception>
    public void AssignImportKey(string key, string? legacyKey = null)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Import key cannot be empty.", nameof(key));

        var trimmed = key.Trim();
        if (trimmed.Length > MaxImportKeyLength)
            throw new ArgumentException($"Import key cannot exceed {MaxImportKeyLength} characters.", nameof(key));

        ImportKey = trimmed;
        LegacyImportKey = string.IsNullOrWhiteSpace(legacyKey) ? null : legacyKey.Trim();
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
    /// <remarks>
    /// A DEFINITION CARRYING AN ID THAT IS ALREADY HERE UPDATES THAT SPLIT INSTEAD OF REPLACING IT
    /// (TD-60). The set is still replaced wholesale as far as the caller is concerned — anything not
    /// named is gone — but a split that survives the replacement keeps the identity it had, rather
    /// than being deleted and re-inserted under a new <see cref="Guid"/> on every save.
    /// <para>
    /// The staging is now over DEFINITIONS rather than over freshly built splits, and that is what
    /// preserves this method's stated guarantee: the sum is checked before a single existing child
    /// is touched, so a rejected set still leaves the aggregate completely unmutated. Building the
    /// children first and validating them afterwards would have been a mutation before a throw.
    /// </para>
    /// </remarks>
    public void ReplaceSplits(IEnumerable<SplitDefinition> splits)
    {
        var staged = new List<SplitDefinition>();

        if (splits is not null)
        {
            foreach (var definition in splits)
            {
                if (definition.Amount is null)
                    throw new ArgumentException("Split amount is required.", nameof(splits));

                if (definition.Amount.Currency != TotalAmount.Currency)
                    throw new ArgumentException("Split currency must match transaction currency.", nameof(splits));

                staged.Add(definition);
            }
        }

        ValidateAmountsSumToTotal(
            staged.ConvertAll(definition => definition.Amount),
            "The sum of splits must equal the total transaction amount.");

        var survivors = new Dictionary<Guid, TransactionSplit>();
        foreach (var split in _splits)
        {
            survivors[split.Id] = split;
        }

        var applied = new List<TransactionSplit>(staged.Count);
        foreach (var definition in staged)
        {
            // Remove on match, so a set naming the same id twice produces two rows rather than one
            // instance tracked under one key twice — which EF Core refuses outright.
            if (definition.Id != Guid.Empty && survivors.Remove(definition.Id, out var existing))
            {
                existing.Update(definition.Amount, definition.Category, definition.Notes);
                applied.Add(existing);
            }
            else
            {
                applied.Add(new TransactionSplit(Id, definition.Amount, definition.Category, definition.Notes));
            }
        }

        _splits.Clear();
        _splits.AddRange(applied);
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
        var staged = new List<LineItemDefinition>();

        if (lineItems is not null)
        {
            foreach (var definition in lineItems)
            {
                if (definition.UnitPrice is null || definition.TotalPrice is null)
                    throw new ArgumentException("Line item unit price and total price are required.", nameof(lineItems));

                if (definition.UnitPrice.Currency != TotalAmount.Currency || definition.TotalPrice.Currency != TotalAmount.Currency)
                    throw new ArgumentException("Line item currency must match transaction currency.", nameof(lineItems));

                // Before anything is touched: a rejected set must leave the aggregate unmutated (TD-95).
                LineQuantity.EnsureValid(definition.Quantity);

                staged.Add(definition);
            }
        }

        ValidateStagedLineItemSum(staged);

        var survivors = new Dictionary<Guid, TransactionLineItem>();
        foreach (var lineItem in _lineItems)
        {
            survivors[lineItem.Id] = lineItem;
        }

        var applied = new List<TransactionLineItem>(staged.Count);
        foreach (var definition in staged)
        {
            if (definition.Id != Guid.Empty && survivors.Remove(definition.Id, out var existing))
            {
                existing.Update(definition.Description, definition.Quantity, definition.UnitPrice, definition.TotalPrice, definition.Kind);
                applied.Add(existing);
            }
            else
            {
                applied.Add(new TransactionLineItem(Id, definition.Description, definition.Quantity, definition.UnitPrice, definition.TotalPrice, definition.Kind));
            }
        }

        _lineItems.Clear();
        _lineItems.AddRange(applied);
    }

    /// <summary>
    /// Updates the mutable recording details of this transaction: when it happened, who it was paid
    /// to or received from, its signed amount, and any notes -- without touching its identity, its
    /// <see cref="ImportKey"/>, its <see cref="AccountId"/>, or its linked leg.
    /// </summary>
    /// <remarks>
    /// <para>
    /// REFUSED ON A VOIDED TRANSACTION, ON PURPOSE (TD-68, closing against TD-02). <see cref="Void"/>'s
    /// own contract is a cancellation that deliberately KEEPS the row -- the row stops counting but its
    /// last true values stay on it as the record of what was cancelled. Letting an edit change a voided
    /// row's amount, date or payee would silently rewrite the thing the void exists to preserve, through
    /// a door that has nothing to do with un-voiding it. Unvoiding is TD-69, a separate and still-open
    /// ticket; this method does not reach for it, and refuses cleanly instead of reaching past it.
    /// </para>
    /// <para>
    /// REFUSED ON A TRANSFER LEG, ON PURPOSE. A transfer is two linked transactions on two accounts,
    /// constructed together so neither can exist without the other (<see cref="CreateTransferPair"/>).
    /// Editing one leg's amount here would leave the other leg -- and the account on its far side --
    /// silently out of step, with no caller pairing the edit the way
    /// <c>VoidTransactionCommandHandler</c> pairs a void. Editing a transfer is out of scope for this
    /// close of TD-68; it would need the same both-legs choreography void already has.
    /// </para>
    /// <para>
    /// TYPE IS RE-DERIVED FROM THE NEW AMOUNT'S SIGN, exactly as the add-transaction handler derives it
    /// on create: Income for a non-negative total, Expense for a negative one. A caller that edits an
    /// expense into a positive figure is describing a correction into income, not a different
    /// operation, and "forbid crossing the line" is a rule no screen has ever asked for.
    /// </para>
    /// <para>
    /// SPLITS ARE DELIBERATELY NOT TOUCHED HERE. A caller that is also changing the split set calls
    /// <see cref="ReplaceSplits"/> afterwards, in the same unit of work -- this method only changes the
    /// header fields, so a currency change does not have to simultaneously describe every split for the
    /// currency check inside <see cref="ReplaceSplits"/> to make sense of what just happened.
    /// </para>
    /// </remarks>
    /// <param name="date">When the money moved, as the user now understands it.</param>
    /// <param name="payee">Who the money was paid to, or received from.</param>
    /// <param name="totalAmount">The new signed amount.</param>
    /// <param name="notes">Free text the user attached to the transaction.</param>
    /// <param name="timeProvider">Clock used to reject a future date.</param>
    /// <exception cref="ArgumentNullException"><paramref name="totalAmount"/> or <paramref name="timeProvider"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="date"/> is more than a day in the future.</exception>
    /// <exception cref="InvalidOperationException">
    /// This transaction is voided, or is one leg of a transfer.
    /// </exception>
    public void Update(DateTimeOffset date, string payee, Money totalAmount, string notes, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(totalAmount);
        ArgumentNullException.ThrowIfNull(timeProvider);

        if (IsVoided)
            throw new InvalidOperationException("A voided transaction cannot be edited. Unvoid it first.");

        if (Type == TransactionType.Transfer)
            throw new InvalidOperationException("A transfer leg cannot be edited directly.");

        var now = timeProvider.GetUtcNow();
        if (date > now.AddDays(1))
            throw new ArgumentException("Transaction date cannot be in the future.", nameof(date));

        Date = date;
        DateUtc = date.UtcDateTime;
        Payee = payee;
        TotalAmount = totalAmount;
        Type = totalAmount.Amount >= 0 ? TransactionType.Income : TransactionType.Expense;
        Notes = notes;
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
    }

    /// <summary>
    /// Puts a voided transaction back in force (TD-69), the inverse of <see cref="Void"/>.
    /// </summary>
    /// <remarks>
    /// Like <see cref="Void"/>, this changes the transaction ONLY: re-applying its amount to the account
    /// balance is the caller's job, inside the same database transaction. The row keeps its id and its
    /// <see cref="ImportKey"/> throughout (a void never cleared either), so unvoiding restores exactly
    /// the row that was cancelled. Not idempotent on purpose: a second call means the caller believed
    /// the transaction was void, and accepting it would let a double-unvoid apply the balance twice.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The transaction is not void.</exception>
    public void Unvoid()
    {
        if (!IsVoided)
            throw new InvalidOperationException("This transaction has not been voided.");

        VoidedAt = null;
        VoidReason = null;
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

    private void ValidateSplitSum(List<TransactionSplit> splits) =>
        ValidateAmountsSumToTotal(
            splits.ConvertAll(split => split.Amount),
            "The sum of splits must equal the total transaction amount.");

    /// <summary>
    /// The shared core of both sum invariants, over amounts rather than over children.
    /// </summary>
    /// <remarks>
    /// Taking amounts is what lets <see cref="ReplaceSplits"/> check a candidate set before it has
    /// built anything, which is how "a rejected set leaves the aggregate unmutated" survived the
    /// TD-60 change from building children to updating them.
    /// </remarks>
    private void ValidateAmountsSumToTotal(List<Money> amounts, string message)
    {
        if (amounts.Count == 0)
        {
            return;
        }

        var sum = amounts.Aggregate(Money.Zero(TotalAmount.Currency), (current, amount) => current + amount);
        if (sum != TotalAmount)
        {
            throw new InvalidOperationException(message);
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
            throw LineItemSumFailure(sum);
        }
    }

    /// <summary>
    /// The TD-19 invariant checked over a candidate set, before any child has been touched.
    /// </summary>
    /// <remarks>
    /// The null-kind escape hatch is repeated here rather than shared, and it has to be: a legacy
    /// row whose kind is absent still suspends the invariant, and a staged set is where a legacy
    /// row's kind arrives — the repository's detached path re-describes materialised children and
    /// carries a null kind through deliberately. Losing the check here would reconcile a set the
    /// data has never reconciled.
    /// </remarks>
    private void ValidateStagedLineItemSum(List<LineItemDefinition> staged)
    {
        if (staged.Count == 0)
        {
            return;
        }

        if (staged.Exists(definition => definition.Kind is null))
        {
            return;
        }

        var sum = staged.Aggregate(
            Money.Zero(TotalAmount.Currency),
            (current, definition) => current + definition.TotalPrice);

        if (sum != TotalAmount)
        {
            throw LineItemSumFailure(sum);
        }
    }

    private InvalidOperationException LineItemSumFailure(Money sum) =>
        new($"The sum of the line items ({sum}) must equal the total transaction amount ({TotalAmount}). " +
            $"Record the {TotalAmount - sum} difference as an adjustment line — tax, tip, discount, " +
            "deposit, rounding or unattributed — rather than leaving it unexplained.");

    private static Money SumOfLineItems(List<TransactionLineItem> lineItems, CurrencyCode currency) =>
        lineItems.Aggregate(Money.Zero(currency), (current, lineItem) => current + lineItem.TotalPrice);
}
