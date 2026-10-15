using System;

namespace Pine.Application.Security;

/// <summary>
/// Re-encrypts a CLOSED vault database under a different data key (TD-86 building block).
/// </summary>
/// <remarks>
/// This is the database half of a data-key rotation and nothing more. It does not touch
/// <c>pine.vault.json</c>, <c>sync.key</c>, the categoriser model or the open session; see
/// <see cref="Rekey"/> for what a caller must do around it.
/// </remarks>
public interface IVaultDatabaseRekeyer
{
    /// <summary>
    /// Rekeys <c>pine.db</c> in <paramref name="vaultDirectory"/> from <paramref name="currentKey"/> to
    /// <paramref name="newKey"/>, all or nothing.
    /// </summary>
    /// <remarks>
    /// The work happens on a COPY: the copy is rekeyed, verified (opens and reads under the new key,
    /// is refused under the old key, schema entry count unchanged), and only then swapped over the
    /// original with an atomic replace. Any failure leaves the original file untouched and removes the
    /// copy, so the vault fails closed to "still opens under the current key". No key or password is
    /// ever written to a message or log. The caller must have closed every connection to the database
    /// (an open handle makes the swap fail, which is again a clean refusal), and owns the ordering of
    /// the header rewrite and re-sealing of key-derived files.
    /// </remarks>
    /// <exception cref="ArgumentException">A key is not 32 bytes, or both keys are equal.</exception>
    /// <exception cref="VaultRekeyException">The database is missing, the current key is wrong, or verification failed.</exception>
    void Rekey(string vaultDirectory, byte[] currentKey, byte[] newKey);

    /// <summary>True when <paramref name="key"/> opens and reads <c>pine.db</c> in <paramref name="vaultDirectory"/>. Never throws for a wrong key.</summary>
    bool CanOpen(string vaultDirectory, byte[] key);
}

/// <summary>A rekey was refused or failed; the original database is unchanged.</summary>
public sealed class VaultRekeyException : Exception
{
    public VaultRekeyException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
