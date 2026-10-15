using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Pine.Core.Security;

namespace Pine.Application.Security.Commands;

/// <summary>
/// Replaces the open vault with the contents of one of its own backups, then locks the vault.
/// </summary>
/// <remarks>
/// <para>
/// DELIBERATELY NOT <c>ITransactionalRequest</c>, for the same reason as
/// <see cref="ExportVaultBackupCommand"/> and then one more. The handler drives a service that runs
/// <c>VACUUM INTO</c> for its safety copy, and SQLite refuses to VACUUM inside a transaction. Beyond
/// that, the operation this command performs is not a database write at all — it replaces the
/// database FILE. There is no transaction that could span that, and a transaction opened around it
/// would be a transaction on a connection to a file that stops existing halfway through.
/// </para>
/// <para>
/// IT LOCKS THE VAULT ON SUCCESS, AND THAT IS THE FEATURE RATHER THAN A LIMITATION. Every
/// <c>PineDbContext</c> in flight was built against the file that has just been replaced, and every
/// cached entity in the UI describes rows that may no longer exist. Locking forces the one path that
/// re-opens the vault from scratch — the unlock screen — so the user sees the restored ledger rather
/// than a screen full of the ledger they just replaced. The alternative, reloading every view model,
/// is a larger surface with a worse failure mode: a stale screen that looks correct.
/// </para>
/// <para>
/// The lock happens AFTER the restore, never before: the restore needs the key that locking zeroes.
/// </para>
/// </remarks>
/// <param name="BackupPath">
/// Absolute path of the backup to restore. It must be one of this vault's own backups; the service
/// refuses anything else rather than copying an arbitrary file over the ledger.
/// </param>
public record RestoreVaultBackupCommand(string BackupPath) : IRequest<VaultRestoreResult>;

public class RestoreVaultBackupCommandHandler : IRequestHandler<RestoreVaultBackupCommand, VaultRestoreResult>
{
    private readonly IVaultRestoreService _restoreService;
    private readonly IVaultSession _session;
    private readonly IPlatformUnlock _platformUnlock;

    public RestoreVaultBackupCommandHandler(
        IVaultRestoreService restoreService,
        IVaultSession session,
        IPlatformUnlock platformUnlock)
    {
        _platformUnlock = platformUnlock;
        _restoreService = restoreService;
        _session = session;
    }

    public async Task<VaultRestoreResult> Handle(
        RestoreVaultBackupCommand request,
        CancellationToken cancellationToken)
    {
        // DbPath is the vault DIRECTORY (it contains pine.db and pine.salt).
        if (!_session.IsUnlocked || string.IsNullOrWhiteSpace(_session.DbPath))
        {
            throw new VaultLockedException("Cannot restore a vault backup while the vault is locked.");
        }

        // TD-87: a restore replaces the ledger the platform wrap was enabled against. Destroyed first so
        // that failing to do so aborts the restore before anything is replaced (fail closed).
        _platformUnlock.InvalidateWrap(_session.DbPath);

        var result = await _restoreService
            .RestoreAsync(_session.DbPath, request.BackupPath, cancellationToken)
            .ConfigureAwait(false);

        // Only on success. A refused restore left the vault untouched and open, and locking it would
        // punish the user with a password prompt for an operation that changed nothing.
        _session.Lock(VaultLockReason.UserRequested);

        return result;
    }
}

/// <summary>
/// Restores a backup taken before the vault's data key was rotated (TD-86), using the password that was in force
/// when that backup was created. The restored database ends up under the CURRENT data key, so the vault opens with
/// the current password afterwards. Raised only after <c>VaultBackupKeyMismatchException</c> from the plain restore.
/// </summary>
/// <remarks>The password never appears in <c>ToString()</c>, so it cannot reach a log through a request dump.</remarks>
public sealed record RestoreVaultBackupWithPasswordCommand(string BackupPath, string BackupPassword) : IRequest<VaultRestoreResult>
{
    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append("BackupPath = ").Append(BackupPath);
        return true;
    }
}

public sealed class RestoreVaultBackupWithPasswordCommandHandler : IRequestHandler<RestoreVaultBackupWithPasswordCommand, VaultRestoreResult>
{
    private readonly IVaultRestoreService _restoreService;
    private readonly IVaultSession _session;
    private readonly IPlatformUnlock _platformUnlock;

    public RestoreVaultBackupWithPasswordCommandHandler(
        IVaultRestoreService restoreService,
        IVaultSession session,
        IPlatformUnlock platformUnlock)
    {
        _restoreService = restoreService;
        _session = session;
        _platformUnlock = platformUnlock;
    }

    /// <exception cref="VaultBackupPasswordIncorrectException">Wrong password for that backup; nothing changed, retry allowed.</exception>
    public async Task<VaultRestoreResult> Handle(
        RestoreVaultBackupWithPasswordCommand request,
        CancellationToken cancellationToken)
    {
        if (!_session.IsUnlocked || string.IsNullOrWhiteSpace(_session.DbPath))
        {
            throw new VaultLockedException("Cannot restore a vault backup while the vault is locked.");
        }

        // Same as the plain restore (TD-87): the platform wrap belongs to the ledger being replaced.
        _platformUnlock.InvalidateWrap(_session.DbPath);

        var result = await _restoreService
            .RestoreWithBackupPasswordAsync(_session.DbPath, request.BackupPath, request.BackupPassword, cancellationToken)
            .ConfigureAwait(false);

        _session.Lock(VaultLockReason.UserRequested);
        return result;
    }
}
