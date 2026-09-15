using System;

namespace Pine.Infrastructure.Data;

/// <summary>
/// A table the store keeps for synchronisation's sake, not a thing the user owns. Rows of these types carry
/// no version, no audit columns and no clock of their own, and are never tombstoned.
/// </summary>
public interface ISyncBookkeeping
{
}

/// <summary>
/// The vault's Lamport counter: one row, incremented once per save that writes or deletes a root (TD-144).
/// </summary>
/// <remarks>
/// <para>
/// A COUNTER IN THE DATABASE, TAKEN UNDER THE WRITE LOCK. TD-17 chose a random <see cref="Guid"/> for the
/// row version because a counter read by the application can be read by two writers and incremented to the
/// same number. That does not happen to this one, and the reason is the lock, not the statement: every save
/// that takes a number runs inside a transaction (the caller's, or one <c>PineDbContext</c> opens), and
/// Microsoft.Data.Sqlite begins transactions IMMEDIATE, which acquires SQLite's write lock before the counter
/// is read. The single <c>UPDATE … RETURNING</c> statement is defence in depth for a caller that supplies a
/// deferred transaction of its own. Measured, not assumed: the independent review of plan item 3.1 showed a
/// read-then-write pair passed the race test; with every save in an immediate transaction that mutation is
/// equivalent, and the deterministic test that holds one writer's lock while another waits is what guards
/// the property now. The version token stays as it is — it answers "did this row change under me", and this
/// answers "which of two changes came later".
/// </para>
/// <para>
/// WHAT THIS IS NOT YET, AND THE REGISTER SAYS SO (independent review of plan item 3.1). It is the LOCAL half
/// of a Lamport clock: a counter that orders this vault's own changes. The other half — on receiving a
/// change, advancing to <c>max(local, remote) + 1</c> — belongs to the merge, which does not exist, and until
/// it does, clocks from two vaults cannot be compared with each other. Two further limits: restoring an older
/// copy of the vault file puts the counter back, so the next write can reuse a <c>(clock, device)</c> pair a
/// discarded change already carried; and migrating down past this table and up again reseeds it at zero. A
/// per-installation high-water mark beside the device id is the likely remedy, and it is the merge's to build,
/// because the merge is what would be deceived. TD-144 stays open for these.
/// </para>
/// <para>
/// GAPS ARE NOT EXPECTED IN PRACTICE, AND WOULD BE HARMLESS. The increment runs in the save's transaction, so a
/// failed save gives its number back; only order matters to a Lamport clock, never density.
/// </para>
/// </remarks>
public sealed class SyncClockRow : ISyncBookkeeping
{
    /// <summary>The only row's key.</summary>
    public const int SingletonId = 1;

    public int Id { get; set; }

    public long Value { get; set; }
}

/// <summary>
/// That a root was deleted, when, and by whom — and nothing about what it was (TD-145).
/// </summary>
/// <remarks>
/// <para>
/// A SEPARATE RECORD, NOT A HIDDEN ROW. The alternative — an <c>IsDeleted</c> flag and a query filter on every
/// table — keeps every deleted payee, amount and note in the vault indefinitely, readable by anything that
/// forgets the filter. The register named that trade-off: a hard delete is what a privacy-first ledger should
/// mean by the word. So the row is still physically deleted, and what survives is only what a merge needs to
/// tell deletion from absence: the type, the id, the clock and the device.
/// </para>
/// </remarks>
public sealed class SyncTombstone : ISyncBookkeeping
{
    public Guid Id { get; set; }

    /// <summary>The aggregate root's CLR type name, e.g. <c>Transaction</c>.</summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>The deleted root's id.</summary>
    public Guid EntityId { get; set; }

    /// <summary>The Lamport clock of the save that deleted it.</summary>
    public long Clock { get; set; }

    /// <summary>The installation that deleted it; <see cref="Guid.Empty"/> when not known.</summary>
    public Guid DeviceId { get; set; }

    /// <summary>When, by this machine's clock. For people; a merge orders by <see cref="Clock"/>.</summary>
    public DateTime DeletedAtUtc { get; set; }
}
