namespace Pine.Core.Security;

/// <summary>
/// Thrown when an operation that requires an unlocked vault is requested while the vault is
/// locked. Distinct from a generic <see cref="InvalidOperationException"/> so callers can tell
/// "you need to unlock first" apart from a real failure.
/// </summary>
/// <remarks>
/// <para>
/// RELOCATED FROM <c>Pine.Application.Security</c> BY TD-16, and the relocation is the point. This
/// type already existed one layer up, where <c>ChangeVaultPasswordCommand</c> and
/// <c>ExportVaultBackupCommand</c> throw it. TD-16 needed the same meaning in
/// <c>Pine.Infrastructure</c>, which references only <c>Pine.Core</c> -- and defining a second
/// <c>VaultLockedException</c> in Core is what a first attempt actually did, producing two types
/// with one name and one meaning in a single solution. That is a landmine, not a design: a
/// <c>catch (VaultLockedException)</c> would silently depend on which <c>using</c> happened to be
/// in scope. Moving the existing type down leaves exactly one.
/// </para>
/// <para>
/// WHY IT IS ALSO WHAT TD-23 WANTS. The composition roots throw
/// <c>InvalidOperationException("Cannot access database: Vault is locked.")</c> from the
/// <c>PineDbContext</c> factory, and <c>CategorizationTrainingBackgroundService</c> has to
/// recognise that case by SNIFFING THE MESSAGE for the substring "Vault". Everything reached
/// through <see cref="IVaultFileCipher"/> and the categoriser's model store raises this type
/// instead, so a background service can treat "the user locked up and went home" as the ordinary
/// condition it is.
/// </para>
/// </remarks>
public sealed class VaultLockedException : Exception
{
    /// <summary>Creates the exception with the standard message.</summary>
    public VaultLockedException()
        : base("The vault is locked. Unlock it before performing this operation.")
    {
    }

    /// <summary>Creates the exception with a caller-supplied message.</summary>
    /// <remarks>
    /// The message must never name a vault path or carry key material: it reaches the log, and a
    /// line saying a vault is locked must not also say where that vault is.
    /// </remarks>
    public VaultLockedException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception wrapping an underlying cause.</summary>
    public VaultLockedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Thrown when a sealed vault file cannot be opened (TD-16).
/// </summary>
/// <remarks>
/// <para>
/// ONE EXCEPTION FOR EVERY FAILURE, FOLLOWING <c>VaultKeyUnwrapException</c>'s PRECEDENT. A blob
/// that is truncated, carries the wrong magic, was sealed for a different purpose, was sealed by a
/// different vault, or has had a single bit flipped all arrive here identically, with no inner
/// exception and no property separating them. Any signal that distinguished them would answer the
/// one question an attacker holding a stolen file actually has.
/// </para>
/// <para>
/// CALLERS ARE EXPECTED TO SWALLOW THIS FOR DERIVED DATA. The categoriser's model can always be
/// retrained from the ledger, so its store treats an unopenable model as an absent one. That is a
/// deliberate policy decision at the call site, not a weakness here -- and it is the right one,
/// because the alternative is a corrupt cache file preventing a vault from being used.
/// </para>
/// </remarks>
public sealed class VaultFileCipherException : Exception
{
    /// <summary>Creates the exception. The message is deliberately identical for every cause.</summary>
    /// <param name="purpose">The purpose the open was attempted under.</param>
    public VaultFileCipherException(string purpose)
        : base($"The sealed vault file for '{purpose}' could not be opened.")
    {
        Purpose = purpose;
    }

    /// <summary>The purpose the caller asked for. Not a hint about why the open failed.</summary>
    public string Purpose { get; }
}
