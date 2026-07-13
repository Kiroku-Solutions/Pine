using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pine.Core.Categories;

namespace Pine.Infrastructure.Data;

public sealed class SqliteCategoryRepository : ICategoryRepository
{
    private readonly PineDbContext _dbContext;

    public SqliteCategoryRepository(PineDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Category?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Categories.FindAsync(new object[] { id }, cancellationToken);
    }

    public async Task<IEnumerable<Category>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Categories.ToListAsync(cancellationToken);
    }

    public async Task SaveAsync(Category category, CancellationToken cancellationToken = default)
    {
        if (!_dbContext.Categories.Local.Contains(category))
        {
            var existing = await _dbContext.Categories.FindAsync(new object[] { category.Id }, cancellationToken);
            if (existing == null)
            {
                await _dbContext.Categories.AddAsync(category, cancellationToken);
            }
            else
            {
                _dbContext.Entry(existing).CurrentValues.SetValues(category);
            }
        }
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var category = await GetByIdAsync(id, cancellationToken);
        if (category != null)
        {
            _dbContext.Categories.Remove(category);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
