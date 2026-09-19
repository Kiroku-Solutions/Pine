using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pine.Infrastructure.Data;

namespace Pine.Infrastructure.Sync;

/// <summary>The merge's export and apply through §5's envelope (post-audit plan item 3.1d).</summary>
/// <remarks>
/// <para>
/// STILL NO TRANSPORT. These are <see cref="ExportAsync"/> and <see cref="ApplyAsync"/> with the change set sealed on
/// the way out and opened on the way in: the export hands its caller only ciphertext, and the apply accepts only an
/// envelope. Neither writes the change set anywhere; the plaintext exists in memory between the database and the
/// cipher.
/// </para>
/// <para>
/// THE CURSOR RULES, FOR AN ENVELOPE. An in-process merge tolerates a change set that overlaps what the target already
/// has, because it is idempotent and both files are in front of it. An envelope has come from somewhere else, so it is
/// held to more: it is refused as <see cref="SyncEnvelopeRefusal.Replayed"/> unless its source clock is beyond this
/// vault's cursor for its source — an envelope applied already, or one older than what has been applied since, brings
/// nothing and could only raise stale conflicts — and as <see cref="SyncEnvelopeRefusal.Gap"/> if it starts after that
/// cursor. A newer envelope that overlaps is accepted, exactly as a change set is.
/// </para>
/// <para>
/// BEFORE ANY WRITE. The envelope's shape, key, tag and target are checked before a transaction is opened, reading the
/// vault id without creating one; the cursors inside the apply's transaction, before <see cref="ApplyAsync"/>'s own
/// rules run. A refusal there rolls the transaction back.
/// </para>
/// </remarks>
public static partial class VaultMerger
{
    /// <summary>
    /// Everything <paramref name="source"/> has written since <paramref name="sinceClock"/>, cut for
    /// <paramref name="targetVaultId"/> and sealed under <paramref name="key"/>.
    /// </summary>
    /// <returns>The envelope: ciphertext and a header. The plaintext change set is not returned or written.</returns>
    public static async Task<byte[]> ExportSealedAsync(PineDbContext source, Guid targetVaultId, long sinceClock, SyncKey key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        var changeSet = await ExportAsync(source, targetVaultId, sinceClock, cancellationToken).ConfigureAwait(false);
        return SyncEnvelope.Seal(changeSet, key, source.Clock.GetUtcNow());
    }

    /// <summary>Opens <paramref name="envelope"/> with <paramref name="key"/> and applies it, all of it or none of it.</summary>
    /// <exception cref="SyncEnvelopeRefusedException">The envelope was refused; nothing was written.</exception>
    /// <exception cref="SyncMergeRefusedException">The change set inside was refused by the merge; nothing was written.</exception>
    public static async Task<SyncApplyResult> ApplySealedAsync(PineDbContext target, ReadOnlyMemory<byte> envelope, SyncKey key, CancellationToken cancellationToken)
    {
        RefuseBusy(target);
        ArgumentNullException.ThrowIfNull(key);

        _ = SyncEnvelope.ReadHeader(envelope.Span);

        // Opened before any transaction exists: the key, the tag and the target are decided with nothing held and
        // nothing to roll back. The vault id is read, never created — a vault with none is no envelope's target.
        Guid thisVaultId;
        await target.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            thisVaultId = await ReadVaultIdAsync(new Sql(target), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await target.Database.CloseConnectionAsync().ConfigureAwait(false);
        }

        var changeSet = SyncEnvelope.Open(envelope.Span, key, thisVaultId);

        // The cursor rules need the vault's state as the apply will see it, so they run inside its transaction.
        // ApplyCoreAsync checks the target again there.
        await using var transaction = await target.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var sql = new Sql(target);

        var cursor = await ReadPeerCursorAsync(sql, changeSet.SourceVaultId, cancellationToken).ConfigureAwait(false);
        if (changeSet.SourceClock <= cursor)
        {
            throw new SyncEnvelopeRefusedException(
                SyncEnvelopeRefusal.Replayed,
                $"This vault already has the source's changes up to clock {cursor}; the envelope reaches only {changeSet.SourceClock}.");
        }

        if (changeSet.SinceClock > cursor)
        {
            throw new SyncEnvelopeRefusedException(
                SyncEnvelopeRefusal.Gap,
                $"The envelope starts after clock {changeSet.SinceClock}, but this vault has the source's changes only up to {cursor}.");
        }

        var result = await ApplyCoreAsync(sql, changeSet, changeSet.SourceClock, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <summary>This vault's id, or <see cref="Guid.Empty"/> when it has none yet. Writes nothing.</summary>
    private static async Task<Guid> ReadVaultIdAsync(Sql sql, CancellationToken cancellationToken) =>
        await sql.ScalarAsync("SELECT VaultId FROM SyncClock WHERE Id = 1;", cancellationToken).ConfigureAwait(false) is string stored
        && Guid.TryParse(stored, out var id)
            ? id
            : Guid.Empty;
}
