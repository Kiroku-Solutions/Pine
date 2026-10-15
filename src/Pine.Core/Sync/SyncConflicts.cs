using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pine.Core.Common;

namespace Pine.Core.Sync;

/// <summary>What a person decided about one recorded sync conflict (post-audit plan item 3.1c).</summary>
public enum SyncConflictResolution
{
    /// <summary>
    /// This vault's side stands. Where this vault holds the row, it is stamped again with this vault's clock so
    /// the decision travels to the peer on the next merge; where it holds a deletion, the deletion is stamped
    /// again. Where it holds neither — the peer's row was never applied here — nothing is written.
    /// </summary>
    KeepLocal,

    /// <summary>
    /// The peer's side is applied here, as the conflict recorded it: its row written, or its deletion applied.
    /// Offered only where that is safe; see <see cref="SyncConflictSummary.TakeRemoteBlockedBy"/>.
    /// </summary>
    TakeRemote,
}

/// <summary>Why taking the peer's side of a conflict is not offered.</summary>
public enum SyncConflictBlock
{
    /// <summary>Nothing: it is offered.</summary>
    None,

    /// <summary>The peer's row points at something this vault does not have, so writing it would break a reference.</summary>
    ParentMissing,

    /// <summary>The peer deleted a row that rows here still point at; deleting it would orphan them.</summary>
    StillPointedAt,

    /// <summary>The peer's row matches more than one row here by natural key; applying it would be a guess.</summary>
    AmbiguousNaturalKey,

    /// <summary>
    /// A row like the peer's already exists here under another id — created since the conflict was recorded —
    /// so writing the peer's would duplicate it (or, for a setting, break its unique key).
    /// </summary>
    AlreadyExistsHere,

    /// <summary>
    /// The peer's side was recorded before this vault's schema changed, and names data this version no longer
    /// holds, so it cannot be written as it is. It is shown; keeping this device's side is still offered
    /// (independent review of item 3.1c).
    /// </summary>
    SchemaChanged,

    /// <summary>
    /// The peer's account is in another currency than this vault's. An account's currency is immutable, so the
    /// two are not versions of one account and the peer's is never written here; only keeping this device's is offered.
    /// </summary>
    CurrencyMismatch,
}

/// <summary>One field of a conflicting row, as each side holds it.</summary>
/// <param name="Name">The stored column, or an owned child table for a difference in children (e.g. <c>TransactionSplits</c>).</param>
/// <param name="LocalValue">
/// This vault's value: a <see cref="string"/>, <see cref="decimal"/> (money), <see cref="DateTimeOffset"/>,
/// <see cref="bool"/>, <see cref="long"/> or <see cref="int"/> (a count of children); null when it has none.
/// </param>
/// <param name="RemoteValue">The peer's value, typed the same way; null when it has none.</param>
public sealed record SyncConflictField(string Name, object? LocalValue, object? RemoteValue);

/// <summary>A recorded sync conflict, read for a person to decide (post-audit plan item 3.1c).</summary>
/// <param name="Id">The conflict.</param>
/// <param name="Kind">Why the merge did not apply the peer's change.</param>
/// <param name="EntityTable">The table of the row it is about, e.g. <c>Transactions</c>.</param>
/// <param name="EntityId">The row's id in this vault.</param>
/// <param name="EntityLabel">What a person calls the row — a payee, an account's name, a setting's key — or null.</param>
/// <param name="PeerVaultId">The vault the change came from.</param>
/// <param name="RemoteDeviceId">The installation that wrote the peer's change.</param>
/// <param name="RemoteClock">The peer's Lamport clock for that change.</param>
/// <param name="DetectedAtUtc">When the merge recorded it, by this machine's clock.</param>
/// <param name="LocalExists">Whether this vault holds the row now.</param>
/// <param name="RemoteIsDeletion">Whether the peer's side is a deletion rather than a row.</param>
/// <param name="Fields">The fields that differ, or every shown field when only one side has a row.</param>
/// <param name="TakeRemoteBlockedBy">Why the peer's side cannot be taken; <see cref="SyncConflictBlock.None"/> when it can.</param>
/// <param name="TakeRemoteGrantsConsent">
/// Taking the peer's side would grant a consent this vault has not granted (`14-Synchronization` §7), so it is
/// refused without an explicit confirmation.
/// </param>
/// <param name="ResolvedAtUtc">When it was resolved; null while it is open.</param>
/// <param name="Resolution">How it was resolved: a <see cref="SyncConflictResolution"/> name, or <c>Superseded</c>.</param>
public sealed record SyncConflictSummary(
    Guid Id,
    SyncConflictKind Kind,
    string EntityTable,
    Guid EntityId,
    string? EntityLabel,
    Guid PeerVaultId,
    Guid RemoteDeviceId,
    long RemoteClock,
    DateTime DetectedAtUtc,
    bool LocalExists,
    bool RemoteIsDeletion,
    IReadOnlyList<SyncConflictField> Fields,
    SyncConflictBlock TakeRemoteBlockedBy,
    bool TakeRemoteGrantsConsent,
    DateTime? ResolvedAtUtc,
    string? Resolution)
{
    /// <summary>What a conflict closed by a later change from the same peer is marked as.</summary>
    public const string Superseded = "Superseded";

    /// <summary>Whether a person has decided it, or a later change from the peer made it moot.</summary>
    public bool IsResolved => ResolvedAtUtc is not null;

    /// <summary>Whether the peer's side is offered.</summary>
    public bool CanTakeRemote => TakeRemoteBlockedBy == SyncConflictBlock.None;
}

