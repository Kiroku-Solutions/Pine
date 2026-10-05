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
        _ => throw new ArgumentOutOfRangeException(nameof(refusal), refusal, "A sync refusal with no sentence."),
    };

    private static string Explain(SyncRunRefusal refusal) => refusal switch
    {
        SyncRunRefusal.NoKey => "This vault holds no sync key, so it can neither seal nor open an envelope.",
        SyncRunRefusal.FolderUnavailable => "The sync folder is not there or cannot be used.",
        _ => "The sync run was refused.",
    };
}
