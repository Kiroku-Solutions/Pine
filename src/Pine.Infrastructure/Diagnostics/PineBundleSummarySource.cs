using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pine.Core.Diagnostics;
using Pine.Core.Security;
using Pine.Infrastructure.Data;

namespace Pine.Infrastructure.Diagnostics;

/// <summary>
/// Reads the sync and import summaries for the diagnostic bundle (TD-84). Counts and timestamps only:
/// the last sync attempt's own counts, and how many transactions carry the zero-amount-import tag.
/// Any failure yields <see cref="BundleSummariesSnapshot.Unavailable"/> rather than an exception message (TD-23).
/// </summary>
public sealed class PineBundleSummarySource : IBundleSummarySource
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IVaultLockState _vaultLockState;

    /// <summary>Creates the source.</summary>
    public PineBundleSummarySource(IServiceScopeFactory scopeFactory, IVaultLockState vaultLockState)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(vaultLockState);
        _scopeFactory = scopeFactory;
        _vaultLockState = vaultLockState;
    }

    /// <inheritdoc />
    public async Task<BundleSummariesSnapshot> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (!_vaultLockState.IsUnlocked)
        {
            return BundleSummariesSnapshot.Unavailable;
        }

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<PineDbContext>();

            var attempts = await context.SyncRunAuditLog.AsNoTracking().CountAsync(cancellationToken).ConfigureAwait(false);
            var last = await context.SyncRunAuditLog
                .AsNoTracking()
                .OrderByDescending(r => r.RanAtUtc)
                .ThenByDescending(r => r.Id)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            var zero = await context.Transactions
                .AsNoTracking()
                .CountAsync(t => t.IsZeroAmountImport, cancellationToken)
                .ConfigureAwait(false);

            var lastSync = last is null
                ? null
                : new BundleSyncSummary(
                    last.RanAtUtc,
                    last.Outcome,
                    last.FailureReason,
                    last.EnvelopesUp,
                    last.EnvelopesDown,
                    last.ConflictsDetected,
                    last.ConflictsResolved);

            return new BundleSummariesSnapshot(attempts, lastSync, zero);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return BundleSummariesSnapshot.Unavailable;
        }
    }
}
