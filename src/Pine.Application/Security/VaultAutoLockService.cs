using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace Pine.Application.Security;

/// <summary>
/// The idle watchdog: polls the session and locks it once the idle timeout has elapsed (TD-11).
/// </summary>
/// <remarks>
/// <para>
/// WHY A POLL AND NOT A ONE-SHOT TIMER PER ACTIVITY. Activity is reported from UI input handlers,
/// so it can arrive tens of times a second. Re-arming a timer on each report would put a kernel
/// object update on the keystroke path to save one cheap comparison every fifteen seconds. The poll
/// reads two fields and subtracts two timestamps; it costs nothing and it cannot fall out of step
/// with the session, because the session owns the timestamp and this class owns no countdown state
/// of its own.
/// </para>
/// <para>
/// WHY <see cref="TimeProvider"/> AND NOT <see cref="System.Timers.Timer"/> OR
/// <see cref="DateTime.UtcNow"/>. Both the schedule and the clock come from the injected provider,
/// which is what makes an idle lock testable: a test hands in a fake provider, advances it past the
/// timeout, and asserts the session locked - in microseconds, deterministically, with no
/// <c>Task.Delay</c> anywhere. This is the same abstraction the command handlers already take
/// (<c>VoidTransactionCommandHandler</c>), so nothing new was invented for this ticket.
/// </para>
/// <para>
/// IT ALSO IMPLEMENTS <see cref="IHostedService"/> so a host that runs one starts and stops it
/// automatically. Avalonia heads that do not run a host can call <see cref="StartMonitoring"/>
/// themselves - but they do not have to, which is why
/// <see cref="VaultAutoLockOptions.StartAutomatically"/> defaults to true and the constructor arms
/// the timer: merely RESOLVING the service is enough. A watchdog that has to be remembered is the
/// class of failure this ticket exists to fix.
/// </para>
/// </remarks>
public sealed class VaultAutoLockService : IVaultAutoLockService, IHostedService, IDisposable
{
    private readonly IVaultSession _session;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _pollInterval;
    private readonly object _gate = new();

    private ITimer? _timer;
    private long _idleTimeoutTicks;
    private bool _disposed;

    /// <summary>Creates the watchdog over a session and a clock.</summary>
    /// <param name="session">The session to watch and, when it goes idle, to lock.</param>
    /// <param name="timeProvider">The clock AND the timer source; see the class remarks.</param>
    /// <param name="options">
    /// The timeout and poll interval. Optional so a composition root that wants the shipped defaults
    /// does not have to register anything extra.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The configured idle timeout is outside the supported range.
    /// </exception>
    public VaultAutoLockService(
        IVaultSession session,
        TimeProvider timeProvider,
        VaultAutoLockOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _session = session;
        _timeProvider = timeProvider;

        var effective = options ?? new VaultAutoLockOptions();

        ValidateIdleTimeout(effective.IdleTimeout);
        _idleTimeoutTicks = effective.IdleTimeout.Ticks;

        // Clamped rather than validated: a poll interval longer than the timeout it is checking is
        // a configuration mistake with a single sensible reading - check at least four times per
        // timeout - and refusing to start the watchdog over it would leave the vault unprotected,
        // which is strictly worse than polling more often than asked.
        var quarterTimeout = TimeSpan.FromTicks(effective.IdleTimeout.Ticks / 4);
        var requested = effective.PollInterval > TimeSpan.Zero
            ? effective.PollInterval
            : VaultAutoLockOptions.DefaultPollInterval;

        _pollInterval = requested < quarterTimeout ? requested : quarterTimeout;

        if (effective.StartAutomatically)
        {
            StartMonitoring();
        }
    }

    /// <inheritdoc />
    public TimeSpan IdleTimeout => TimeSpan.FromTicks(Interlocked.Read(ref _idleTimeoutTicks));

    /// <inheritdoc />
    public bool IsMonitoring
    {
        get { lock (_gate) { return _timer != null; } }
    }

