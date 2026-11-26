using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Pine.Core.Transactions;

namespace Pine.Infrastructure.Data;

/// <remarks>
/// PERSISTENCE IS NOT COMMITTED HERE (TD-01). This repository only stages changes on the scoped
/// <see cref="PineDbContext"/>; the write is flushed and committed by
/// <c>Pine.Core.Common.IUnitOfWork</c>, driven by the application layer's
/// <c>TransactionBehavior</c>. A repository that called <c>SaveChangesAsync</c> itself would make
/// every write its own implicit transaction, which is exactly the defect this class no longer has.
/// Any write path that does not go through a MediatR request marked <c>ITransactionalRequest</c>
/// will therefore never be persisted.
/// </remarks>
public sealed class SqliteTransactionRepository : ITransactionRepository
{
    private readonly PineDbContext _dbContext;

    public SqliteTransactionRepository(PineDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Transaction?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Transactions
            .Include(t => t.Splits)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<Transaction>> GetByAccountIdAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Transactions
            .Include(t => t.Splits)
            .Where(t => t.AccountId == accountId)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Transaction>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Transactions
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> CountPossiblyCategorisedAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Transactions
            .AsNoTracking()
            .Where(t => t.Splits.Any(s => s.Category != null && s.Category.Trim() != ""))
            .CountAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Transaction>> GetCategorisedPageAsync(int skip, int take, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(skip);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(take);

        return await _dbContext.Transactions
            .AsNoTracking()
            .Where(t => t.Splits.Any(s => s.Category != null && s.Category.Trim() != ""))
            .OrderBy(t => t.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TransferLinkRow>> GetTransferLinkRowsAsync(CancellationToken cancellationToken = default)
    {
        // One query with correlated sub-selects, so neither the ledger nor the partners are
        // materialised: only the linked rows come back, as plain scalars. Nothing is tracked or written.
        var rows = await _dbContext.Transactions
            .AsNoTracking()
            .Where(t => t.LinkedTransactionId != null)
            .Select(t => new
            {
                t.Id,
                t.AccountId,
                Linked = t.LinkedTransactionId!.Value,
                IsVoided = t.VoidedAt != null,
                PartnerExists = _dbContext.Transactions.Any(p => p.Id == t.LinkedTransactionId),
                PartnerLink = _dbContext.Transactions
                    .Where(p => p.Id == t.LinkedTransactionId)
                    .Select(p => p.LinkedTransactionId)
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new TransferLinkRow(r.Id, r.AccountId, r.Linked, r.IsVoided, r.PartnerExists, r.PartnerLink))
            .ToList();
    }

    public Task<Pine.Core.Common.PagedResult<Transaction>> GetPagedAsync(int page, int pageSize, bool includeVoided, CancellationToken cancellationToken = default) =>
        GetPagedAsync(page, pageSize, includeVoided, accountIds: null, typeFilter: null, searchText: null, cancellationToken);

    public async Task<Pine.Core.Common.PagedResult<Transaction>> GetPagedAsync(
        int page,
        int pageSize,
        bool includeVoided,
        IReadOnlyCollection<Guid>? accountIds,
        TransactionType? typeFilter,
        string? searchText,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Transactions.AsNoTracking();
        if (!includeVoided)
        {
            query = query.Where(t => t.VoidedAt == null);
        }

        if (accountIds is { Count: > 0 })
        {
            var idSet = accountIds.ToList();
            query = query.Where(t => idSet.Contains(t.AccountId));
        }

        if (typeFilter.HasValue)
        {
            var targetType = typeFilter.Value;
            query = query.Where(t => t.Type == targetType);
        }

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            var pattern = $"%{searchText.Trim()}%";
            query = query.Where(t =>
                EF.Functions.Like(t.Payee, pattern)
                || (t.Notes != null && EF.Functions.Like(t.Notes, pattern))
                || t.Splits.Any(s => s.Category != null && EF.Functions.Like(s.Category, pattern)));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            // DateUtc, not Date. Date carries the user's offset and this provider stores it as text
            // with that offset appended, so ordering it compares local wall clocks: a purchase at
            // 23:00+02:00 sorts after one at 18:00-05:00 that happened two hours later. DateUtc is
            // the same instant with no offset in its text form, and it is indexed descending.
            .OrderByDescending(t => t.DateUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new Pine.Core.Common.PagedResult<Transaction>(items.AsReadOnly(), totalCount, page, pageSize);
    }

    /// <summary>
    /// Stages an insert or an update for the whole <see cref="Transaction"/> aggregate, owned
    /// collections included. Nothing is flushed here; see the remarks on this class.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The aggregate violates its split-sum invariant, or its line item reconciliation invariant
    /// (TD-19).
    /// </exception>
    /// <remarks>
    /// THE KIND IS CARRIED THROUGH ON THE DETACHED PATH, INCLUDING WHEN IT IS NULL. This is the one
    /// caller in the solution that legitimately passes a null <c>LineItemDefinition.Kind</c>: it is
    /// re-describing children it has just materialised, and flattening a legacy row's absent kind
    /// to <c>Product</c> here would assert the transaction reconciles when the data has never said
    /// so, turning a merge into a throw.
    /// </remarks>
    public async Task SaveAsync(Transaction transaction, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        // Aggregate boundary. AddSplit no longer validates the split sum eagerly (a multi-split set
        // is built up incrementally and is only meaningful once complete), so this is the last point
        // at which an inconsistent aggregate can be stopped before it reaches the database.
        transaction.EnsureValid();

        // FindAsync returns the tracked instance when one is already in the change tracker, and
        // otherwise queries. Either way the owned collections come back populated: EF Core always
        // loads owned navigations with their owner, so no Include is needed to see Splits/LineItems.
        var existing = await _dbContext.Transactions
            .FindAsync(new object[] { transaction.Id }, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            _dbContext.Transactions.Add(transaction);
            return;
        }

        if (!ReferenceEquals(existing, transaction))
        {
            // A detached aggregate over an existing row. CurrentValues.SetValues copies the scalars
            // and the TotalAmount complex property but never touches owned collections (TD-13), so
            // both collections are replaced explicitly, through the domain, on the tracked instance.
            _dbContext.Entry(existing).CurrentValues.SetValues(transaction);

            // The ids travel with the definitions (TD-60). Without them every child here would be a
            // new child, and the merge below would delete and re-insert the whole collection on a
            // save that changed one word of one note.
            existing.ReplaceSplits(transaction.Splits
                .Select(s => new SplitDefinition(s.Amount, s.Category, s.Notes, s.Id)));
            existing.ReplaceLineItems(transaction.LineItems
                .Select(li => new LineItemDefinition(li.Description, li.Quantity, li.UnitPrice, li.TotalPrice, li.Kind, li.Id)));
        }

        // Runs for the already-tracked case too: the caller mutated the very instance this context
        // tracks, and its new owned children still need their state corrected.
        await ReconcileOwnedChildrenAsync(existing, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Corrects the state EF Core assigns to owned children that were added to a tracked owner.
    /// </summary>
    /// <remarks>
    /// EF Core infers the state of an owned child it discovers on an already-tracked owner from
    /// whether that child's key is set. <see cref="TransactionSplit"/> and
    /// <see cref="TransactionLineItem"/> carry a client-assigned <see cref="Guid"/> key, which is
    /// never the default, so EF always guesses <see cref="EntityState.Modified"/> and emits an
    /// UPDATE against a row that does not exist yet — the flush then fails with a zero-rows-affected
    /// concurrency error.
    /// <para>
    /// THE RULE THIS METHOD USED TO APPLY IS NO LONGER TRUE, AND THAT IS WHY IT ASKS THE DATABASE.
    /// It used to promote EVERY Modified child, on the stated grounds that "neither type exposes a
    /// mutator, so a child that came from the database can never legitimately be modified". TD-60's
    /// fix gives both types a mutator, so a Modified child is now genuinely ambiguous: it is either
    /// a new child EF mis-classified or an existing one that was actually edited, and promoting the
    /// second kind would INSERT a row whose primary key is already taken. The only thing that
    /// distinguishes them is whether the row exists, so that is what gets asked — once per
    /// collection, on the update path only, never on an insert.
    /// </para>
    /// Removed children are orphaned owned dependents and are deleted by EF without help.
    /// </remarks>
    private async Task ReconcileOwnedChildrenAsync(Transaction transaction, CancellationToken cancellationToken)
    {
        // Attach the children the caller added to the navigation collections before inspecting them.
        _dbContext.ChangeTracker.DetectChanges();

        var persistedSplitIds = await PersistedChildIdsAsync(
            transaction.Id, t => t.Splits.Select(s => s.Id), cancellationToken).ConfigureAwait(false);

        var persistedLineItemIds = await PersistedChildIdsAsync(
            transaction.Id, t => t.LineItems.Select(li => li.Id), cancellationToken).ConfigureAwait(false);

        // TD-59: the state of every owned child is SET from what the database holds, never inferred
        // from EF's guess about a client-assigned key (or from an assumption that the owned types are
        // immutable). Not persisted -> Added; persisted and still present -> never Added;
        // persisted and gone from the collection -> Deleted.
        SetStates(transaction.Splits, s => s.Id, persistedSplitIds);
        SetStates(transaction.LineItems, li => li.Id, persistedLineItemIds);
    }

    private void SetStates<TChild>(
        IEnumerable<TChild> current, Func<TChild, Guid> idOf, HashSet<Guid> persistedIds)
        where TChild : class
    {
        var currentIds = new HashSet<Guid>();

        foreach (var child in current)
        {
            var id = idOf(child);
            currentIds.Add(id);
            var entry = _dbContext.Entry(child);

            if (!persistedIds.Contains(id))
            {
                entry.State = EntityState.Added;
            }
            else if (entry.State is EntityState.Added or EntityState.Detached or EntityState.Deleted)
            {
                // An existing row can never be inserted. Modified (not Unchanged) because the caller
                // has re-described it; an untouched child keeps EF's Unchanged and costs no UPDATE.
                entry.State = EntityState.Modified;
            }
        }

        // Tracked children whose row exists but which are no longer in the collection. The persisted
        // set is scoped to this transaction, so membership also proves ownership.
        foreach (var entry in _dbContext.ChangeTracker.Entries<TChild>().ToList())
        {
            var id = idOf(entry.Entity);
            if (persistedIds.Contains(id) && !currentIds.Contains(id) && entry.State != EntityState.Deleted)
            {
                entry.State = EntityState.Deleted;
            }
        }
    }

    /// <summary>
    /// The ids of one owned collection as the database currently holds them.
    /// </summary>
    /// <remarks>
    /// <c>AsNoTracking</c> is not an optimisation here, it is a requirement: this reads the very
    /// rows whose in-memory counterparts the change tracker is holding, and tracking the result
    /// would attach a second instance of each under a key already taken.
    /// </remarks>
    private async Task<HashSet<Guid>> PersistedChildIdsAsync(
        Guid transactionId,
        Expression<Func<Transaction, IEnumerable<Guid>>> childIds,
        CancellationToken cancellationToken)
    {
        var ids = await _dbContext.Transactions
            .AsNoTracking()
            .Where(t => t.Id == transactionId)
            .SelectMany(childIds)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new HashSet<Guid>(ids);
    }

    /// <summary>
    /// Returns the subset of <paramref name="candidateKeys"/> already used by transactions on the
    /// given account. Read-only; nothing is staged or flushed.
    /// </summary>
    /// <remarks>
    /// The comparison runs IN SQL, against the <c>(AccountId, ImportKey)</c> index, and only the
    /// matching keys are materialised. This is deliberate: the defect being fixed loaded every
    /// existing transaction for the account into memory to de-duplicate against it, so replacing
    /// that with a second full load would miss the point entirely. Pushing the predicate down is
    /// safe here because <see cref="Transaction.ImportKey"/> is a string — SQLite stores decimals as
    /// TEXT (TD-31), so a numeric key would have been compared lexicographically by the database and
    /// matched rows that do not match in memory.
    /// </remarks>
    /// <param name="accountId">The account being imported into.</param>
    /// <param name="candidateKeys">The keys of the rows about to be imported.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An ordinal-comparison set of the keys already present on that account.</returns>
    public async Task<IReadOnlyCollection<string>> GetExistingImportKeysAsync(
        Guid accountId,
        IReadOnlyCollection<string> candidateKeys,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidateKeys);

        // An empty candidate set has an empty answer by definition; issuing a query with an empty
        // IN list would cost a round trip to learn that.
        if (candidateKeys.Count == 0)
        {
            return Array.Empty<string>();
        }

        var matches = await _dbContext.Transactions
            .AsNoTracking()
            .Where(t => t.AccountId == accountId
                        && t.ImportKey != null
                        && candidateKeys.Contains(t.ImportKey))
            .Select(t => t.ImportKey!)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Ordinal, so the caller's lookups cannot be re-interpreted by the machine's culture; an
        // import key is an opaque token, never a word to be compared linguistically.
        return new HashSet<string>(matches, StringComparer.Ordinal);
    }

    /// <summary>
    /// Returns the transaction on the given account carrying the given import key, if one exists.
    /// Read-only; nothing is staged or flushed.
    /// </summary>
    /// <remarks>
    /// The comparison runs IN SQL against the same <c>(AccountId, ImportKey)</c> index
    /// <see cref="GetExistingImportKeysAsync"/> relies on — the migration that added it
    /// (<c>20260827141109</c>) already covers this shape of query, so no new index is needed here.
    /// </remarks>
    /// <param name="accountId">The account being checked.</param>
    /// <param name="importKey">The key to look up.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The matching transaction, or null when the account holds none with that key.</returns>
    public async Task<Transaction?> GetByImportKeyAsync(
        Guid accountId,
        string importKey,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Transactions
            .Include(t => t.Splits)
            .FirstOrDefaultAsync(t => t.AccountId == accountId && t.ImportKey == importKey, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the signed amount of every transaction on the account that is still in force.
    /// Read-only; nothing is staged or flushed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE PROJECTION IS THE POINT. Only <c>TotalAmount_Amount</c> is read, and the addition happens
    /// in .NET over <see cref="decimal"/>. Writing this as a <c>SumAsync</c> would push the addition
    /// into SQLite, which stores decimals as TEXT (TD-31) and would coerce them to REAL to add them —
    /// so the recomputed total would be an IEEE-754 approximation of the ledger. Repairing a balance
    /// with an approximate total is a worse defect than the drift being repaired, so the exact path is
    /// the only acceptable one and the extra rows are the price of it.
    /// </para>
    /// <para>
    /// The filter is <c>VoidedAt == null</c> rather than the domain's <c>IsVoided</c>, which is
    /// unmapped and would force the whole scan into memory before filtering. Both express the same
    /// rule; only this one is translatable.
    /// </para>
    /// </remarks>
    /// <param name="accountId">The account whose ledger is being totalled.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One signed amount per transaction still in force.</returns>
    public async Task<IReadOnlyList<decimal>> GetAmountsInForceAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        var amounts = await _dbContext.Transactions
            .AsNoTracking()
            .Where(t => t.AccountId == accountId && t.VoidedAt == null)
            .Select(t => t.TotalAmount.Amount)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return amounts;
    }

    /// <summary>
    /// Returns the distinct currency codes on the account's transactions that are still in force.
    /// Read-only; nothing is staged or flushed.
    /// </summary>
    /// <remarks>
    /// The <c>Distinct</c> runs IN SQL, so a healthy account costs one row regardless of how many
    /// transactions it holds — this is a pre-condition check on a maintenance command, and making it
    /// scan the ledger a second time in memory would be paying the price of the very scan it exists
    /// to authorise. Currency codes are three ASCII letters, so unlike the decimal columns (TD-31)
    /// there is nothing about pushing this comparison down that changes its meaning.
    /// </remarks>
    /// <param name="accountId">The account being inspected.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An ordinal-comparison set of the distinct currency codes.</returns>
    public async Task<IReadOnlyCollection<string>> GetDistinctCurrenciesInForceAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        var currencies = await _dbContext.Transactions
            .AsNoTracking()
            .Where(t => t.AccountId == accountId && t.VoidedAt == null)
            .Select(t => t.TotalAmount.Currency.Value)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new HashSet<string>(currencies, StringComparer.Ordinal);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var transaction = await GetByIdAsync(id, cancellationToken);
        if (transaction != null)
        {
            _dbContext.Transactions.Remove(transaction);
        }
    }
}
