using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pine.Core.Transactions;

namespace Pine.Infrastructure.Data;

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
            .Include(t => t.Splits)
            .ToListAsync(cancellationToken);
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

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var transaction = await GetByIdAsync(id, cancellationToken);
        if (transaction != null)
        {
            _dbContext.Transactions.Remove(transaction);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
