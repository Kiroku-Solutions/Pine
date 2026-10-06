using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Pine.Core.Sync;

/// <summary>
/// `14-Synchronization` §8's provider: the place envelopes are left for another vault and picked up from it
/// (post-audit plan item 3.1f). It is handed ciphertext and addresses, never a change set or a key.
/// </summary>
/// <remarks>
/// <para>
/// A PORT, AND THE PROVIDER SEES ONLY WHAT §5 SAYS IT SEES. Every byte array that crosses this interface is an
/// envelope already sealed under the user's sync key; the only things in the clear are the two vault ids in its
/// address and in its header, and the public key id a vault announces itself with. This layer cannot name a
/// <c>SyncKey</c>, so it cannot be handed one by mistake.
/// </para>
/// <para>
/// WHERE THIS DEPARTS FROM §8's TABLE, AND WHY. §8 lists <c>Capabilities</c>, <c>Auth</c>, <c>Upload</c>,
/// <c>Download(sinceCursor, limit)</c>, <c>Delete</c> and <c>Health</c>. The only provider built is a folder
/// (<c>FolderSyncProvider</c>), which has no credentials and no capability worth reporting, so <c>Auth</c> and
/// <c>Capabilities</c> are not here: a method with no honest implementation is not added ahead of a provider that
/// needs it. The cursor is not the provider's either (SY-D-1, deferred, decided here as "unified"): it travels
/// inside the authenticated envelope header, and a provider keeps ONE envelope per (source, target) address — the
/// newest, which supersedes the older because it always starts no later than the peer last said it had reached.
/// So <c>Download</c> takes an address, not a cursor. Two things this port has that §8's table has not: a vault
/// has to be able to find its peers before any envelope can be cut for one, so a vault announces itself and lists
/// the others.
/// </para>
/// <para>
/// §11's RIGHT TO ERASURE, ADDED HERE (post-audit plan item 3.1h). <see cref="ListEnvelopesFromAsync"/> and
/// <see cref="RetractAnnouncementAsync"/> exist only so a vault can find and remove what it itself left on a
/// provider, without first learning every peer that might be out there. Neither reads an envelope's plaintext or
/// its key — a provider is told addresses, nothing it would need the sync key to understand.
/// </para>
/// <para>
/// §13's RULES, AS A PORT CAN STATE THEM. A provider never touches the local store (it is given bytes and returns
/// bytes); it supports deletion (<see cref="DeleteAsync"/>); and it has no plaintext to log.
/// </para>
/// </remarks>
public interface ISyncProvider
{
    /// <summary>Whether the provider can be reached at all right now — a folder that exists, a drive that is plugged in.</summary>
    Task<SyncProviderHealth> HealthAsync(CancellationToken cancellationToken);

    /// <summary>Says that this vault syncs here, under the key whose public id it gives. Idempotent.</summary>
    Task AnnounceAsync(SyncPeerAnnouncement self, CancellationToken cancellationToken);

    /// <summary>Every vault that has announced itself here, under any key. Unauthenticated: see the remarks on
    /// <see cref="SyncPeerAnnouncement"/>.</summary>
    Task<IReadOnlyList<SyncPeerAnnouncement>> ListPeersAsync(CancellationToken cancellationToken);

    /// <summary>Leaves <paramref name="envelope"/> for its target, replacing whatever this address held.</summary>
    Task UploadAsync(SyncEnvelopeAddress address, ReadOnlyMemory<byte> envelope, CancellationToken cancellationToken);

    /// <summary>The envelope this address holds, or null when it holds none.</summary>
    /// <exception cref="SyncEnvelopeTooLargeException">What is there is larger than any envelope may be; it was not read.</exception>
    Task<byte[]?> DownloadAsync(SyncEnvelopeAddress address, CancellationToken cancellationToken);

    /// <summary>Removes the envelope this address holds (§11, §13: the right to erasure). False when it held none.</summary>
    Task<bool> DeleteAsync(SyncEnvelopeAddress address, CancellationToken cancellationToken);

    /// <summary>
    /// Every address <paramref name="sourceVaultId"/> has left an envelope at, as the source (post-audit plan item
    /// 3.1h; §11's right to erasure). Not the envelopes a peer left FOR this vault — those are the peer's own data,
    /// and erasure reaches only what this vault put there.
    /// </summary>
    Task<IReadOnlyList<SyncEnvelopeAddress>> ListEnvelopesFromAsync(Guid sourceVaultId, CancellationToken cancellationToken);

    /// <summary>Removes <paramref name="vaultId"/>'s own announcement (§11, §13). False when it had made none.</summary>
    Task<bool> RetractAnnouncementAsync(Guid vaultId, CancellationToken cancellationToken);
}

/// <summary>Whether a provider can be reached (post-audit plan item 3.1f).</summary>
public enum SyncProviderHealth
{
    /// <summary>It can be read and written.</summary>
    Reachable = 1,

    /// <summary>It is not there: an unplugged drive, a folder that was moved, a share that is offline.</summary>
    Unreachable,
}

/// <summary>Where one envelope goes: from one vault to one other (post-audit plan item 3.1f).</summary>
/// <param name="SourceVaultId">The vault that sealed it.</param>
/// <param name="TargetVaultId">The vault it is sealed for.</param>
public readonly record struct SyncEnvelopeAddress(Guid SourceVaultId, Guid TargetVaultId);

/// <summary>A vault saying that it syncs through a provider, and under which key (post-audit plan item 3.1f).</summary>
/// <param name="VaultId">The vault.</param>
/// <param name="KeyId">The public id of its sync key, as upper-case hexadecimal — the same id every envelope header
/// carries in the clear. Two vaults with different keys cannot read each other, so a vault syncs only with the
/// announcements that name its own key id.</param>
/// <remarks>
/// NOT AUTHENTICATED, AND WHAT THAT COSTS. Anyone who can write to the provider can announce a vault id with a key id
/// copied from any envelope header. What it buys them is envelopes sealed under a key they do not have, addressed to
/// a vault that does not exist: disk space, and nothing readable. It cannot make a vault accept anything, because
/// every envelope a vault applies is authenticated under the sync key.
/// </remarks>
public sealed record SyncPeerAnnouncement(Guid VaultId, string KeyId);

/// <summary>Bounds a provider holds itself to (post-audit plan item 3.1f).</summary>
public static class SyncProviderLimits
{
    /// <summary>
    /// The largest envelope a vault will write or read: 64 MiB. A shared folder is writable by more than Pine, and a
    /// file of any size dropped under an envelope's name must not be read into memory whole before it is refused.
    /// A ledger's change set is rows of text; attachments' contents do not travel.
    /// </summary>
    public const long MaximumEnvelopeBytes = 64L * 1024 * 1024;
}

/// <summary>An envelope's address holds more bytes than any envelope may have; nothing was read (item 3.1f).</summary>
public sealed class SyncEnvelopeTooLargeException : IOException
{
    /// <summary>Creates the exception.</summary>
    public SyncEnvelopeTooLargeException()
        : base($"The envelope is larger than {SyncProviderLimits.MaximumEnvelopeBytes} bytes, so it was not read.")
    {
    }

    /// <summary>Creates the exception.</summary>
    public SyncEnvelopeTooLargeException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception.</summary>
    public SyncEnvelopeTooLargeException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
