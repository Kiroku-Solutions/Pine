namespace Pine.Application.Security;

/// <summary>
/// Thrown when an operation that requires an unlocked vault is requested while the vault is
/// locked. Distinct from a generic <see cref="InvalidOperationException"/> so callers can tell
/// "you need to unlock first" apart from a real failure.
/// </summary>
public sealed class VaultLockedException : Exception
{
    public VaultLockedException()
        : base("The vault is locked. Unlock it before performing this operation.")
    {
    }

    public VaultLockedException(string message)
        : base(message)
    {
    }

    public VaultLockedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
