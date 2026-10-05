using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Pine.Core.Security;
using Pine.Core.Sync;
using Pine.Infrastructure.Data;

namespace Pine.Infrastructure.Sync;

/// <summary>
/// <see cref="ISyncRunner"/> over the open vault: one exchange of sealed envelopes with every peer that has announced
/// itself through a provider under the same sync key (post-audit plan item 3.1f; `14-Synchronization` §5, §8, §9).
/// </summary>
/// <remarks>
/// <para>
/// IN, THEN OUT. Every peer's envelope for this vault is applied first, each in its own transaction through
/// <see cref="VaultMerger.ApplySealedAsync"/>; only then is an envelope cut for each peer through
/// <see cref="VaultMerger.ExportSealedAsync"/>, so what goes out carries everything this run took in. A peer whose
/// envelope is refused is reported and the run goes on: a refusal writes nothing, and the next peer is its own
/// transaction.
/// </para>
/// <para>
/// WHERE AN ENVELOPE STARTS, WITH NO NEW STATE. An envelope for a peer is cut from the clock that peer last said it
/// had reached of this vault's — <see cref="SyncChangeSet.SourceKnowsTargetUpTo"/>, which travels inside the peer's
/// own envelope and is authenticated with it. The provider keeps the peer's newest envelope for this vault, so the
/// acknowledgement is read from there each run: nothing is added to the vault's schema, and nothing can drift out of
/// step with what the peer actually sent. A peer that has never written is acknowledged at
/// <see cref="SyncChangeSet.Never"/> and gets everything. Starting there can only overlap what the peer has, never
/// skip past it, so §5's gap refusal is never provoked by this side — and an overlap is accepted, as a change set is.
/// </para>
/// <para>
/// WHEN NOTHING IS WRITTEN. Not when this vault's counter is no further than the peer has acknowledged (it has
/// everything), and not when the envelope already waiting for the peer is authentic, reaches this vault's counter,
/// and starts no later than the acknowledgement — it is what would be written again. Without that second rule two
/// vaults with nothing new would rewrite the same envelope for each other on every run: an applied envelope lifts the
/// receiver's counter (the Lamport receive step), and the receiver cannot learn that its own envelope was taken until
/// the sender writes again. With it, two runs on each side after the last change, the folder stops changing.
/// </para>
/// <para>
/// §9's QUEUE IS THE VAULT. What has not reached a peer is exactly the rows and tombstones this vault wrote after the
/// peer's acknowledgement; they sit in the encrypted vault, survive a crash with it, and go out the next time the
/// folder can be reached. The application stays usable meanwhile because nothing waits for the folder but this run.
/// No second copy is kept in a separate queue file — which is also why §9's size limit and its online/offline state
/// machine are not built: there is nothing here that grows while the folder is away.
/// </para>
/// <para>
/// SCOPED, like the pairing service: it reads and writes this vault through the request's <see cref="PineDbContext"/>.
/// </para>
/// </remarks>
public sealed class VaultSyncRunner : ISyncRunner
{
    private readonly PineDbContext _context;
    private readonly IVaultDataKeyAccessor _vault;
    private readonly IVaultFileCipher _cipher;

    /// <summary>Creates the runner over the open vault.</summary>
    /// <param name="context">The request's context.</param>
    /// <param name="vault">The open vault, for its directory — where the sealed sync key is kept.</param>
    /// <param name="cipher">The cipher the sync key is sealed under.</param>
    public VaultSyncRunner(PineDbContext context, IVaultDataKeyAccessor vault, IVaultFileCipher cipher)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(cipher);

