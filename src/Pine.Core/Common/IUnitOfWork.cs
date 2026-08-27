using System.Threading;
using System.Threading.Tasks;

namespace Pine.Core.Common;

/// <summary>
/// Transactional boundary over the persistence store.
/// </summary>
/// <remarks>
/// <para>
/// This port is deliberately expressed with explicit <see cref="BeginTransactionAsync"/> /
/// <see cref="CommitAsync"/> / <see cref="RollbackAsync"/> members rather than a single
/// <c>ExecuteInTransactionAsync(Func&lt;...&gt;)</c> delegate-taking method.
/// </para>
/// <para>
/// WHY: the transaction boundary is driven by a MediatR pipeline behaviour, which must wrap
/// <c>await next(cancellationToken)</c> — the pipeline continuation. A delegate-taking API would
/// force that behaviour to capture the continuation inside a closure handed to the persistence
/// adapter, which muddies two things that must stay obvious: (1) exactly which cancellation token
/// flows into the handler versus into the rollback (rollback must run on
/// <see cref="CancellationToken.None"/> so an already-cancelled request can still be undone), and
/// (2) exception propagation — a bare <c>throw;</c> inside the behaviour's own <c>catch</c> keeps
/// the original stack trace, whereas an exception crossing a delegate boundary owned by the
/// adapter invites catch-and-wrap. Explicit begin/commit/rollback keeps the behaviour readable and
/// the failure semantics auditable.
/// </para>
/// <para>
/// This interface lives in Pine.Core and must remain expressible with zero package references:
/// only <c>System.Threading</c> and <c>System.Threading.Tasks</c> are used. Pine.Infrastructure
/// references Pine.Core only (never Pine.Application), which is why the port belongs here.
/// </para>
/// </remarks>
public interface IUnitOfWork
{
    /// <summary>
    /// Gets a value indicating whether a transaction is currently open on this unit of work.
    /// </summary>
    bool HasActiveTransaction { get; }

    /// <summary>
    /// Flushes all pending tracked changes to the store.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of state entries written.</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a transaction. Implementations must not silently open a nested transaction.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once the transaction is open.</returns>
    Task BeginTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Commits the open transaction. Implementations must treat "no transaction open" as a no-op.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once the transaction is committed and disposed.</returns>
    Task CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Rolls the open transaction back. Implementations must treat "no transaction open" as a
    /// no-op, because a caller's <c>catch</c> block may run before <see cref="BeginTransactionAsync"/>
    /// ever succeeded.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once the transaction is rolled back and disposed.</returns>
    Task RollbackAsync(CancellationToken cancellationToken = default);
}
