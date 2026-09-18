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
/// THE OTHER HALF OF THE LAMPORT CLOCK IS THE MERGE'S (post-audit plan item 3.1b). Item 3.1a built the local
/// half: a counter that orders this vault's own changes. <c>VaultMerger</c> adds the receive step — applying a
/// peer's change set first lifts this counter to at least the highest clock in it — so that after a merge
/// the two vaults' clocks can be compared. A restore no longer rewinds it either: the restore records the
/// clock it discards in a floor file beside the database, and every increment starts above that floor (see
/// <c>PineDbContext.IncrementClockSql</c>). One limit remains: migrating down past this table and up again
/// reseeds it at zero, and nothing writes the floor on that path.
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

    /// <summary>
    /// This vault's identity as a sync peer (post-audit plan item 3.1b). <see cref="Guid.Empty"/> until the vault
    /// first exports or applies a change set; then set once, under the write lock, and never changed.
    /// </summary>
    /// <remarks>
    /// A vault, not a device, is what a merge meets on the other side: two vault files on one machine share a
    /// device id and still have separate histories. It lives in the vault because it names the vault. A byte
    /// copy of the file carries the same id and forks its history. A merge between the two is refused (they
    /// claim to be one vault); a third vault that merges with both cannot tell them apart, and that is recorded
    /// as a limit rather than guessed around.
    /// </remarks>
    public Guid VaultId { get; set; }
}

/// <summary>What this vault knows about one peer vault it has merged with (post-audit plan item 3.1b).</summary>
/// <remarks>
/// Written only in the transaction that applied the peer's change set, so a merge that fails leaves it as it
/// was, and the next merge asks for the same changes again — which is safe because applying is idempotent.
/// </remarks>
public sealed class SyncPeer : ISyncBookkeeping
{
    /// <summary>The other vault's <see cref="SyncClockRow.VaultId"/>.</summary>
    public Guid PeerVaultId { get; set; }

    /// <summary>
    /// The highest of the PEER's clock values up to which every change of the peer's has been applied here. The
    /// next change set taken from the peer for this vault starts after it.
    /// </summary>
    /// <remarks>
    /// A local-sequence cursor, compared with the peer's <c>RowLocalClock</c>, not with <c>RowClock</c>: a row the
    /// peer received from a third vault keeps that vault's Lamport stamp, which can be lower than this cursor,
    /// and must still travel on.
    /// </remarks>
    public long LastRemoteClockSeen { get; set; }

    /// <summary>
    /// This vault's own clock when the last merge with the peer committed here. Kept for people and diagnosis; the
    /// merge tells "changed on both sides" from "changed on one" by the peer's own record of this vault, which
    /// travels in the change set as <c>SourceKnowsTargetUpTo</c>.
    /// </summary>
    public long LocalClockAtLastSync { get; set; }
}

/// <summary>
/// A remote change the merge did not apply, because applying it would have discarded something (§4's third layer).
/// </summary>
/// <remarks>
/// Kept in the vault until a person decides. Recorded once per peer, entity and remote clock, so merging the
/// same change set again does not duplicate it.
/// </remarks>
public sealed class SyncConflict : ISyncBookkeeping
{
    public Guid Id { get; set; }

    public Guid PeerVaultId { get; set; }

    /// <summary>The root's table, e.g. <c>Transactions</c>.</summary>
    public string EntityTable { get; set; } = string.Empty;

    /// <summary>The row's id in THIS vault: the remote id translated through <see cref="SyncIdAlias"/> when there is one.</summary>
    public Guid EntityId { get; set; }

    /// <summary>Why it was not applied; a <see cref="Pine.Infrastructure.Sync.SyncConflictKind"/> name.</summary>
    public string Kind { get; set; } = string.Empty;

    public long RemoteClock { get; set; }

    public Guid RemoteDeviceId { get; set; }

    /// <summary>The remote change as it arrived, so resolving it later does not need the other vault.</summary>
    public string RemotePayload { get; set; } = string.Empty;

    public DateTime DetectedAtUtc { get; set; }
}

/// <summary>
/// That a row a peer knows by one id is, here, the row with another (post-audit plan item 3.1b).
/// </summary>
/// <remarks>
/// <para>
/// WRITTEN WHEN A NATURAL KEY MATCHES AND THE IDS DO NOT. A budget created on two devices before they ever synced
/// is one budget with two ids (TD-146). The merge does not insert a duplicate and does not rename either row:
/// renaming a transaction's primary key would have to cascade into its splits, its line items, its attachments
/// and any transfer that links to it, under foreign keys that restrict. Each vault keeps its own id and records
/// the peer's, so the peer's later edits, deletes, and the attachments that point at its id all land on the
/// local row.
/// </para>
/// <para>
/// Per peer, because the same row can have a third id in a third vault.
/// </para>
/// </remarks>
public sealed class SyncIdAlias : ISyncBookkeeping
{
    public Guid PeerVaultId { get; set; }

    /// <summary>The root's table, e.g. <c>Budgets</c>.</summary>
    public string EntityTable { get; set; } = string.Empty;

    /// <summary>The id the peer uses.</summary>
    public Guid RemoteId { get; set; }

    /// <summary>The id this vault uses.</summary>
    public Guid LocalId { get; set; }
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

    /// <summary>The Lamport clock of the save that deleted it — the deleting vault's, when a merge brought the deletion here.</summary>
    public long Clock { get; set; }

    /// <summary>
    /// This vault's clock when the tombstone was written here: equal to <see cref="Clock"/> for a local delete, and
    /// the merge's own number for one a peer sent (item 3.1b). A change set is cut by this, so a deletion received
    /// from one peer travels on to the next.
    /// </summary>
    public long LocalClock { get; set; }

    /// <summary>The installation that deleted it; <see cref="Guid.Empty"/> when not known.</summary>
    public Guid DeviceId { get; set; }

    /// <summary>When, by this machine's clock. For people; a merge orders by <see cref="Clock"/>.</summary>
    public DateTime DeletedAtUtc { get; set; }
}
