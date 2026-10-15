using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Pine.Core.Sync;

namespace Pine.Application.Sync.Queries;

/// <summary>
/// Every sync attempt logged so far, newest first, for the Settings screen (`14-Synchronization` §12).
/// </summary>
/// <param name="Limit">The most rows to return. Defaults to 50: enough to show a recent history without the
/// screen paging through a vault that has synced for years.</param>
public sealed record GetSyncAuditLogQuery(int Limit = 50) : IRequest<IReadOnlyList<SyncRunAuditEntry>>;

/// <summary>Reads the audit log from the vault.</summary>
public sealed class GetSyncAuditLogQueryHandler : IRequestHandler<GetSyncAuditLogQuery, IReadOnlyList<SyncRunAuditEntry>>
{
    private readonly ISyncAuditLog _auditLog;

    /// <summary>Creates the handler.</summary>
    /// <param name="auditLog">The vault's own sync audit log.</param>
    public GetSyncAuditLogQueryHandler(ISyncAuditLog auditLog)
    {
        ArgumentNullException.ThrowIfNull(auditLog);
        _auditLog = auditLog;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SyncRunAuditEntry>> Handle(GetSyncAuditLogQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _auditLog.GetRecentAsync(request.Limit, cancellationToken);
    }
}
