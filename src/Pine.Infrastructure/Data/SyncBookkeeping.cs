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
/// A COUNTER IN THE DATABASE, INCREMENTED BY THE DATABASE. TD-17 chose a random <see cref="Guid"/> for the
/// row version because a counter read by the application can be read by two writers and incremented to the
/// same number. That argument is right for a counter the application reads and then writes, and does not
/// apply to this one: <c>UPDATE … SET Value = Value + 1 … RETURNING Value</c> is one statement, and SQLite
/// holds the write lock across it, so two writers always get two numbers. The version token stays as it is —
/// it answers "did this row change under me", and this answers "which of two changes came later".
/// </para>
/// <para>
/// GAPS ARE ALLOWED; REPEATS ARE NOT. A save that increments the counter and then fails leaves a number
/// unused, which a Lamport clock tolerates: only order matters, never density.
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
