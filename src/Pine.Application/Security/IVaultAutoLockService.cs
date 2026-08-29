using System;

namespace Pine.Application.Security;

/// <summary>
/// Locks the vault after a period with no reported user activity (TD-11).
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS LIVES IN THE APPLICATION LAYER. It needs a timer, and <c>Pine.Core</c> is
/// reference-free and must stay that way - a domain that schedules callbacks is no longer a domain.
/// It must equally not live in the UI: there are five composition roots, and a rule implemented in
/// one of them is a rule the other four do not have. The application layer is the only place that
/// sees <see cref="IVaultSession"/> and can be shared by every head, and nothing here references
/// Avalonia or any UI type.
/// </para>
/// <para>
/// WHAT THE UI STILL OWNS. Two things, and only two: calling
/// <see cref="IVaultSession.ReportActivity"/> when the user does something, and handling
/// <see cref="IVaultSession.Locked"/> by navigating away from whatever is on screen. This service
/// cannot do either - it has no idea what input is, and no way to touch a window.
/// </para>
/// </remarks>
public interface IVaultAutoLockService
{
    /// <summary>How long the vault may be idle before it locks itself.</summary>
    TimeSpan IdleTimeout { get; }

    /// <summary>Whether the idle timer is currently running.</summary>
    bool IsMonitoring { get; }

    /// <summary>
    /// How long is left before an idle lock, or <c>null</c> when the vault is locked or the timer is
    /// not running. Intended for a "locking in N seconds" warning; it is a snapshot and is stale the
    /// instant it is returned.
    /// </summary>
    TimeSpan? TimeUntilIdleLock { get; }

    /// <summary>Starts the idle timer. Idempotent.</summary>
    void StartMonitoring();

    /// <summary>
    /// Stops the idle timer. Idempotent. Does NOT lock the vault - stopping the watchdog and
    /// closing the vault are different intentions and must not be spelled the same way.
    /// </summary>
    void StopMonitoring();

    /// <summary>
    /// Changes the idle timeout, applying immediately to the countdown already in progress.
    /// </summary>
    /// <param name="idleTimeout">The new timeout.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Outside <see cref="VaultAutoLockOptions.MinimumIdleTimeout"/> ..
    /// <see cref="VaultAutoLockOptions.MaximumIdleTimeout"/>. Rejected rather than clamped: silently
    /// substituting a different value for a security setting the user chose is how a setting screen
    /// comes to lie about what is in force.
    /// </exception>
    void SetIdleTimeout(TimeSpan idleTimeout);

    /// <summary>
    /// Performs the idleness check immediately instead of waiting for the next poll, locking the
    /// session if it is due.
    /// </summary>
    /// <returns><c>true</c> if this call locked the session.</returns>
    /// <remarks>
    /// Exposed so the behaviour can be tested against a controlled clock with no timer involved at
    /// all, and so a UI that observes its window being minimised or the machine resuming from sleep
    /// can force an immediate check rather than leaving the vault open for the rest of the poll
    /// interval.
    /// </remarks>
    bool CheckNow();
}
