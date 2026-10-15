using System;

namespace Pine.Core.Security;

/// <summary>
/// A backup cannot be opened with the vault's CURRENT data key, but it carries the key record (header) of the
/// era it was written in, and that header differs from the vault's current one: the vault's data key was rotated
/// (TD-86) after the backup was taken. The backup can still be restored if the user supplies the password that
/// was in force when it was created. Nothing was changed.
/// </summary>
public sealed class VaultBackupKeyMismatchException : Exception
{
    public VaultBackupKeyMismatchException(string backupPath)
        : base("The backup was encrypted under a data key that has since been rotated; the password from when it was created is needed.")
    {
        BackupPath = backupPath;
    }

    /// <summary>The backup that needs the old password. Never contains a secret.</summary>
    public string BackupPath { get; }
}

/// <summary>
/// The password given for an older backup does not unlock that backup's key record, or the record does not
/// belong to the backup. Nothing was changed and another attempt is allowed. Carries no password.
/// </summary>
public sealed class VaultBackupPasswordIncorrectException : Exception
{
    public VaultBackupPasswordIncorrectException(Exception? inner = null)
        : base("The password does not unlock this backup's key record.", inner)
    {
    }
}
