using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
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

    public async Task<Pine.Core.Common.PagedResult<Transaction>> GetPagedAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Transactions.AsNoTracking();
        var totalCount = await query.CountAsync(cancellationToken);
        
        var items = await query
            .OrderByDescending(t => t.Date)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
            
        return new Pine.Core.Common.PagedResult<Transaction>(items.AsReadOnly(), totalCount, page, pageSize);
    }

    /// <summary>
    /// Stages an insert or an update for the whole <see cref="Transaction"/> aggregate, owned
    /// collections included. Nothing is flushed here; see the remarks on this class.
    /// </summary>
    /// <exception cref="InvalidOperationException">The aggregate violates its split-sum invariant.</exception>
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
            existing.ReplaceSplits(transaction.Splits
                .Select(s => new SplitDefinition(s.Amount, s.Category, s.Notes)));
            existing.ReplaceLineItems(transaction.LineItems
                .Select(li => new LineItemDefinition(li.Description, li.Quantity, li.UnitPrice, li.TotalPrice)));
        }

        // Runs for the already-tracked case too: the caller mutated the very instance this context
        // tracks, and its new owned children still need their state corrected.
        ReconcileOwnedChildren(existing);
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
    /// concurrency error. Neither type exposes a mutator, so a child that came from the database can
    /// never legitimately be modified: every Modified child here is a new one EF mis-classified, and
    /// promoting it to <see cref="EntityState.Added"/> is the only correct reading. Removed children
    /// are orphaned owned dependents and are deleted by EF without help.
    /// </remarks>
    private void ReconcileOwnedChildren(Transaction transaction)
    {
        // Attach the children the caller added to the navigation collections before inspecting them.
        _dbContext.ChangeTracker.DetectChanges();

        foreach (var split in transaction.Splits)
        {
            var entry = _dbContext.Entry(split);
            if (entry.State == EntityState.Modified)
            {
                entry.State = EntityState.Added;
            }
        }

        foreach (var lineItem in transaction.LineItems)
        {
            var entry = _dbContext.Entry(lineItem);
            if (entry.State == EntityState.Modified)
            {
                entry.State = EntityState.Added;
            }
        }
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