        _context = context;
        _vault = vault;
        _cipher = cipher;
    }

    /// <inheritdoc />
    public Task<SyncRunReport> RunThroughFolderAsync(string folderPath, CancellationToken cancellationToken)
    {
        FolderSyncProvider provider;
        try
        {
            provider = new FolderSyncProvider(folderPath);
        }
        catch (ArgumentException)
        {
            // Not a folder anyone could mean. The validator says why first; this is the layer's own guard.
            throw new SyncRunRefusedException(SyncRunRefusal.FolderUnavailable);
        }

        return RunAsync(provider, cancellationToken);
    }

    /// <summary>Runs one exchange through <paramref name="provider"/>.</summary>
    /// <exception cref="SyncRunRefusedException">No key, or the provider cannot be reached. Nothing was sent or applied.</exception>
    public async Task<SyncRunReport> RunAsync(ISyncProvider provider, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);

        // The key before the folder: a vault that cannot seal or open anything has no business in it.
        using var key = new VaultSyncKeyStore(_cipher, VaultDirectory()).Load()
                        ?? throw new SyncRunRefusedException(SyncRunRefusal.NoKey);

        if (await provider.HealthAsync(cancellationToken).ConfigureAwait(false) != SyncProviderHealth.Reachable)
        {
            throw new SyncRunRefusedException(SyncRunRefusal.FolderUnavailable);
        }

        // Taking part in a sync is what a vault id is for, so this is where one is made if there is none.
        var self = await VaultMerger.GetOrCreateVaultIdAsync(_context, cancellationToken).ConfigureAwait(false);
        var keyId = Convert.ToHexString(key.KeyId);

        IReadOnlyList<SyncPeerAnnouncement> announced;
        try
        {
            await provider.AnnounceAsync(new SyncPeerAnnouncement(self, keyId), cancellationToken).ConfigureAwait(false);
            announced = await provider.ListPeersAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Reachable a moment ago and not now, or there and read-only: either way, not a folder to sync through.
            throw new SyncRunRefusedException(SyncRunRefusal.FolderUnavailable);
        }

        // Only vaults under this key: another person's vaults may share the folder, and could read nothing of ours.
        var peers = announced
            .Where(peer => peer.VaultId != self && string.Equals(peer.KeyId, keyId, StringComparison.Ordinal))
            .Select(peer => peer.VaultId)
            .Distinct()
            .ToList();

        var problems = new List<SyncRunProblem>();
        var acknowledged = new Dictionary<Guid, long>();
        var applied = 0;
        var rows = 0;
        var conflicts = 0;

        foreach (var peer in peers)
        {
            var incoming = await FetchAsync(provider, new SyncEnvelopeAddress(peer, self), peer, problems, cancellationToken).ConfigureAwait(false);
            if (incoming is null)
            {
                continue;
            }

            acknowledged[peer] = Acknowledgement(incoming, key, self, peer);

            try
            {
                var result = await VaultMerger.ApplySealedAsync(_context, incoming, key, cancellationToken).ConfigureAwait(false);
                applied++;
                rows += result.Inserted + result.Updated + result.Deleted;
                conflicts += result.ConflictsRecorded;
            }
            catch (SyncEnvelopeRefusedException ex) when (ex.Reason == SyncEnvelopeRefusal.Replayed)
            {
                // Already taken, by this run's predecessor: the ordinary state of a folder between two changes.
            }
            catch (SyncEnvelopeRefusedException ex)
            {
                problems.Add(new SyncRunProblem(peer, Classify(ex.Reason)));
            }
            catch (SyncMergeRefusedException)
            {
                problems.Add(new SyncRunProblem(peer, SyncRunProblemKind.MergeRefused));
            }
        }

        var clock = await VaultMerger.ReadClockAsync(_context, cancellationToken).ConfigureAwait(false);
        var left = 0;

        foreach (var peer in peers)
        {
            var since = acknowledged.GetValueOrDefault(peer, SyncChangeSet.Never);
            if (clock <= since)
            {
                continue;
            }

            var address = new SyncEnvelopeAddress(self, peer);
            try
            {
                if (IsAlreadyLeft(await ExistingAsync(provider, address, cancellationToken).ConfigureAwait(false), key, self, peer, clock, since))
                {
                    continue;
                }

                var envelope = await VaultMerger.ExportSealedAsync(_context, peer, since, key, cancellationToken).ConfigureAwait(false);
                await provider.UploadAsync(address, envelope, cancellationToken).ConfigureAwait(false);
                left++;
            }
            catch (SyncEnvelopeTooLargeException)
            {
                // The provider holds the limit (SyncProviderLimits); a peer could not read an envelope this size anyway.
                problems.Add(new SyncRunProblem(peer, SyncRunProblemKind.TooLarge));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                problems.Add(new SyncRunProblem(peer, SyncRunProblemKind.TransferFailed));
            }
        }

        return new SyncRunReport(peers.Count, applied, left, rows, conflicts, problems);
    }

    /// <summary>
    /// How far <paramref name="peer"/> said it had this vault's changes, read from the peer's own envelope. Anything
    /// that does not authenticate as the peer's envelope for this vault acknowledges nothing.
    /// </summary>
    internal static long Acknowledgement(byte[] envelope, SyncKey key, Guid self, Guid peer)
    {
        try
        {
            var changeSet = SyncEnvelope.Open(envelope, key, self);
            return changeSet.SourceVaultId == peer ? changeSet.SourceKnowsTargetUpTo : SyncChangeSet.Never;
        }
        catch (SyncEnvelopeRefusedException)
        {
            return SyncChangeSet.Never;
        }
    }

    /// <summary>
    /// Whether the envelope already waiting for <paramref name="peer"/> is what would be written now: this vault's,
    /// authentic, reaching <paramref name="clock"/>, and starting no later than <paramref name="since"/>.
    /// </summary>
    internal static bool IsAlreadyLeft(byte[]? existing, SyncKey key, Guid self, Guid peer, long clock, long since)
    {
        if (existing is null)
        {
            return false;
        }

        try
        {
            var changeSet = SyncEnvelope.Open(existing, key, peer);
            return changeSet.SourceVaultId == self && changeSet.SourceClock == clock && changeSet.SinceClock <= since;
        }
        catch (SyncEnvelopeRefusedException)
        {
            // Damaged, or not ours: replaced, never trusted.
            return false;
        }
    }

    /// <summary>The <see cref="SyncEnvelopeRefusal"/> a person can act on, in the run's terms.</summary>
    internal static SyncRunProblemKind Classify(SyncEnvelopeRefusal reason) => reason switch
    {
        SyncEnvelopeRefusal.WrongKey => SyncRunProblemKind.WrongKey,
        SyncEnvelopeRefusal.NotAuthentic or SyncEnvelopeRefusal.WrongTarget => SyncRunProblemKind.NotAuthentic,
        SyncEnvelopeRefusal.Gap => SyncRunProblemKind.Gap,
        _ => SyncRunProblemKind.Unreadable,
    };

    /// <summary>The envelope at <paramref name="address"/>, or null when there is none or it could not be taken.</summary>
    private static async Task<byte[]?> FetchAsync(
        ISyncProvider provider, SyncEnvelopeAddress address, Guid peer, List<SyncRunProblem> problems, CancellationToken cancellationToken)
    {
        try
        {
            return await provider.DownloadAsync(address, cancellationToken).ConfigureAwait(false);
        }
        catch (SyncEnvelopeTooLargeException)
        {
            problems.Add(new SyncRunProblem(peer, SyncRunProblemKind.TooLarge));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problems.Add(new SyncRunProblem(peer, SyncRunProblemKind.TransferFailed));
        }

        return null;
    }

    /// <summary>This vault's own waiting envelope for a peer; one too large to read is one to replace.</summary>
    private static async Task<byte[]?> ExistingAsync(ISyncProvider provider, SyncEnvelopeAddress address, CancellationToken cancellationToken)
    {
        try
        {
            return await provider.DownloadAsync(address, cancellationToken).ConfigureAwait(false);
        }
        catch (SyncEnvelopeTooLargeException)
        {
            return null;
        }
    }

    private string VaultDirectory() =>
        _vault is { IsUnlocked: true, DbPath: { Length: > 0 } directory }
            ? directory
            : throw new InvalidOperationException("Cannot sync: the vault is locked.");
}
