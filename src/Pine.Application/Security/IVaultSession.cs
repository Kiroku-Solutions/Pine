using System;

namespace Pine.Application.Security;

/// <summary>
/// The process-wide record of which vault is open and the key it was opened with.
/// </summary>
/// <remarks>
/// <para>
/// TD-11 ADDED THREE THINGS TO THIS CONTRACT, and each closes a specific hole the audit named.
/// </para>
/// <list type="number">
///   <item><description><see cref="Locked"/> - a lock was previously invisible. It happened in one
///   place, the <c>catch</c> of a failed unlock, and nothing else needed to know. An automatic lock
///   is different: it happens while the user is looking at a screen full of their transactions, and
///   the shell has to navigate away from that screen. Without an event the UI would have to poll
///   <see cref="IsUnlocked"/>, which is exactly the kind of thing that gets forgotten on one code
///   path and leaks the data on the screen.</description></item>
///   <item><description><see cref="ReportActivity"/> and <see cref="LastActivityUtc"/> - idleness
///   has to be measured somewhere, and the session is the only object that already knows whether
///   there is anything to be idle ABOUT. Keeping the timestamp here rather than in the auto-lock
///   service also means the countdown resets correctly when the vault is unlocked, without the two
///   having to agree on a protocol.</description></item>
///   <item><description><see cref="Lock(VaultLockReason)"/> - see
///   <see cref="VaultLockReason"/>.</description></item>
/// </list>
/// <para>
/// THREAD SAFETY IS PART OF THE CONTRACT, not an implementation detail, because TD-11 introduces a
/// second thread: an implementation MUST tolerate <see cref="Lock(VaultLockReason)"/> being called
/// from a timer thread while the UI thread reads <see cref="Key"/> and <see cref="IsUnlocked"/>.
/// See <c>VaultSession</c> for exactly what that guarantees and, honestly, what it does not.
/// </para>
/// </remarks>
public interface IVaultSession
{
    /// <summary>
    /// The vault encryption key, or <c>null</c> when locked.
    /// </summary>
    /// <remarks>
    /// The returned array is the session's own buffer, not a copy. <see cref="Lock()"/> zeroes it,
    /// so a caller that captures this reference and reads it AFTER a lock reads zeroes - which
    /// fails closed (SQLCipher refuses to open) rather than leaking anything. Callers should use it
    /// immediately and never cache it.
    /// </remarks>
    byte[]? Key { get; }

    /// <summary>The directory of the open vault, or <c>null</c> when locked.</summary>
    string? DbPath { get; }

    /// <summary>Whether a vault is currently open.</summary>
    bool IsUnlocked { get; }

    /// <summary>
    /// When user activity was last reported, or <c>null</c> when locked. Read from the session's
    /// <see cref="TimeProvider"/>, so a fake clock drives it in tests.
    /// </summary>
    DateTimeOffset? LastActivityUtc { get; }

    /// <summary>
    /// Raised after the session transitions from unlocked to locked - never for a lock call on an
    /// already-locked session, so the repeated defensive <c>Lock()</c> calls in the unlock failure
    /// paths do not produce a storm of events.
    /// </summary>
    /// <remarks>
    /// HANDLERS RUN ON WHATEVER THREAD LOCKED THE SESSION. For an idle lock that is a timer thread,
    /// so a UI handler MUST marshal to the UI thread itself; the application layer cannot do it for
    /// them without taking a dependency on a UI framework, which it must not. The event is raised
    /// OUTSIDE the session's internal lock, so a handler may safely call back into the session.
    /// </remarks>
    event EventHandler<VaultLockedEventArgs>? Locked;

    /// <summary>Opens the session with a vault directory and its derived key.</summary>
    /// <param name="dbPath">The vault directory.</param>
    /// <param name="key">The derived key. Copied; the caller keeps ownership of its own array.</param>
    /// <exception cref="ArgumentException"><paramref name="key"/> is null or empty.</exception>
    void Unlock(string dbPath, byte[] key);

    /// <summary>
    /// Records that the user did something, resetting the idle countdown.
    /// </summary>
    /// <remarks>
    /// Called from UI input handlers, so it may run on every keystroke or pointer move and MUST be
    /// cheap and allocation-free. A no-op while locked: there is nothing to keep alive, and
    /// accepting activity for a locked vault would let a stray input event resurrect a countdown
    /// against a session that no longer exists.
    /// </remarks>
    void ReportActivity();

    /// <summary>
    /// Locks the session, zeroing the key. Equivalent to
    /// <c>Lock(VaultLockReason.UserRequested)</c>; retained so every existing call site keeps
    /// compiling and keeps its original meaning.
    /// </summary>
    void Lock();

    /// <summary>Locks the session, zeroing the key, and reports why.</summary>
    /// <param name="reason">Why the lock happened; carried on <see cref="Locked"/>.</param>
    void Lock(VaultLockReason reason);
}
