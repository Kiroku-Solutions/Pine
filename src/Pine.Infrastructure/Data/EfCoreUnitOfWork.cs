using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Pine.Core.Common;

namespace Pine.Infrastructure.Data;

/// <summary>
/// EF Core / SQLCipher implementation of <see cref="IUnitOfWork"/>.
/// </summary>
/// <remarks>
/// <para>
/// This type shares the Scoped <see cref="PineDbContext"/> with every repository resolved in the
/// same DI scope, so the transaction it opens covers every write those repositories perform.
/// </para>
/// <para>
/// NON-REENTRANT BY DESIGN: <see cref="BeginTransactionAsync"/> throws
/// <see cref="InvalidOperationException"/> when a transaction is already open rather than opening
/// a nested one or silently joining the existing one. SQLite has no real nested transactions
/// (only savepoints), so a "nested" begin would produce a boundary whose commit does not actually
/// commit — the worst possible failure mode for a write path. Throwing turns a mis-nested
/// transactional request into a loud, deterministic failure instead of silent partial durability.
/// </para>
/// </remarks>
public sealed class EfCoreUnitOfWork : IUnitOfWork, IDisposable, IAsyncDisposable
{
    private readonly PineDbContext _dbContext;
    private IDbContextTransaction? _transaction;

    /// <summary>
    /// Initializes a new instance of the <see cref="EfCoreUnitOfWork"/> class.
    /// </summary>
    /// <param name="dbContext">The scoped database context whose changes are being coordinated.</param>
    public EfCoreUnitOfWork(PineDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public bool HasActiveTransaction => _transaction is not null;

    /// <inheritdoc />
    /// <exception cref="Pine.Core.Common.ConcurrencyConflictException">
    /// A row this unit of work is updating was changed by someone else after it was read (TD-17).
    /// </exception>
    /// <remarks>
    /// The translation is here because this is where persistence stops. <c>Pine.Application</c> and
    /// the view models cannot reference <c>DbUpdateConcurrencyException</c> — they do not know EF
    /// exists — so letting it travel outwards would either leak the ORM into every layer or, more
    /// likely, be caught by nothing at all and surfaced to the user as a raw provider message.
    ///
    /// The original is kept as the inner exception so a log still has EF's own account of which
    /// entries failed; only the message the user might see is replaced.
    /// </remarks>
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw Pine.Core.Common.ConcurrencyConflictException.From(ex);
        }
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">A transaction is already open on this unit of work.</exception>
    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is not null)
        {
            throw new InvalidOperationException(
                "A transaction is already active on this unit of work. Nested transactions are not supported; " +
                "the transactional boundary is owned by a single request.");
        }

        _transaction = await _dbContext.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        var transaction = _transaction;
        if (transaction is null)
        {
            // No-op: nothing was ever opened, so there is nothing to commit.
            return;
        }

        try
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // Null the field first so a throwing DisposeAsync cannot leave a dead transaction latched.
            _transaction = null;
            await transaction.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        var transaction = _transaction;
        if (transaction is null)
        {
            // No-op: the caller's catch block may run before BeginTransactionAsync ever succeeded.
            return;
        }

        try
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _transaction = null;
            await transaction.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Disposes any transaction still open (an un-committed transaction is rolled back by EF Core
    /// on dispose), so no <see cref="IDbContextTransaction"/> can leak when the DI scope ends.
    /// </summary>
    public void Dispose()
    {
        _transaction?.Dispose();
        _transaction = null;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Asynchronously disposes any transaction still open.
    /// </summary>
    /// <returns>A task that completes once the transaction has been disposed.</returns>
    public async ValueTask DisposeAsync()
    {
        var transaction = _transaction;
        _transaction = null;
        if (transaction is not null)
        {
            await transaction.DisposeAsync().ConfigureAwait(false);
        }

        GC.SuppressFinalize(this);
    }
}
