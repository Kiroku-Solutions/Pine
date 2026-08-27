using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pine.Core.Accounts;

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
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var account = await GetByIdAsync(id, cancellationToken);
        if (account != null)
        {
            _dbContext.Accounts.Remove(account);
        }
    }
}
