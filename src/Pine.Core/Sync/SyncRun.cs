using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pine.Core.Common;

namespace Pine.Core.Sync;

/// <summary>
/// One sync run: this vault takes what its peers left for it in a folder, and leaves them what they have not got
/// (post-audit plan item 3.1f; `14-Synchronization` §5, §8, §9).
/// </summary>
/// <remarks>
/// <para>
/// THE FIRST THING IN THE APPLICATION THAT RUNS A MERGE. Everything under it was already built and is called, not
/// copied: the sealed export and apply (item 3.1d), the merge and its conflicts (3.1b, 3.1c), the key pairing put
/// beside the vault (3.1e).
/// </para>
/// <para>
/// A FOLDER, BECAUSE NOTHING ON A NETWORK MAY BE BUILT HERE. ADR-0012 and <c>NetworkEgressTests</c> hold that Pine
/// makes no network connection, and §8 names no first provider. A folder — a USB drive carried between two
/// machines, a share, a folder another program copies around — is a provider Pine itself never opens a socket for,
/// and it is the channel pairing already uses.
/// </para>
/// <para>
/// A PORT, for the reason <see cref="ISyncPairing"/> is one: the run needs the sync key, which this layer must not
/// learn the shape of. What comes back is counts and reasons, never key material or rows.
/// </para>
/// </remarks>
public interface ISyncRunner
{
    /// <summary>Runs one exchange through the folder at <paramref name="folderPath"/>.</summary>
    /// <param name="folderPath">The folder the vaults share. It must already exist; it is never created.</param>
    /// <param name="cancellationToken">Cancels the run between steps; each apply is all or nothing.</param>
    /// <returns>What the run did, peer by peer where it matters.</returns>
    /// <exception cref="SyncRunRefusedException">The run could not start: no key, or no folder. Nothing was written.</exception>
    Task<SyncRunReport> RunThroughFolderAsync(string folderPath, CancellationToken cancellationToken);

    /// <summary>
    /// Runs one exchange through the folder at <paramref name="folderPath"/>, exactly as the two-argument overload
    /// does for every peer taken in, but, when <paramref name="forceFullResync"/> is set, re-derives and re-sends
    /// EVERY row and tombstone this vault holds to each peer, regardless of what that peer's own acknowledgement
    /// says it already has (`14-Synchronization` §11 — post-audit plan item 3.1h).
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHAT THIS IS FOR. A peer's cursor can lie: restored from an old backup, or a vault whose state looks
    /// inconsistent after some other repair. The ordinary run only ever sends what that peer has not yet
    /// acknowledged, which is exactly wrong when the acknowledgement itself cannot be trusted.
    /// </para>
    /// <para>
    /// WHY THIS CANNOT LOSE DATA. Forcing resync changes only where the export STARTS from — §6's clock, not
    /// what is written. The peer applies the full export through the same idempotent merge every ordinary
    /// envelope goes through (3.1b, 3.1d): a row the peer already has, byte for byte, is matched by content and
    /// counted unchanged; a row it has under a different id is matched by natural key; nothing is duplicated, and
    /// nothing already newer on the peer's side is overwritten, because the merge's own last-writer rules decide
    /// that exactly as they would for a normal envelope. The cost is one-sided redundancy — bandwidth and a larger
    /// envelope — never a second copy of a row or a lost edit.
    /// </para>
    /// <para>
    /// WHAT IT DOES NOT DO. It does not touch what this vault accepts FROM a peer: incoming envelopes are applied
    /// exactly as an ordinary run applies them, cursor and gap and replay rules included. "Force" describes only
    /// this vault's export.
    /// </para>
    /// </remarks>
    /// <param name="folderPath">The folder the vaults share. It must already exist; it is never created.</param>
    /// <param name="forceFullResync">When true, every peer is sent everything this vault holds, not only what it has not acknowledged.</param>
    /// <param name="cancellationToken">Cancels the run between steps; each apply is all or nothing.</param>
    /// <returns>What the run did, peer by peer where it matters.</returns>
    /// <exception cref="SyncRunRefusedException">The run could not start: no key, or no folder. Nothing was written.</exception>
    Task<SyncRunReport> RunThroughFolderAsync(string folderPath, bool forceFullResync, CancellationToken cancellationToken);

