using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pine.Core.Budgets;

namespace Pine.Infrastructure.Data;

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
        var existing = await _dbContext.Budgets.FindAsync(new object[] { budget.Id }, cancellationToken);
        if (existing == null)
        {
            await _dbContext.Budgets.AddAsync(budget, cancellationToken);
        }
        else
        {
            _dbContext.Entry(existing).CurrentValues.SetValues(budget);
        }
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var budget = await _dbContext.Budgets.FindAsync(new object[] { id }, cancellationToken);
        if (budget != null)
        {
            _dbContext.Budgets.Remove(budget);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
