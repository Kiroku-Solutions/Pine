using System;

namespace Pine.Application.Security;

/// <summary>
/// How long the vault may sit untouched before it locks itself, and how often that is checked.
/// </summary>
/// <remarks>
/// A plain settable class rather than an <c>IOptions</c> binding: the timeout is a user preference
/// that a settings screen changes at runtime, and the options pattern's snapshot semantics would
/// mean the change did not reach the already-running timer.
/// </remarks>
public sealed class VaultAutoLockOptions
{
    /// <summary>
    /// The shipped default idle timeout: ten minutes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY TEN AND NOT FIVE OR SIXTY. The threat this defends against is the unattended unlocked
    /// machine - a laptop left on a cafe table, a desktop in a shared house, a screen visible to a
    /// colleague. Three constraints bracket the number:
    /// </para>
    /// <list type="bullet">
    ///   <item><description><b>Below the platform's own screen lock.</b> Windows and macOS
    ///   commonly idle-lock at around fifteen minutes when they are configured to lock at all.
    ///   Pine must lock BEFORE the platform does, otherwise its auto-lock is decoration on top of a
    ///   protection that already fired; ten minutes clears fifteen with room to spare. Pine also
    ///   cannot rely on the platform, because a machine with no screen lock configured is precisely
    ///   the machine that needs this.</description></item>
    ///   <item><description><b>Above a plausible pause in real work.</b> The activity signal is
    ///   input, and entering transactions, reading a statement on paper, or answering a phone call
    ///   mid-entry produces gaps of a minute or two, not ten. A timeout that interrupts genuine work
    ///   trains the user to raise it to its maximum, which is worse than a longer default.</description></item>
    ///   <item><description><b>Small against the unlock cost.</b> Re-unlocking costs one Argon2id
    ///   derivation at 64 MiB - a fraction of a second - plus typing the password. An interruption
    ///   that cheap can afford to be frequent.</description></item>
    /// </list>
    /// <para>
    /// The number is a default, not a policy: <see cref="IVaultAutoLockService.SetIdleTimeout"/>
    /// lets the user move it inside <see cref="MinimumIdleTimeout"/> and
    /// <see cref="MaximumIdleTimeout"/>.
    /// </para>
    /// </remarks>
    public static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromMinutes(10);

    /// <summary>
    /// The shortest timeout that may be configured. One minute: anything shorter locks in the
    /// middle of reading a screen and would be abandoned by the user rather than endured.
    /// </summary>
    public static readonly TimeSpan MinimumIdleTimeout = TimeSpan.FromMinutes(1);

    /// <summary>
    /// The longest timeout that may be configured. Eight hours is a working day - beyond that the
    /// feature is off in all but name, and a user who wants it off should be able to say so
    /// explicitly rather than by setting a number so large it is indistinguishable from disabled.
    /// </summary>
    public static readonly TimeSpan MaximumIdleTimeout = TimeSpan.FromHours(8);

    /// <summary>
    /// The default gap between idle checks: fifteen seconds.
    /// </summary>
    /// <remarks>
    /// This is the GRANULARITY of the lock, not its accuracy target: the vault locks somewhere
    /// between <c>IdleTimeout</c> and <c>IdleTimeout + PollInterval</c> after the last activity.
    /// Fifteen seconds of slack on a ten-minute timeout is 2.5%, which no threat model distinguishes,
    /// and polling is preferred over arming a one-shot timer per activity report because activity is
    /// reported on every keystroke - rescheduling a timer that often would cost far more than the
    /// poll it replaces.
    /// </remarks>
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(15);

    /// <summary>How long the vault may be idle before it locks itself.</summary>
    public TimeSpan IdleTimeout { get; set; } = DefaultIdleTimeout;

    /// <summary>
    /// How often idleness is checked. Clamped by the service so it can never exceed the timeout it
    /// is meant to detect.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = DefaultPollInterval;

    /// <summary>
    /// Whether the auto-lock timer starts on its own when the service is constructed or hosted.
    /// </summary>
    /// <remarks>
    /// Defaults to <c>true</c> - a security control that has to be switched on is a security control
    /// somebody forgets to switch on, which is how TD-11 came to exist. It is settable only so a
    /// test, or a host that owns its own scheduling, can opt out.
    /// </remarks>
    public bool StartAutomatically { get; set; } = true;
}