    /// <summary>
    /// Removes every envelope this vault has left for a peer in the folder at <paramref name="folderPath"/>, and
    /// this vault's own announcement (`14-Synchronization` §11, §13: the right to erasure; post-audit plan item 3.1h).
    /// </summary>
    /// <remarks>
    /// REACHES ONLY WHAT THIS VAULT PUT THERE. An envelope a peer left FOR this vault is the peer's own export of
    /// its own data; erasing it would delete someone else's data without their say, so it is left alone. What a
    /// peer has already taken and merged into its own vault is also untouched — this is erasure from the shared
    /// folder, not a recall of rows that already crossed into another vault's database, which no provider could do.
    /// </remarks>
    /// <param name="folderPath">The folder the vaults share. It must already exist; it is never created.</param>
    /// <param name="cancellationToken">Cancels between deletions; each one is independent, so a cancellation after some have gone leaves the rest.</param>
    /// <returns>How many envelopes were removed, and whether this vault's announcement was one of them.</returns>
    /// <exception cref="SyncRunRefusedException">The folder cannot be used. Nothing was deleted.</exception>
    Task<SyncEraseReport> EraseFromFolderAsync(string folderPath, CancellationToken cancellationToken);
}

/// <summary>What one erasure did (post-audit plan item 3.1h; `14-Synchronization` §11's right to erasure).</summary>
/// <param name="EnvelopesDeleted">Envelopes this vault had left for a peer, removed from the folder.</param>
/// <param name="AnnouncementRemoved">Whether this vault's own announcement was there to remove.</param>
public sealed record SyncEraseReport(int EnvelopesDeleted, bool AnnouncementRemoved);

/// <summary>
/// Keys into the generic vault-scoped setting store (<c>GetSettingQuery</c>/<c>SaveSettingCommand</c>) that sync's
/// user controls use (`14-Synchronization` §11; post-audit plan item 3.1h). No dedicated table: a boolean flag is
/// exactly what that store already exists for, and the generic mechanism is reused rather than a new one invented.
/// </summary>
public static class SyncSettingKeys
{
    /// <summary>
    /// Whether this vault is willing to sync at all. The value is the literal string <c>"false"</c> to disable;
    /// anything else, including the key being absent, means enabled — a vault that has never touched this setting
    /// has never been told sync is off.
    /// </summary>
    public const string Enabled = "Sync.Enabled";
}

/// <summary>What one sync run did (post-audit plan item 3.1f; the counts `14-Synchronization` §12 asks for).</summary>
/// <param name="PeersFound">Other vaults in the folder that hold the same sync key.</param>
/// <param name="EnvelopesApplied">Envelopes taken into this vault.</param>
/// <param name="EnvelopesLeft">Envelopes written to the folder for a peer.</param>
/// <param name="RowsChanged">Rows inserted, updated or deleted here by what was applied.</param>
/// <param name="ConflictsRecorded">Changes left for a person on the Sync conflicts page.</param>
/// <param name="Problems">Every envelope that was not applied or not left, and why. Empty when none.</param>
public sealed record SyncRunReport(
    int PeersFound,
    int EnvelopesApplied,
    int EnvelopesLeft,
    int RowsChanged,
    int ConflictsRecorded,
    IReadOnlyList<SyncRunProblem> Problems);

/// <summary>One envelope, from or for one peer, that did not go through (post-audit plan item 3.1f).</summary>
/// <param name="PeerVaultId">The other vault.</param>
/// <param name="Kind">Why.</param>
public sealed record SyncRunProblem(Guid PeerVaultId, SyncRunProblemKind Kind);

/// <summary>
/// Why one peer's envelope did not go through. None of them stops the run: each peer is its own transaction, and a
/// refusal writes nothing.
/// </summary>
public enum SyncRunProblemKind
{
    /// <summary>The file under that peer's name is not an envelope this build reads, or is not one at all.</summary>
    Unreadable = 1,

    /// <summary>It was sealed under a different sync key: the two vaults were not paired with each other.</summary>
    WrongKey,