    /// <inheritdoc />
    public TimeSpan? TimeUntilIdleLock
    {
        get
        {
            if (!IsMonitoring || _session.LastActivityUtc is not { } lastActivity)
            {
                return null;
            }

            var remaining = IdleTimeout - (_timeProvider.GetUtcNow() - lastActivity);

            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }

    /// <inheritdoc />
    public void StartMonitoring()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_timer != null)
            {
                return;
            }

            _timer = _timeProvider.CreateTimer(OnTick, state: null, _pollInterval, _pollInterval);
        }
    }

    /// <inheritdoc />
    public void StopMonitoring()
    {
        ITimer? timer;

        lock (_gate)
        {
            timer = _timer;
            _timer = null;
        }

        timer?.Dispose();
    }

    /// <inheritdoc />
    public void SetIdleTimeout(TimeSpan idleTimeout)
    {
        ValidateIdleTimeout(idleTimeout);

        // The countdown itself lives on the session as a timestamp, so changing the timeout takes
        // effect on the very next poll with no rescheduling and no reset of the elapsed time. A user
        // who shortens the timeout past the time already elapsed gets locked at the next poll, which
        // is the correct reading of "lock me sooner".
        Interlocked.Exchange(ref _idleTimeoutTicks, idleTimeout.Ticks);
    }

    /// <inheritdoc />
    public bool CheckNow()
    {
        // IsUnlocked and LastActivityUtc are each read atomically by the session, and this method
        // does not need them to be consistent WITH EACH OTHER: if the session locks between the two
        // reads, the Lock call below is a no-op on an already-locked session and returns false.
        if (!_session.IsUnlocked || _session.LastActivityUtc is not { } lastActivity)
        {
            return false;
        }

        if (_timeProvider.GetUtcNow() - lastActivity < IdleTimeout)
        {
            return false;
        }

        _session.Lock(VaultLockReason.IdleTimeout);

        return true;
    }

    /// <summary>The timer callback. Runs on a pool thread, never on the UI thread.</summary>
    /// <remarks>
    /// EVERYTHING IS CAUGHT. An exception escaping a timer callback terminates the process, and the
    /// most likely source of one is a handler of <see cref="IVaultSession.Locked"/> - UI code, on
    /// the wrong thread, written by somebody who did not read this comment. Crashing the app over a
    /// failed navigation would be a far worse outcome than a missed lock, and the missed lock is not
    /// permanent: the next poll tries again. Pine has no <c>ILogger</c> anywhere yet (TD-14), so
    /// there is nowhere honest to record the failure; when logging arrives this is one of the first
    /// places that needs it.
    /// </remarks>
    private void OnTick(object? state)
    {
        try
        {
            CheckNow();
        }
        catch (Exception)
        {
            // Deliberately swallowed. See the remarks above.
        }
    }

    /// <summary>Starts the watchdog when the service is hosted by a generic host.</summary>
    Task IHostedService.StartAsync(CancellationToken cancellationToken)
    {
        StartMonitoring();

        return Task.CompletedTask;
    }

    /// <summary>
    /// Stops the watchdog AND locks the vault: the process is going away, so the key should not
    /// outlive it in a heap that might be dumped.
    /// </summary>
    Task IHostedService.StopAsync(CancellationToken cancellationToken)
    {
        StopMonitoring();
        _session.Lock(VaultLockReason.Shutdown);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Stops the timer. Deliberately does NOT lock the session: disposing the watchdog is not the
    /// same intention as closing the vault, and the session has its own <c>Dispose</c> for that.
    /// </summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        StopMonitoring();
    }

    private static void ValidateIdleTimeout(TimeSpan idleTimeout)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(idleTimeout, VaultAutoLockOptions.MinimumIdleTimeout);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(idleTimeout, VaultAutoLockOptions.MaximumIdleTimeout);
    }
}
