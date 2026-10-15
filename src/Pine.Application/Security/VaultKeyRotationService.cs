using System;
using System.Security.Cryptography;
using Pine.Core.Security;

namespace Pine.Application.Security;

/// <summary>What a write-ahead rekey leaves in the vault folder until it has fully completed (TD-86).</summary>
/// <param name="NewHeader">The header the vault will have afterwards: the NEW data key under the NEW password.</param>
/// <param name="NewKeyUnderCurrentPassword">The NEW data key under the OLD password, so the old password can still reach it.</param>
/// <param name="CurrentKeyUnderNewPassword">The OLD data key under the NEW password, so the new password can still reach it.</param>
public sealed record VaultRekeyJournalEntry(
    VaultHeader NewHeader,
    VaultKeyWrap NewKeyUnderCurrentPassword,
    VaultKeyWrap CurrentKeyUnderNewPassword);

/// <summary>The sidecar journal file. Writes are atomic and flushed; it never holds a plaintext key.</summary>
public interface IVaultRekeyJournal
{
    bool Exists(string vaultDirectory);

    void Write(string vaultDirectory, VaultRekeyJournalEntry entry);

    /// <summary>The entry, or <c>null</c> when absent or unreadable.</summary>
    VaultRekeyJournalEntry? TryRead(string vaultDirectory);

    void Delete(string vaultDirectory);
}

/// <summary>Re-seals the files that are encrypted under keys derived from the data key (sync.key, the categoriser model).</summary>
public interface IVaultSealedFileResealer
{
    /// <summary>Idempotent: a file already sealed under <paramref name="newKey"/> is left alone; an unreadable derived cache is dropped.</summary>
    void Reseal(string vaultDirectory, byte[] oldKey, byte[] newKey);
}

/// <summary>Recovery found a state it cannot resolve; the vault was left as found (or rolled back).</summary>
public sealed class VaultRekeyRecoveryException : Exception
{
    public VaultRekeyRecoveryException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

/// <summary>Crash-safe data-key rotation (TD-86).</summary>
public interface IVaultKeyRotationService
{
    /// <summary>Changes the password AND rotates the data key. Returns the NEW data key (caller zeroes it) for the session swap.</summary>
    byte[] ChangePassword(string vaultDirectory, string currentPassword, string newPassword);

    bool HasPendingRekey(string vaultDirectory);

    /// <summary>
    /// Resolves a leftover journal and returns the data key the vault now opens with. Always rolls FORWARD
    /// to the new key (both keys are reachable from either password), so the outcome is the same whichever
    /// step a crash interrupted. Accepts the old or the new password.
    /// </summary>
    /// <exception cref="VaultKeyUnwrapException">The password matches neither.</exception>
    /// <exception cref="VaultRekeyRecoveryException">The database opens under neither key, or could not be completed and was rolled back.</exception>
    byte[] RecoverAndUnwrap(string vaultDirectory, string password);
}

/// <summary>
/// ORDER (each step survives a crash after it):
/// 1 journal written (atomic)            -> db old key, header old: recovery finishes the rekey.
/// 2 database rekeyed (atomic swap)      -> db new key, header old: recovery finishes.
/// 3 header rewritten (atomic) = COMMIT  -> the password is now the new one: recovery re-seals files.
/// 4 sealed files re-sealed (idempotent)
/// 5 journal deleted = old key material gone.
/// </summary>
public sealed class VaultKeyRotationService : IVaultKeyRotationService
{
    private readonly IVaultHeaderStore _headerStore;
    private readonly IVaultKeyService _keyService;
    private readonly IVaultDatabaseRekeyer _rekeyer;
    private readonly IVaultRekeyJournal _journal;
    private readonly IVaultSealedFileResealer _resealer;

    public VaultKeyRotationService(
        IVaultHeaderStore headerStore,
        IVaultKeyService keyService,
        IVaultDatabaseRekeyer rekeyer,
        IVaultRekeyJournal journal,
        IVaultSealedFileResealer resealer)
    {
        _headerStore = headerStore;
        _keyService = keyService;
        _rekeyer = rekeyer;
        _journal = journal;
        _resealer = resealer;
    }

    public bool HasPendingRekey(string vaultDirectory) => _journal.Exists(vaultDirectory);

    public byte[] ChangePassword(string vaultDirectory, string currentPassword, string newPassword)
    {
        if (_journal.Exists(vaultDirectory))
        {
            // A leftover journal must be resolved before a new rotation may start.
            CryptographicOperations.ZeroMemory(RecoverAndUnwrap(vaultDirectory, currentPassword));
        }

        var header = _headerStore.Read(vaultDirectory);
        var rotation = _keyService.BeginDataKeyRotation(currentPassword, newPassword, header);
        try
        {
            _journal.Write(
                vaultDirectory,
                new VaultRekeyJournalEntry(rotation.NewHeader, rotation.NewKeyUnderCurrentPassword, rotation.CurrentKeyUnderNewPassword));

            try
            {
                _rekeyer.Rekey(vaultDirectory, rotation.OldDataKey, rotation.NewDataKey);
            }
            catch (Exception)
            {
                // The rekeyer is all-or-nothing, but only the database can say which side of the swap we are on.
                if (_rekeyer.CanOpen(vaultDirectory, rotation.OldDataKey))
                {
                    _journal.Delete(vaultDirectory); // nothing changed: roll back cleanly
                    throw;
                }

                if (!_rekeyer.CanOpen(vaultDirectory, rotation.NewDataKey))
                {
                    throw; // unknowable here: leave the journal, recovery at unlock decides
                }

                // The swap happened, so carry on forward.
            }

            FinishForward(vaultDirectory, rotation.NewHeader, rotation.OldDataKey, rotation.NewDataKey);
            return (byte[])rotation.NewDataKey.Clone();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(rotation.OldDataKey);
            CryptographicOperations.ZeroMemory(rotation.NewDataKey);
        }
    }

