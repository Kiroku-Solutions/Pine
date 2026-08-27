using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pine.Core.Categories;

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
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var category = await GetByIdAsync(id, cancellationToken);
        if (category != null)
        {
            _dbContext.Categories.Remove(category);
        }
    }
}
