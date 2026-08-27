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

    public async Task SaveAsync(Transaction transaction, CancellationToken cancellationToken = default)
    {
        var existing = await _dbContext.Transactions.FindAsync(new object[] { transaction.Id }, cancellationToken);
        if (existing == null)
        {
            _dbContext.Transactions.Add(transaction);
        }
        else
        {
            _dbContext.Entry(existing).CurrentValues.SetValues(transaction);
        }
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