    public byte[] RecoverAndUnwrap(string vaultDirectory, string password)
    {
        var header = _headerStore.Read(vaultDirectory);
        var entry = _journal.TryRead(vaultDirectory);

        if (entry is null)
        {
            // Unreadable journal: it is written whole or not at all BEFORE anything destructive, so an
            // unreadable one means the rekey never began. Prove that against the database before dropping it.
            var onlyKey = _keyService.UnwrapDataKey(password, header);
            if (!_rekeyer.CanOpen(vaultDirectory, onlyKey))
            {
                CryptographicOperations.ZeroMemory(onlyKey);
                throw new VaultRekeyRecoveryException("The rekey journal is unreadable and the database does not open with the header key.");
            }

            _journal.Delete(vaultDirectory);
            return onlyKey;
        }

        var headerIsNew = SameWrap(header, entry.NewHeader);
        byte[] oldKey;
        byte[] newKey;
        if (headerIsNew || !TryResolveWithOldPassword(password, header, entry, out oldKey, out newKey))
        {
            if (!TryResolveWithNewPassword(password, entry, out oldKey, out newKey))
            {
                throw new VaultKeyUnwrapException();
            }
        }

        try
        {
            if (_rekeyer.CanOpen(vaultDirectory, oldKey))
            {
                try
                {
                    _rekeyer.Rekey(vaultDirectory, oldKey, newKey);
                }
                catch (Exception ex) when (_rekeyer.CanOpen(vaultDirectory, oldKey))
                {
                    // Could not complete (e.g. file locked) and nothing changed: the old password still
                    // works. Drop the journal only if the header is still the old one.
                    if (!headerIsNew)
                    {
                        _journal.Delete(vaultDirectory);
                    }

                    throw new VaultRekeyRecoveryException("The interrupted password change could not be completed and was rolled back; use your previous password.", ex);
                }
            }
            else if (!_rekeyer.CanOpen(vaultDirectory, newKey))
            {
                throw new VaultRekeyRecoveryException("The database opens under neither key of the interrupted password change.");
            }

            FinishForward(vaultDirectory, entry.NewHeader, oldKey, newKey);
            return (byte[])newKey.Clone();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(oldKey);
            CryptographicOperations.ZeroMemory(newKey);
        }
    }

    private void FinishForward(string vaultDirectory, VaultHeader newHeader, byte[] oldKey, byte[] newKey)
    {
        if (!SameWrap(_headerStore.Read(vaultDirectory), newHeader))
        {
            _headerStore.Write(vaultDirectory, newHeader); // the commit point
        }

        _resealer.Reseal(vaultDirectory, oldKey, newKey);
        _journal.Delete(vaultDirectory); // old key material ends here
    }

    private bool TryResolveWithOldPassword(string password, VaultHeader header, VaultRekeyJournalEntry entry, out byte[] oldKey, out byte[] newKey)
    {
        oldKey = newKey = Array.Empty<byte>();
        try
        {
            oldKey = _keyService.UnwrapDataKey(password, header);
        }
        catch (VaultKeyUnwrapException)
        {
            return false;
        }

        try
        {
            newKey = _keyService.UnwrapRotationKey(password, header, entry.NewKeyUnderCurrentPassword);
            return true;
        }
        catch (VaultKeyUnwrapException)
        {
            CryptographicOperations.ZeroMemory(oldKey);
            return false;
        }
    }

    private bool TryResolveWithNewPassword(string password, VaultRekeyJournalEntry entry, out byte[] oldKey, out byte[] newKey)
    {
        oldKey = newKey = Array.Empty<byte>();
        try
        {
            newKey = _keyService.UnwrapDataKey(password, entry.NewHeader);
            oldKey = _keyService.UnwrapRotationKey(password, entry.NewHeader, entry.CurrentKeyUnderNewPassword);
            return true;
        }
        catch (VaultKeyUnwrapException)
        {
            CryptographicOperations.ZeroMemory(newKey);
            return false;
        }
    }

    private static bool SameWrap(VaultHeader a, VaultHeader b) =>
        a.Salt.AsSpan().SequenceEqual(b.Salt)
        && a.KeyWrap.Nonce.AsSpan().SequenceEqual(b.KeyWrap.Nonce)
        && a.KeyWrap.Ciphertext.AsSpan().SequenceEqual(b.KeyWrap.Ciphertext)
        && a.KeyWrap.Tag.AsSpan().SequenceEqual(b.KeyWrap.Tag);
}
