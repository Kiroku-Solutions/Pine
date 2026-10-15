using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pine.Core.Sync;
using Pine.Infrastructure.Data;

namespace Pine.Infrastructure.Sync;

/// <summary>
/// <see cref="ISyncAuditLog"/> over the open vault: one row per sync attempt, written and read through the same
/// SQLCipher-encrypted store as everything else here (`14-Synchronization` §12).
/// </summary>
/// <remarks>
/// <para>
/// ITS OWN SAVE, NOT A STEP OF THE RUN'S. <c>RunSyncCommandHandler</c> calls <see cref="RecordAsync"/> after
/// <see cref="VaultMerger"/>'s own transactions have already committed (or after a refusal that wrote nothing),
/// so this is a separate, small <c>SaveChangesAsync</c> and never shares a transaction with an envelope's apply
/// or export — exactly the reason <c>RunSyncCommand</c> is not <c>ITransactionalRequest</c> (see its remarks).
/// </para>
/// <para>SCOPED, like every other repository here: it shares the request's context.</para>
/// </remarks>
public sealed class SqliteSyncAuditLog : ISyncAuditLog
{
    private readonly PineDbContext _context;

    /// <summary>Creates the store over the request's context.</summary>
    public SqliteSyncAuditLog(PineDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <inheritdoc />
    public async Task RecordAsync(
        string provider,
        int envelopesUp,
        int envelopesDown,
        int conflictsDetected,
        int conflictsResolved,
        SyncRunAuditOutcome outcome,
        string? failureReason,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);

        _context.SyncRunAuditLog.Add(new SyncRunAuditLogRow
        {
            Id = Guid.NewGuid(),
            RanAtUtc = _context.Clock.GetUtcNow().UtcDateTime,
            Provider = provider,
            EnvelopesUp = envelopesUp,
            EnvelopesDown = envelopesDown,
            ConflictsDetected = conflictsDetected,
            ConflictsResolved = conflictsResolved,
            Outcome = outcome.ToString(),
            FailureReason = failureReason,
        });

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SyncRunAuditEntry>> GetRecentAsync(int limit, CancellationToken cancellationToken)
    {
        var rows = await _context.SyncRunAuditLog
            .AsNoTracking()
            .OrderByDescending(r => r.RanAtUtc)
            .ThenByDescending(r => r.Id)
            .Take(Math.Max(0, limit))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(ToEntry).ToList();
    }

    private static SyncRunAuditEntry ToEntry(SyncRunAuditLogRow row) => new(
        row.Id,
        row.RanAtUtc,
        row.Provider,
        row.EnvelopesUp,
        row.EnvelopesDown,
        row.ConflictsDetected,
        row.ConflictsResolved,
        Enum.Parse<SyncRunAuditOutcome>(row.Outcome),
        row.FailureReason);
}
