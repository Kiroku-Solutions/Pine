using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pine.Core.Importing;
using Pine.Infrastructure.Data;

namespace Pine.Infrastructure.Importing;

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
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var profile = await GetByIdAsync(id, cancellationToken);
        if (profile != null)
        {
            _dbContext.ImportProfiles.Remove(profile);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
