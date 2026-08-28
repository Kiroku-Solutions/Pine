namespace Pine.Application.Security;

/// <summary>
/// Why a vault session stopped being unlocked.
/// </summary>
/// <remarks>
/// <para>
/// WHY THE REASON IS CARRIED AT ALL (TD-11). Before this existed, <c>VaultSession.Lock()</c> was
/// called from exactly one place - the <c>catch</c> of a failed unlock - so "the vault locked" and
/// "the password was wrong" were the same event and nothing had to tell them apart. Once a lock can
/// also arrive from an idle timer, on the way out of the process, or from a button the user
/// pressed, the UI has to react differently to each: an idle lock should say so and offer to unlock
/// again, whereas a shutdown lock should say nothing because there is nobody left to read it.
/// </para>
/// <para>
/// This is an enum and not a message for the same reason <c>PasswordWeakness</c> is: the domain and
/// application layers do not own user-facing wording.
/// </para>
/// </remarks>
public enum VaultLockReason
{
    /// <summary>
    /// The user asked for it, explicitly or implicitly - a lock button, closing the vault, or the
    /// re-lock that follows a failed unlock attempt. The default, so that every pre-existing
    /// <c>Lock()</c> call site keeps its original meaning without being edited.
    /// </summary>
    UserRequested = 0,

    /// <summary>
    /// The idle timeout elapsed with no reported user activity. Raised from a timer thread, never
    /// from the UI thread - see <see cref="IVaultSession.Locked"/>.
    /// </summary>
    IdleTimeout = 1,

    /// <summary>
    /// The host told Pine it is suspending, sleeping or going to the background. Not raised by
    /// anything in the application layer, which has no way to observe platform lifecycle events;
    /// it exists so a UI shell that CAN observe them has a truthful reason to pass in rather than
    /// mislabelling a suspend as a user request.
    /// </summary>
    ApplicationSuspended = 2,

    /// <summary>
    /// The process is shutting down and the key is being cleared on the way out. Distinguished from
    /// <see cref="UserRequested"/> so a handler does not try to navigate a UI that is already gone.
    /// </summary>
    Shutdown = 3,
}
