using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Pine.Core.Sync;

namespace Pine.Application.Sync.Queries;

/// <summary>
/// Every sync conflict still waiting for a person, oldest first (post-audit plan item 3.1c).
/// </summary>
/// <remarks>
/// <para>
/// A MERGE THAT RECORDS CONFLICTS NOBODY CAN SEE SILENTLY KEEPS THE LOCAL SIDE. That is why this screen came before
/// the envelope and the transport (plan §9): item 3.1b recorded every change it would not apply in
/// <c>SyncConflicts</c>, and until this query nothing read them.
/// </para>
/// <para>
/// WHAT NOTHING PRODUCES YET. No command in the application runs a merge; the only merges are the ones the tests
/// run between two vault files. On a real vault this returns an empty list, and the screen says so in a sentence.
/// </para>
/// </remarks>
public sealed record GetSyncConflictsQuery : IRequest<IReadOnlyList<SyncConflictSummary>>;

/// <summary>Reads the open conflicts from the vault.</summary>
public sealed class GetSyncConflictsQueryHandler : IRequestHandler<GetSyncConflictsQuery, IReadOnlyList<SyncConflictSummary>>
{
    private readonly ISyncConflictStore _conflicts;

    /// <summary>Creates the handler.</summary>
    /// <param name="conflicts">The vault's recorded conflicts.</param>
    public GetSyncConflictsQueryHandler(ISyncConflictStore conflicts)
    {
        ArgumentNullException.ThrowIfNull(conflicts);
        _conflicts = conflicts;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SyncConflictSummary>> Handle(GetSyncConflictsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _conflicts.GetOpenAsync(cancellationToken);
    }
}
