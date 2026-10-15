using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pine.Core.Sync;
using Pine.Infrastructure.Data;

namespace Pine.Infrastructure.Sync;

/// <summary>
/// <see cref="ISyncConflictStore"/> over the open vault, through <see cref="VaultMerger"/>'s own write path
/// (post-audit plan item 3.1c).
/// </summary>
/// <remarks>Scoped, like every repository: it shares the request's context, and so its transaction.</remarks>
public sealed class SqliteSyncConflictStore : ISyncConflictStore
{
    private readonly PineDbContext _context;

    /// <summary>Creates the store over the request's context.</summary>
    public SqliteSyncConflictStore(PineDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SyncConflictSummary>> GetOpenAsync(CancellationToken cancellationToken) =>
        VaultMerger.ListOpenConflictsAsync(_context, cancellationToken);

    /// <inheritdoc />
    public Task<SyncConflictSummary?> FindAsync(Guid conflictId, CancellationToken cancellationToken) =>
        VaultMerger.FindConflictAsync(_context, conflictId, cancellationToken);

    /// <inheritdoc />
    public Task ResolveAsync(Guid conflictId, SyncConflictResolution resolution, bool consentGrantConfirmed, CancellationToken cancellationToken) =>
        VaultMerger.ResolveConflictAsync(_context, conflictId, resolution, consentGrantConfirmed, cancellationToken);
}
