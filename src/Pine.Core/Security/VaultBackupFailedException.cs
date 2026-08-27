namespace Pine.Core.Security;

/// <summary>
/// Thrown when a vault backup could not be produced (or an old backup could not be pruned).
/// Carries the destination path so a human can inspect what was attempted; any partially
/// written destination file is deleted before this is thrown, because a truncated backup is
/// worse than no backup at all.
/// </summary>
public sealed class VaultBackupFailedException : Exception
{
    public VaultBackupFailedException(string message, string? path, Exception? innerException)
        : base(message, innerException)
    {
        Path = path;
    }

    /// <summary>The backup path that was being written or deleted when the failure occurred.</summary>
    public string? Path { get; }
}
