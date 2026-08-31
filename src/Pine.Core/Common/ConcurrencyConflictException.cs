using System;

namespace Pine.Core.Common;

/// <summary>
/// A write was refused because the row it was updating had already been changed by someone else
/// since it was read (TD-17).
/// </summary>
/// <remarks>
/// <para>
/// WHAT THIS PREVENTS, CONCRETELY. Pine dispatches every request through <c>ScopedMediator</c>, which
/// opens its own DI scope, its own <c>DbContext</c> and its own database transaction. Two requests in
/// flight at once therefore read, mutate and write the same row independently. Without a concurrency
/// token the second write silently overwrites the first: a deposit and a withdrawal that overlap
/// leave the account holding whichever balance was computed last, and the other movement is gone from
/// the balance while its transaction row still sits in the ledger. Nothing anywhere reports it.
/// </para>
/// <para>
/// THIS TYPE EXISTS SO THAT EF CORE'S DOES NOT LEAK. The store signals the conflict as a
/// <c>DbUpdateConcurrencyException</c>, which the application and UI layers cannot reference and
/// should not have to. <c>IUnitOfWork</c>'s implementation translates it here, at the boundary where
/// persistence stops.
/// </para>
/// <para>
/// THERE IS DELIBERATELY NO AUTOMATIC RETRY. Re-running the request would re-read the row and
/// recompute from the newer value, which is right for an idempotent write and wrong for the
/// read-modify-write this exists to catch — a retry there would quietly apply the change on top of a
/// state the caller never saw, which is the lost update again with extra steps. The caller is told
/// instead.
/// </para>
/// <para>
/// It carries no entity name, id or row values. Those are the user's financial data, and this
/// exception's message can reach a log or a dialog.
/// </para>
/// </remarks>
public sealed class ConcurrencyConflictException : Exception
{
    private const string DefaultMessage =
        "The data was changed by another operation after it was read, so this change was not applied. " +
        "Reload and try again.";

    public ConcurrencyConflictException()
        : base(DefaultMessage)
    {
    }

    public ConcurrencyConflictException(string message)
        : base(message)
    {
    }

    public ConcurrencyConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Wraps the store's own conflict signal, keeping it as the inner exception for the log while
    /// presenting a message that names nothing.
    /// </summary>
    public static ConcurrencyConflictException From(Exception innerException) =>
        new(DefaultMessage, innerException);
}
