using System;

namespace Pine.Application.Security;

/// <summary>
/// Carried by <see cref="IVaultSession.Locked"/>: what happened, and when.
/// </summary>
/// <param name="reason">Why the session locked.</param>
/// <param name="lockedAtUtc">
/// The instant the lock took effect, read from the session's injected <see cref="TimeProvider"/>
/// rather than from <see cref="DateTime.UtcNow"/>, so a test driving a fake clock sees the fake
/// clock's answer here too.
/// </param>
public sealed class VaultLockedEventArgs(VaultLockReason reason, DateTimeOffset lockedAtUtc) : EventArgs
{
    /// <summary>Why the session locked.</summary>
    public VaultLockReason Reason { get; } = reason;

    /// <summary>The instant the lock took effect.</summary>
    public DateTimeOffset LockedAtUtc { get; } = lockedAtUtc;
}
