using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pine.Core.Accounts;

namespace Pine.Infrastructure.Data;

public sealed class SqliteAccountRepository : IAccountRepository
{
    private readonly PineDbContext _dbContext;

    public SqliteAccountRepository(PineDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Account?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Accounts.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<Account>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Accounts.ToListAsync(cancellationToken);
    }

    public async Task SaveAsync(Account account, CancellationToken cancellationToken = default)
    {
        var existing = await _dbContext.Accounts.FindAsync(new object[] { account.Id }, cancellationToken);
        if (existing == null)
        {
            _dbContext.Accounts.Add(account);
        }
        else
        {
            _dbContext.Entry(existing).CurrentValues.SetValues(account);
        }

        // Using BEGIN IMMEDIATE logic (implicit in EF Core SaveChanges if transaction used, but we'll just save here)
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var account = await GetByIdAsync(id, cancellationToken);
        if (account != null)
        {
            _dbContext.Accounts.Remove(account);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
