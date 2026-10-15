using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Pine.Core.Security;

namespace Pine.Application.Security.Queries;

/// <summary>
/// Lists the backups of the open vault, newest first.
/// </summary>
/// <remarks>
/// <para>
/// WHY A QUERY RATHER THAN THE VIEW MODEL CALLING <c>IVaultBackupService</c>. That interface lives in
/// <c>Pine.Core</c> and its only implementation lives in <c>Pine.Infrastructure</c>, which the UI
/// project does not and must not reference — the architecture tests enforce it. More usefully, the
/// service takes a vault directory and the UI has no business knowing one: the session does. This
/// query is the seam where "which vault" is answered.
/// </para>
/// <para>
/// <see cref="VaultBackupInfo.SchemaVersionBefore"/> IS NULL FOR EVERY ROW THIS RETURNS, and that is
/// a property of listing rather than a defect here: recovering it would mean opening each backup with
/// the vault key, which enumerating files should not need to do. The UI must not present a blank as
/// though it were a fact about the backup.
/// </para>
/// </remarks>
public record ListVaultBackupsQuery : IRequest<IReadOnlyList<VaultBackupInfo>>;

public class ListVaultBackupsQueryHandler
    : IRequestHandler<ListVaultBackupsQuery, IReadOnlyList<VaultBackupInfo>>
{
    private readonly IVaultBackupService _backupService;
    private readonly IVaultSession _session;

    public ListVaultBackupsQueryHandler(IVaultBackupService backupService, IVaultSession session)
    {
        _backupService = backupService;
        _session = session;
    }

    public Task<IReadOnlyList<VaultBackupInfo>> Handle(
        ListVaultBackupsQuery request,
        CancellationToken cancellationToken)
    {
        if (!_session.IsUnlocked || string.IsNullOrWhiteSpace(_session.DbPath))
        {
            throw new VaultLockedException("Cannot list vault backups while the vault is locked.");
        }

        return Task.FromResult(_backupService.ListBackups(_session.DbPath));
    }
}
