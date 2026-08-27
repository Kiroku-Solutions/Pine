using MediatR;
using Pine.Core.Security;

namespace Pine.Application.Security.Commands;

/// <summary>
/// Exports an encrypted, point-in-time copy of the currently unlocked vault.
/// The copy is written to the vault's own backups directory and is encrypted with the same
/// key as the vault, so it is only readable by someone who knows the vault password.
/// </summary>
/// <remarks>
/// DELIBERATELY NOT <c>ITransactionalRequest</c> (TD-01). The handler drives
/// <c>IVaultBackupService</c>, whose only implementation executes SQLite
/// <c>VACUUM INTO $destination</c> on the keyed SQLCipher connection. SQLite REFUSES to run
/// <c>VACUUM</c> from inside an open transaction ("cannot VACUUM from within a transaction"), so
/// marking this command transactional would make every vault backup fail at runtime — it would
/// break the vault backup feature outright. This command also writes nothing to the vault
/// database, so it has no transactional boundary to protect in the first place.
/// </remarks>
public record ExportVaultBackupCommand : IRequest<VaultBackupInfo>;

public class ExportVaultBackupCommandHandler : IRequestHandler<ExportVaultBackupCommand, VaultBackupInfo>
{
    private readonly IVaultBackupService _backupService;
    private readonly IVaultSession _session;

    public ExportVaultBackupCommandHandler(IVaultBackupService backupService, IVaultSession session)
    {
        _backupService = backupService;
        _session = session;
    }

    public async Task<VaultBackupInfo> Handle(ExportVaultBackupCommand request, CancellationToken cancellationToken)
    {
        // DbPath is the vault DIRECTORY (it contains pine.db and pine.salt).
        if (!_session.IsUnlocked || string.IsNullOrWhiteSpace(_session.DbPath))
        {
            throw new VaultLockedException("Cannot export a vault backup while the vault is locked.");
        }

        // Deliberately not pruned: pruning is retention for the automatic pre-migration backups.
        // A backup the user asked for by hand must never be deleted as a side effect of the next one.
        return await _backupService
            .CreateBackupAsync(_session.DbPath, VaultBackupReason.Manual, cancellationToken)
            .ConfigureAwait(false);
    }
}
