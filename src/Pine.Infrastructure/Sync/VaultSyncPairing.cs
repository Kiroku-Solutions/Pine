using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Pine.Core.Security;
using Pine.Core.Sync;
using Pine.Infrastructure.Data;

namespace Pine.Infrastructure.Sync;

/// <summary>
/// <see cref="ISyncPairing"/> over the open vault: the offer file, the code, and the decision whether this vault may
/// keep the key it was given (post-audit plan item 3.1e; TD-154).
/// </summary>
/// <remarks>
/// <para>
/// TWO ENFORCEMENT POINTS, AND ONLY ONE OF THEM IS CRYPTOGRAPHY. <see cref="SyncPairingOffer"/> decides whether an
/// offer opens; this class decides whether the key inside it may be kept — an offer already used here, or a vault
/// that already holds a different key — and it is the one that touches the disk.
/// </para>
/// <para>
/// ONE SUCCESSFUL USE, AND HOW FAR THAT CLAIM GOES. On success the offer file is deleted, so the ordinary second
/// attempt finds nothing (<see cref="SyncPairingRefusal.OfferNotFound"/>); and a COPY of the offer replayed against
/// this vault is refused as <see cref="SyncPairingRefusal.Replayed"/>, because the key it carries is the key this
/// vault now holds. What neither of those stops is the same offer, copied before it was used, pairing a THIRD vault
/// inside its lifetime — there is no shared state between two vault files to record it in, and the offer's expiry is
/// what bounds that window. Said here rather than covered over: it is the same window in which the code itself is
/// live, and the user is told to keep the code to the two devices in front of them.
/// </para>
/// <para>
/// NOTHING IS WRITTEN BY A REFUSAL. The key is saved first, and only a save that succeeded deletes the offer; a
/// vault holding another key is refused by <see cref="VaultSyncKeyStore"/> before that, and reported as
/// <see cref="SyncPairingRefusal.DifferentKey"/> rather than as a crash.
/// </para>
/// <para>
/// SCOPED, like the conflict store, because it reads the open vault's id through the request's
/// <see cref="PineDbContext"/>.
/// </para>
/// </remarks>
public sealed class VaultSyncPairing : ISyncPairing
{
    /// <summary>What a pairing offer is called when nothing else says.</summary>
    public const string DefaultOfferFileName = "pine.pairing-offer";

    private readonly PineDbContext _context;
    private readonly IVaultDataKeyAccessor _vault;
    private readonly IVaultFileCipher _cipher;
    private readonly TimeProvider _clock;

    /// <summary>Creates the pairing service over the open vault.</summary>
    /// <param name="context">The request's context, for this vault's id.</param>
    /// <param name="vault">The open vault, for its directory.</param>
    /// <param name="cipher">The cipher the key is sealed under when it is kept.</param>
    /// <param name="clock">For the offer's life.</param>
    public VaultSyncPairing(PineDbContext context, IVaultDataKeyAccessor vault, IVaultFileCipher cipher, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(cipher);
        ArgumentNullException.ThrowIfNull(clock);

        _context = context;
        _vault = vault;
        _cipher = cipher;
        _clock = clock;
    }

    /// <summary>Where an offer goes when the caller does not say: beside the vault it came from.</summary>
    public string DefaultOfferPath => Path.Combine(VaultDirectory(), DefaultOfferFileName);

    /// <inheritdoc />
    public Task<SyncPairingStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var key = Store().Load();
        return Task.FromResult(new SyncPairingStatus(key is not null, key?.Fingerprint ?? string.Empty, DefaultOfferPath));
    }

    /// <inheritdoc />
    public async Task<SyncPairingOfferIssued> CreateOfferAsync(string offerFilePath, TimeSpan lifetime, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(offerFilePath);

        // The offering vault is the one that HAS the key, so creating both here is right: a vault that has never
        // been merged has neither an id nor a key until something asks it to be one end of a sync.
        var vaultId = await VaultMerger.GetOrCreateVaultIdAsync(_context, cancellationToken).ConfigureAwait(false);
        using var key = Store().GetOrCreate();

        var code = SyncPairingCode.Generate();
        var now = _clock.GetUtcNow();
        var offer = SyncPairingOffer.Create(key, vaultId, code, now, lifetime);

        WriteReplacing(offerFilePath, offer);

        return new SyncPairingOfferIssued(code, key.Fingerprint, now + lifetime, offerFilePath);
    }

    /// <inheritdoc />
    public async Task<SyncPairingCompleted> AcceptOfferAsync(string offerFilePath, string code, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(offerFilePath);

        byte[] offer;
        try
        {
            offer = await File.ReadAllBytesAsync(offerFilePath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // A path that is not there, not readable, or not a path: one reason, and nothing about the file system
            // in the sentence the user reads.
            throw new SyncPairingRefusedException(SyncPairingRefusal.OfferNotFound);
        }

        // Read, never created: giving a vault an id is not something accepting a refused offer should do.
        var thisVaultId = await VaultMerger.ReadVaultIdAsync(_context, cancellationToken).ConfigureAwait(false);

        using var key = SyncPairingOffer.Open(offer, code, thisVaultId, _clock.GetUtcNow());

        var store = Store();
        using (var existing = store.Load())
        {
            if (existing is not null)
            {
                throw new SyncPairingRefusedException(
                    existing.HasId(key.KeyId) ? SyncPairingRefusal.Replayed : SyncPairingRefusal.DifferentKey);
            }
        }

        try
        {
            store.Save(key);
        }
        catch (SyncKeyConflictException)
        {
            // Another writer got there between the read above and this save. Same fact, same refusal.
            throw new SyncPairingRefusedException(SyncPairingRefusal.DifferentKey);
        }

        // Only now: an offer that paired this vault has been used, and the file is what a second use would need.
        // Best effort — a file that will not delete is not a reason to unpair a vault that is paired.
        try
        {
            File.Delete(offerFilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The replay check above is what still refuses this offer here.
        }

        return new SyncPairingCompleted(key.Fingerprint);
    }

    private VaultSyncKeyStore Store() => new(_cipher, VaultDirectory());

    private string VaultDirectory() =>
        _vault is { IsUnlocked: true, DbPath: { Length: > 0 } directory }
            ? directory
            : throw new InvalidOperationException("Cannot pair: the vault is locked.");

    /// <summary>Writes an offer, replacing any offer already there: a new code retires the old one.</summary>
    private static void WriteReplacing(string path, byte[] offer)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            stream.Write(offer, 0, offer.Length);
            stream.Flush(flushToDisk: true);
        }

        File.Move(temporary, path, overwrite: true);
    }
}
