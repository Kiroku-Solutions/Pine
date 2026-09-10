using System.Threading;
using System.Threading.Tasks;

namespace Pine.Core.Security;

/// <summary>
/// Replaces the open vault's database with the contents of one of its own backups.
/// </summary>
/// <remarks>
/// <para>
/// THE ASYMMETRY WITH <see cref="IVaultBackupService"/> IS DELIBERATE AND IS THE WHOLE DESIGN.
/// Taking a backup adds a file; nothing the user has can be lost by it, so it validates its inputs
/// and gets on with it. A restore DESTROYS the current ledger by definition — that is what the user
/// asked for — so the order of operations is fixed: prove the backup is openable and understandable,
/// take a copy of what is about to be replaced, and only then replace it. An implementation that
/// reorders those steps is wrong even if every test passes.
/// </para>
/// <para>
/// WHAT A RESTORE IS NOT ALLOWED TO DO: leave the vault in a state that is neither the old contents
/// nor the new ones. Every refusal below happens with the vault untouched, and the copy taken in
/// step two is what makes the one destructive step reversible.
/// </para>
/// </remarks>
public interface IVaultRestoreService
{
    /// <summary>
    /// Replaces <c>&lt;vaultDirectory&gt;\pine.db</c> with the contents of <paramref name="backupPath"/>.
    /// </summary>
    /// <param name="vaultDirectory">Directory holding the vault (the one containing <c>pine.db</c>).</param>
    /// <param name="backupPath">
    /// The backup to restore. MUST be one of this vault's own backups: an implementation is required
    /// to reject a path outside <c>&lt;vaultDirectory&gt;\backups\</c> rather than copy an arbitrary
    /// file over the user's ledger.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancellation token. Honoured only up to the point of no return — once the database file is
    /// being replaced, cancelling would produce exactly the half-restored vault this contract
    /// forbids, so the final step runs to completion.
    /// </param>
    /// <returns>What was restored, and where the replaced contents were kept.</returns>
    /// <exception cref="VaultRestoreRefusedException">
    /// The backup is outside this vault, missing, unreadable with the vault's key, not a Pine vault,
    /// or was written by a newer version of Pine than this one understands. The vault is unchanged.
    /// </exception>
    /// <exception cref="VaultBackupFailedException">
    /// The safety copy of the current vault could not be taken. The vault is unchanged, deliberately:
    /// a restore whose undo does not exist is not offered.
    /// </exception>
    Task<VaultRestoreResult> RestoreAsync(
        string vaultDirectory,
        string backupPath,
        CancellationToken cancellationToken);
}
