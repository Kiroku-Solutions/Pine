namespace Pine.Core.Security;

/// <summary>
/// Creates and manages encrypted, point-in-time copies of the vault database.
/// Implementations MUST never write the vault contents to disk in plaintext.
/// </summary>
public interface IVaultBackupService
{
    /// <summary>
    /// Produces an encrypted copy of the currently open vault under
    /// <c>&lt;vaultDirectory&gt;\backups\</c>.
    /// </summary>
    /// <param name="vaultDirectory">Directory holding the vault (the one containing <c>pine.db</c>).</param>
    /// <param name="reason">Why the backup is being taken.</param>
    /// <param name="cancellationToken">Cancellation token. On cancellation any partial file is removed.</param>
    /// <exception cref="ArgumentException">The vault directory is empty, or does not contain the open database.</exception>
    /// <exception cref="VaultBackupFailedException">The backup directory or file could not be written.</exception>
    /// <exception cref="OperationCanceledException">The operation was cancelled.</exception>
    Task<VaultBackupInfo> CreateBackupAsync(string vaultDirectory, VaultBackupReason reason, CancellationToken cancellationToken);

    /// <summary>
    /// Lists the backups found in <c>&lt;vaultDirectory&gt;\backups\</c>, newest first.
    /// Returns an empty list when the directory does not exist.
    /// </summary>
    IReadOnlyList<VaultBackupInfo> ListBackups(string vaultDirectory);

    /// <summary>
    /// Deletes all but the <paramref name="keepMostRecent"/> newest backups.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="keepMostRecent"/> is less than 1.</exception>
    /// <exception cref="VaultBackupFailedException">One or more backups could not be deleted.</exception>
    Task PruneAsync(string vaultDirectory, int keepMostRecent, CancellationToken cancellationToken);
}
