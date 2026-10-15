namespace Pine.Infrastructure.Sync;

/// <summary>
/// A merge that was not attempted, because the change set or the vaults it names could not be merged safely
/// (post-audit plan item 3.1b). Nothing was written.
/// </summary>
public sealed class SyncMergeRefusedException : InvalidOperationException
{
    public SyncMergeRefusedException()
    {
    }

    public SyncMergeRefusedException(string message)
        : base(message)
    {
    }

    public SyncMergeRefusedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
