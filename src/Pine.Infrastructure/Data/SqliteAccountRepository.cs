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
        ArgumentNullException.ThrowIfNull(account);

        var existing = await _dbContext.Accounts
            .FindAsync(new object[] { account.Id }, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            _dbContext.Accounts.Add(account);
            return;
        }

        if (ReferenceEquals(existing, account))
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
        // Account.Balance is a complex property, so it is covered.
        _dbContext.Entry(existing).CurrentValues.SetValues(account);
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
