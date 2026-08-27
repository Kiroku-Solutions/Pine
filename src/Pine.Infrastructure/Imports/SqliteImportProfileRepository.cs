using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pine.Core.Importing;
using Pine.Infrastructure.Data;

namespace Pine.Infrastructure.Importing;

/// <remarks>
/// PERSISTENCE IS NOT COMMITTED HERE (TD-01). This repository only stages changes on the scoped
/// <see cref="PineDbContext"/>; the write is flushed and committed by
/// <c>Pine.Core.Common.IUnitOfWork</c>, driven by the application layer's
/// <c>TransactionBehavior</c>. A repository that called <c>SaveChangesAsync</c> itself would make
/// every write its own implicit transaction, which is exactly the defect this class no longer has.
/// Any write path that does not go through a MediatR request marked <c>ITransactionalRequest</c>
/// will therefore never be persisted.
/// </remarks>
public sealed class SqliteImportProfileRepository : IImportProfileRepository
{
    private readonly PineDbContext _dbContext;

    public SqliteImportProfileRepository(PineDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ImportProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.ImportProfiles.FindAsync(new object[] { id }, cancellationToken);
    }

    public async Task<IReadOnlyList<ImportProfile>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.ImportProfiles.ToListAsync(cancellationToken);
    }

    public async Task SaveAsync(ImportProfile profile, CancellationToken cancellationToken = default)
    {
        var exists = await _dbContext.ImportProfiles.AnyAsync(p => p.Id == profile.Id, cancellationToken);
        if (exists)
        {
            _dbContext.ImportProfiles.Update(profile);
        }
        else
        {
            _dbContext.ImportProfiles.Add(profile);
        }
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var profile = await GetByIdAsync(id, cancellationToken);
        if (profile != null)
        {
            _dbContext.ImportProfiles.Remove(profile);
        }
    }
}
