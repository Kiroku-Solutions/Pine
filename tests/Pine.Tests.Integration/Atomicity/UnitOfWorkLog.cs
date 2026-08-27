using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pine.Core.Common;
using Pine.Infrastructure.Data;

namespace Pine.Tests.Integration.Atomicity;

/// <summary>
/// One call made against the transactional boundary.
/// </summary>
internal enum UnitOfWorkCall
{
    /// <summary>A transaction was opened.</summary>
    Begin,

    /// <summary>Tracked changes were flushed.</summary>
    SaveChanges,

    /// <summary>The open transaction was committed.</summary>
    Commit,

    /// <summary>The open transaction was rolled back.</summary>
    Rollback
}

/// <summary>
/// Singleton sink recording what <c>TransactionBehavior</c> actually did.
/// </summary>
/// <remarks>
/// Asserting on this log is what separates "the database happens to be empty" from "the boundary
/// was opened, then rolled back and never committed". Without it, a test could not tell a genuine
/// rollback from a request that simply never reached the write stage.
/// </remarks>
internal sealed class UnitOfWorkLog
{
    private readonly List<UnitOfWorkCall> _calls = [];
    private readonly Lock _gate = new();

    /// <summary>Gets a snapshot of the calls recorded so far, in order.</summary>
    public IReadOnlyList<UnitOfWorkCall> Calls
    {
        get
        {
            lock (_gate)
            {
                return [.. _calls];
            }
        }
    }

    /// <summary>Records one call.</summary>
    /// <param name="call">The call to record.</param>
    public void Record(UnitOfWorkCall call)
    {
        lock (_gate)
        {
            _calls.Add(call);
        }
    }

    /// <summary>Forgets everything recorded so far (used after seeding).</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _calls.Clear();
        }
    }
}

/// <summary>
/// Transparent decorator over the real <see cref="EfCoreUnitOfWork"/> that records every call.
/// </summary>
/// <remarks>
/// It changes no behaviour: every member delegates straight through to the genuine EF Core adapter,
/// so the transaction the tests observe is a real SQLite transaction on the real SQLCipher
/// connection. Registering it as the scoped <see cref="IUnitOfWork"/> guarantees that the instance
/// <c>TransactionBehavior</c> receives is the instance being recorded.
/// </remarks>
internal sealed class RecordingUnitOfWork : IUnitOfWork
{
    private readonly EfCoreUnitOfWork _inner;
    private readonly UnitOfWorkLog _log;

    public RecordingUnitOfWork(EfCoreUnitOfWork inner, UnitOfWorkLog log)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(log);
        _inner = inner;
        _log = log;
    }

    /// <inheritdoc />
    public bool HasActiveTransaction => _inner.HasActiveTransaction;

    /// <inheritdoc />
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        _log.Record(UnitOfWorkCall.SaveChanges);
        return _inner.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        _log.Record(UnitOfWorkCall.Begin);
        return _inner.BeginTransactionAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task CommitAsync(CancellationToken cancellationToken = default)
    {
        _log.Record(UnitOfWorkCall.Commit);
        return _inner.CommitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        _log.Record(UnitOfWorkCall.Rollback);
        return _inner.RollbackAsync(cancellationToken);
    }
}