    /// <summary>It does not authenticate: it was altered, damaged in transit, or not sealed for this vault.</summary>
    NotAuthentic,

    /// <summary>
    /// It starts after what this vault has from that peer (this vault was restored from an older copy, say). It is
    /// left where it is; the peer's next run writes one this vault can take.
    /// </summary>
    Gap,

    /// <summary>The change set inside was refused by the merge — most often, the two vaults run different builds.</summary>
    MergeRefused,

    /// <summary>It is larger than any envelope may be, so it was not read — or this vault's would be, so it was not written.</summary>
    TooLarge,

    /// <summary>The folder could not be read or written while this peer's envelope was being moved.</summary>
    TransferFailed,
}

/// <summary>Why a sync run could not start at all (post-audit plan item 3.1f).</summary>
public enum SyncRunRefusal
{
    /// <summary>This vault holds no sync key: it has not been paired, and has made no key of its own.</summary>
    NoKey = 1,

    /// <summary>The folder is not there or cannot be used: a drive unplugged, a share offline. §9's offline state.</summary>
    FolderUnavailable,

    /// <summary>
    /// The user has turned sync off for this vault (§11's "enable or disable sync" — post-audit plan item 3.1h).
    /// Checked before anything else: a vault that declined to sync has no business touching the folder at all.
    /// </summary>
    Disabled,
}

/// <summary>
/// A sync run that did not start (post-audit plan item 3.1f). Nothing was written, in the vault or the folder.
/// </summary>
/// <remarks>THE MESSAGE NEVER QUOTES THE PATH (TD-24).</remarks>
public sealed class SyncRunRefusedException : Exception, IUserFacingFailure
{
    /// <summary>Creates the refusal.</summary>
    /// <param name="refusal">Why.</param>
    public SyncRunRefusedException(SyncRunRefusal refusal)
        : base(Explain(refusal))
    {
        Refusal = refusal;
        UserFacingMessages = [Describe(refusal)];
    }

    /// <summary>Creates the refusal. Present for the analyser's constructor rule; prefer the typed one.</summary>
    public SyncRunRefusedException()
        : this(SyncRunRefusal.FolderUnavailable)
    {
    }

    /// <summary>Creates the refusal. Present for the analyser's constructor rule; prefer the typed one.</summary>
    public SyncRunRefusedException(string message)
        : base(message)
    {
        Refusal = SyncRunRefusal.FolderUnavailable;
        UserFacingMessages = [Describe(SyncRunRefusal.FolderUnavailable)];
    }

    /// <summary>Creates the refusal. Present for the analyser's constructor rule; prefer the typed one.</summary>
    public SyncRunRefusedException(string message, Exception innerException)
        : base(message, innerException)
    {
        Refusal = SyncRunRefusal.FolderUnavailable;
        UserFacingMessages = [Describe(SyncRunRefusal.FolderUnavailable)];
    }

    /// <summary>Why the run did not start.</summary>
    public SyncRunRefusal Refusal { get; }

    /// <inheritdoc />
    public IReadOnlyList<UserFacingMessage> UserFacingMessages { get; } = [];

    /// <summary>The sentence a person reads: one key per reason.</summary>
    public static UserFacingMessage Describe(SyncRunRefusal refusal) => refusal switch
    {
        SyncRunRefusal.NoKey => UserFacingText.SyncRunNoKey(),
        SyncRunRefusal.FolderUnavailable => UserFacingText.SyncRunFolderUnavailable(),
        SyncRunRefusal.Disabled => UserFacingText.SyncRunDisabled(),
        _ => throw new ArgumentOutOfRangeException(nameof(refusal), refusal, "A sync refusal with no sentence."),
    };

    private static string Explain(SyncRunRefusal refusal) => refusal switch
    {
        SyncRunRefusal.NoKey => "This vault holds no sync key, so it can neither seal nor open an envelope.",
        SyncRunRefusal.FolderUnavailable => "The sync folder is not there or cannot be used.",
        SyncRunRefusal.Disabled => "The user has turned sync off for this vault.",
        _ => "The sync run was refused.",
    };
}
