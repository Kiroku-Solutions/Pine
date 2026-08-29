using System;
using Pine.Core.Common;
using Pine.Core.Common.ValueObjects;

namespace Pine.Core.Events;

/// <summary>
/// Raised when a transaction is voided. Carries the amount whose effect on the account balance
/// has to be undone, so a future dispatcher never has to re-read the transaction to know it.
/// </summary>
/// <remarks>
/// <see cref="Amount"/> is the ORIGINAL signed amount of the voided transaction, not its inverse.
/// The consumer decides what to do with it; encoding the inverse here would bake one consumer's
/// interpretation into the event. Note that nothing dispatches domain events yet (TD-33) -- this is
/// raised for symmetry with <see cref="TransactionRegisteredEvent"/> and is not a delivery guarantee.
/// </remarks>
public sealed record TransactionVoidedEvent(
    Guid TransactionId,
    Guid AccountId,
    Money Amount,
    string Reason,
    DateTimeOffset Timestamp) : IDomainEvent;
