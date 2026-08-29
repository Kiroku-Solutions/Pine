using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pine.Core.Budgets;

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
public class SqliteBudgetRepository : IBudgetRepository
{
    private readonly PineDbContext _dbContext;

    public SqliteBudgetRepository(PineDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Budget?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Budgets
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<Budget>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Budgets
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Budget>> GetByCategoryAsync(Guid categoryId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Budgets
            .Where(b => b.CategoryId == categoryId)
            .ToListAsync(cancellationToken);
    }

    public async Task SaveAsync(Budget budget, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(budget);

        var existing = await _dbContext.Budgets
            .FindAsync(new object[] { budget.Id }, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            await _dbContext.Budgets.AddAsync(budget, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (ReferenceEquals(existing, budget))
        {
            // The caller mutated the very instance this context already tracks. Every change is
            // already staged on the change tracker; copying the entity onto itself does nothing.
            return;
        }

        // CurrentValues.SetValues copies scalars and complex properties only; it ignores navigations
        // entirely, so an owned collection would be dropped silently (TD-13). It is sufficient here
        // *because* this aggregate has none. If an OwnsMany navigation is ever added to it, this call
        // must be replaced by an explicit reconciliation of the tracked graph, the way
        // SqliteTransactionRepository.SaveAsync reconciles Transaction.Splits.
        // Budget.Limit is a complex property, so it is covered.
        _dbContext.Entry(existing).CurrentValues.SetValues(budget);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var budget = await _dbContext.Budgets.FindAsync(new object[] { id }, cancellationToken);
        if (budget != null)
        {
            _dbContext.Budgets.Remove(budget);
        }
    }
}
