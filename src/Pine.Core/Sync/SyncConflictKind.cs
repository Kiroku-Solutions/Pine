namespace Pine.Core.Sync;

/// <summary>
/// Why a merge left a remote change unapplied and recorded it for a person instead (post-audit plan item 3.1b; moved to Pine.Core in 3.1c so the conflicts screen can name it).
/// </summary>
/// <remarks>
/// Every value is a case where applying the remote change would have discarded something the local vault holds,
/// or would have made a choice `14-Synchronization` §7 reserves for the user. Stored by name in
/// <c>SyncConflicts.Kind</c>, so renaming a value is a schema change in all but name.
/// </remarks>
public enum SyncConflictKind
{
    /// <summary>
    /// Both vaults changed the row since they last merged, in a field class §7 resolves by user prompt:
    /// a transaction's amount, payee, date or splits; a budget's threshold or period; a rule's definition; or
    /// any field the merge has no rule for.
    /// </summary>
    ConcurrentEdit,

    /// <summary>The peer deleted a row this vault changed after the peer last saw it.</summary>
    RemoteDeleteOfLocalEdit,

    /// <summary>The peer changed a row this vault has deleted. The row is not brought back without a person.</summary>
    RemoteEditOfLocalDelete,

    /// <summary>The peer deleted a row that other rows here still point at (an attachment, a transaction).</summary>
    DeleteBlockedByDependents,

    /// <summary>The row points at a row this vault does not have — deleted here, or itself left in conflict.</summary>
    MissingParent,

    /// <summary>The row's natural key matches more than one row here, so which one it is cannot be decided.</summary>
    AmbiguousNaturalKey,

    /// <summary>
    /// Applying the peer's setting would grant a consent this vault has not granted. §7: telemetry consent is never
    /// granted implicitly.
    /// </summary>
    ConsentNotImplicitlyGranted,
}
