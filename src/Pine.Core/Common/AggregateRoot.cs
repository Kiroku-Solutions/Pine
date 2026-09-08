using System;

namespace Pine.Core.Common;

/// <summary>
/// Marks an entity that is the root of an aggregate: the unit a repository loads and saves, and the
/// unit optimistic concurrency is applied to.
/// </summary>
/// <remarks>
/// <para>
/// WHAT WAS REMOVED FROM HERE, AND WHY (TD-33). This class used to carry a
/// <c>List&lt;IDomainEvent&gt;</c> with <c>AddDomainEvent</c>, <c>ClearDomainEvents</c> and a public
/// <c>DomainEvents</c> collection. Events were raised in exactly three places — account creation,
/// transaction registration, transaction voiding — and **dispatched nowhere**. No dispatcher
/// existed, no <c>INotificationHandler</c> was ever written, and
/// <c>git log -S "IDomainEventDispatcher"</c> returns no commit on any branch: nothing was ever
/// built and later removed. The list was read only by tests asserting that the list was appended to.
/// </para>
/// <para>
/// THE ROW SAID "DISPATCH OR REMOVE", AND REMOVE IS THE HONEST HALF. The two mutations that raised
/// events already do their work synchronously and atomically in the same handler and the same
/// database transaction — <c>RecordTransactionCommandHandler</c> calls <c>Deposit</c>/<c>Withdraw</c>
/// directly, and <c>VoidTransactionCommandHandler</c> reverses the effect the same way, with a
/// written argument for why it must not recompute the ledger instead. A dispatcher would therefore
/// have had no subscriber to deliver to, and building one would have moved the same emptiness down
/// a layer: scaffolding that looks like machinery is worse than no machinery, because a reader
/// cannot tell the difference without following it to the end.
/// </para>
/// <para>
/// The plan those events gestured at is not lost. <c>10-Domain-Model</c> §5 specifies cross-context
/// events — <c>TransactionRecorded</c>, <c>BudgetExceeded</c>, <c>RuleFired</c> and the rest — with
/// their publishers and subscribers. Note that the deleted types did not implement that
/// specification: the spec names <c>TransactionRecorded</c> carrying a category, the code had
/// <c>TransactionRegisteredEvent</c> without one, and every subscriber the spec lists (Reports, Rule
/// Engine, Forecasting, Sync, Notifications) is a context with no source files. When those contexts
/// are built, the events get written against that specification rather than resurrected from three
/// records that never matched it.
/// </para>
/// <para>
/// WHY THE TYPE SURVIVES AS AN EMPTY MARKER. It still says something the compiler can check and
/// <c>AggregateRootMarkerTests</c> does check: the four types a repository loads and saves are
/// aggregate roots, and the entities owned inside them — <c>TransactionSplit</c>,
/// <c>TransactionLineItem</c> — are not. Deleting it would erase that distinction from the type
/// system at the moment it finally has a test behind it.
/// </para>
/// </remarks>
public abstract class AggregateRoot : Entity
{
    protected AggregateRoot() : base() { }

    protected AggregateRoot(Guid id) : base(id) { }
}