/// <summary>
/// The conflicts a merge recorded, and the one safe way to resolve each (post-audit plan item 3.1c).
/// </summary>
/// <remarks>
/// A PORT, because a resolution writes rows the way a merge does — column by column, with the clock stamped — and
/// only the infrastructure that wrote them can. Resolving re-checks everything this reports inside its own
/// transaction; a caller's earlier read is advice, not permission.
/// </remarks>
public interface ISyncConflictStore
{
    /// <summary>Every conflict still waiting for a person, oldest first.</summary>
    Task<IReadOnlyList<SyncConflictSummary>> GetOpenAsync(CancellationToken cancellationToken);

    /// <summary>One conflict, open or resolved; null when there is no such conflict.</summary>
    Task<SyncConflictSummary?> FindAsync(Guid conflictId, CancellationToken cancellationToken);

    /// <summary>Applies a person's decision and marks the conflict resolved, all of it or none of it.</summary>
    /// <param name="conflictId">The conflict.</param>
    /// <param name="resolution">The decision.</param>
    /// <param name="consentGrantConfirmed">
    /// The person was told that taking the peer's side grants a consent, and confirmed it. Required when
    /// <see cref="SyncConflictSummary.TakeRemoteGrantsConsent"/>; ignored otherwise.
    /// </param>
    /// <param name="cancellationToken">Cancels it; nothing is kept.</param>
    /// <exception cref="SyncConflictRefusedException">Not found, already resolved, or the decision is not safe now.</exception>
    Task ResolveAsync(Guid conflictId, SyncConflictResolution resolution, bool consentGrantConfirmed, CancellationToken cancellationToken);
}

/// <summary>Why a resolution was refused.</summary>
public enum SyncConflictRefusal
{
    /// <summary>No conflict has that id.</summary>
    NotFound,

    /// <summary>It was resolved already — by a person, or by a later change from the peer.</summary>
    AlreadyResolved,

    /// <summary>Taking the peer's side would grant a consent, and that was not confirmed.</summary>
    ConsentNotConfirmed,

    /// <summary>Taking the peer's side is not safe now; <see cref="SyncConflictRefusedException.Block"/> says why.</summary>
    TakeRemoteBlocked,
}

/// <summary>A conflict resolution refused before anything was written (post-audit plan item 3.1c).</summary>
public sealed class SyncConflictRefusedException : Exception, IUserFacingFailure
{
    /// <summary>Creates the refusal.</summary>
    /// <param name="message">For the log.</param>
    /// <param name="refusal">Why.</param>
    /// <param name="block">For <see cref="SyncConflictRefusal.TakeRemoteBlocked"/>, what blocks it.</param>
    public SyncConflictRefusedException(string message, SyncConflictRefusal refusal, SyncConflictBlock block = SyncConflictBlock.None)
        : base(message)
    {
        Refusal = refusal;
        Block = block;
        UserFacingMessages = [Describe(refusal, block)];
    }

    /// <summary>The sentence a person reads for a refusal: one key per reason, none carrying a row's data.</summary>
    public static UserFacingMessage Describe(SyncConflictRefusal refusal, SyncConflictBlock block) => refusal switch
    {
        SyncConflictRefusal.NotFound => UserFacingText.SyncConflictNotFound(),
        SyncConflictRefusal.AlreadyResolved => UserFacingText.SyncConflictAlreadyResolved(),
        SyncConflictRefusal.ConsentNotConfirmed => UserFacingText.SyncConflictConsentNotConfirmed(),
        SyncConflictRefusal.TakeRemoteBlocked => DescribeBlock(block),
        _ => throw new ArgumentOutOfRangeException(nameof(refusal), refusal, "A refusal with no sentence."),
    };

    /// <summary>Why the peer's side is not offered, as a person reads it.</summary>
    public static UserFacingMessage DescribeBlock(SyncConflictBlock block) => block switch
    {
        SyncConflictBlock.ParentMissing => UserFacingText.SyncConflictParentMissing(),
        SyncConflictBlock.StillPointedAt => UserFacingText.SyncConflictStillPointedAt(),
        SyncConflictBlock.AmbiguousNaturalKey => UserFacingText.SyncConflictAmbiguous(),
        SyncConflictBlock.AlreadyExistsHere => UserFacingText.SyncConflictAlreadyExistsHere(),
        SyncConflictBlock.SchemaChanged => UserFacingText.SyncConflictSchemaChanged(),
        SyncConflictBlock.CurrencyMismatch => UserFacingText.SyncConflictCurrencyMismatch(),
        _ => throw new ArgumentOutOfRangeException(nameof(block), block, "Nothing blocks it, so there is nothing to say."),
    };

    /// <summary>Why it was refused.</summary>
    public SyncConflictRefusal Refusal { get; }

    /// <summary>What blocks taking the peer's side, when that is why.</summary>
    public SyncConflictBlock Block { get; }

    /// <inheritdoc />
    public IReadOnlyList<UserFacingMessage> UserFacingMessages { get; }
}
