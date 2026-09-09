using System;
using System.Threading;

namespace Pine.Application.Security;

/// <summary>
/// The singleton that holds the open vault's key for the life of the unlock.
/// </summary>
/// <remarks>
/// <para>
/// WHAT THE ZEROING DOES AND DOES NOT BUY (TD-11). <see cref="Lock()"/> clears the key array before
/// dropping it, and that path is unchanged by this ticket - it is now simply reachable from more
/// than one place. What it CANNOT promise is that no copy survives: the key lives in a managed
/// <c>byte[]</c>, and a compacting garbage collector is free to have moved that array at any point
/// between <see cref="Unlock"/> and <see cref="Lock()"/>, leaving the old bytes behind in a region
/// this code has no reference to and cannot clear. The array can also be paged to disk by the OS.
/// The audit already records this; nothing here is claimed to fix it. Doing so properly means a
/// pinned or unmanaged buffer plus platform memory locking, and belongs with the envelope-key work
/// (TD-06), not here.
/// </para>
///
/// <para>
/// THREAD SAFETY, AND EXACTLY WHAT IT COVERS. Before TD-11 there was one thread. There are now two:
/// the auto-lock timer can call <see cref="Lock(VaultLockReason)"/> while the UI thread is reading
/// <see cref="Key"/> to build a <c>PineDbContext</c>. All state transitions and all reads of the
/// triple (key, path, unlocked) happen under one gate, so a reader can never observe a HALF-locked
/// session - a cleared key beside a live path, for instance, which would produce a
/// <c>PineDbContext</c> pointing at a real vault with a null key.
/// </para>
/// <para>
/// What the gate deliberately does NOT do is protect a caller that captures the array and reads it
/// later. That window is real and it is left open on purpose: closing it would mean handing out a
/// defensive copy per call, which multiplies the number of copies of the key in memory - the exact
/// thing the zeroing exists to minimise. The consequence of losing that race is that SQLCipher is
/// handed a zeroed key and refuses to open the database. It FAILS CLOSED: the worst outcome is a
/// spurious "cannot open vault" the user resolves by unlocking again, never a disclosure.
/// </para>
/// <para>
/// <see cref="ReportActivity"/> is exempt from the gate by design. It is called from UI input
/// handlers, potentially on every pointer move, and taking a lock there would put a contended
/// monitor on the input path for the sake of a timestamp that is read once every poll interval. It
/// uses <see cref="Interlocked"/> on a <c>long</c> of ticks instead, which is atomic on every
/// platform including 32-bit ones, where a plain <c>long</c> write would not be.
/// </para>
/// </remarks>
public class VaultSession : IVaultSession, IDisposable
{
    /// <summary>
    /// Guards every transition and every composite read of the session state. A private object
    /// rather than <c>this</c>, so no caller can take the same lock and deadlock against an event
    /// handler.
    /// </summary>
    private readonly object _gate = new();

    private readonly TimeProvider _timeProvider;

    private byte[]? _key;
    private string? _dbPath;

    /// <summary>
    /// Ticks of the last reported activity, or 0 when locked. A bare <c>long</c> written through
    /// <see cref="Interlocked"/> rather than a <see cref="DateTimeOffset"/> field, because only a
    /// naturally-atomic primitive can be updated without the gate.
    /// </summary>
    private long _lastActivityTicks;

    /// <summary>
    /// Creates a session that reads the clock through <paramref name="timeProvider"/>.
    /// </summary>
    /// <param name="timeProvider">
    /// The clock. Injected rather than calling <see cref="DateTime.UtcNow"/> so the idle countdown
    /// can be driven by a fake clock in a test - the same pattern the command handlers already use
    /// (see <c>VoidTransactionCommandHandler</c>), and the reason <c>TimeProvider.System</c> is
    /// registered in the composition roots.
    /// </param>
    public VaultSession(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Creates a session on the system clock.
    /// </summary>
    /// <remarks>
    /// WHY THIS OVERLOAD EXISTS: it dates from when each head registered its own services and two
    /// of them (<c>Pine.Ui.Android</c>, <c>Pine.Ui.Ios</c>) registered <c>IVaultSession</c> without
    /// a <see cref="TimeProvider"/> — so making the injected constructor the only one would have
    /// turned a compile-time-clean change into a runtime failure on exactly the heads CI has never
    /// built (TD-08). A third head, <c>Pine.Ui.Web</c>, was in that list until the Phase 3 platform
    /// review deleted it. The DI container prefers the greediest constructor it can satisfy, so in
    /// the shared root every head now gets the injected clock and nothing reaches this overload
    /// through composition; it survives for direct construction in tests.
    /// </remarks>
    public VaultSession()
        : this(TimeProvider.System)
    {
    }

    /// <inheritdoc />
    public byte[]? Key
    {
        get { lock (_gate) { return _key; } }
    }

    /// <inheritdoc />
    public string? DbPath
    {
        get { lock (_gate) { return _dbPath; } }
    }

    /// <inheritdoc />
    public bool IsUnlocked
    {
        get { lock (_gate) { return _key != null && _dbPath != null; } }
    }

    /// <inheritdoc />
    public DateTimeOffset? LastActivityUtc
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastActivityTicks);

            return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }

    /// <inheritdoc />
    public event EventHandler<VaultLockedEventArgs>? Locked;

    /// <inheritdoc />
    public void Unlock(string dbPath, byte[] key)
    {
        if (key == null || key.Length == 0)
            throw new ArgumentException("Key cannot be null or empty", nameof(key));

        lock (_gate)
        {
            // Copy to prevent external modification
            _key = new byte[key.Length];
            Array.Copy(key, _key, key.Length);

            _dbPath = dbPath;
        }

        // Seeded here rather than left at zero: an unlock IS activity, and without this the very
        // first idle check after unlocking would see "no activity ever reported" and would have to
        // choose between locking immediately and never locking at all.
        Interlocked.Exchange(ref _lastActivityTicks, _timeProvider.GetUtcNow().UtcTicks);
    }

    /// <inheritdoc />
    public void ReportActivity()
    {
        // Deliberately unsynchronised, and deliberately conditional on the session being open. See
        // the remarks on the class for why this must stay allocation- and lock-free.
        if (!IsUnlocked)
        {
            return;
        }

        Interlocked.Exchange(ref _lastActivityTicks, _timeProvider.GetUtcNow().UtcTicks);
    }

    /// <inheritdoc />
    public void Lock() => Lock(VaultLockReason.UserRequested);

    /// <inheritdoc />
    public void Lock(VaultLockReason reason)
    {
        bool transitioned;

        lock (_gate)
        {
            // The guard is what makes Lock idempotent, and idempotence is what lets the failed
            // unlock paths keep calling Lock() defensively without producing an event each time.
            transitioned = _key != null;

            if (transitioned)
            {
                Array.Clear(_key!, 0, _key!.Length);
                _key = null;
                _dbPath = null;
            }
        }

        Interlocked.Exchange(ref _lastActivityTicks, 0);

        if (!transitioned)
        {
            return;
        }

        // Raised OUTSIDE the gate, on purpose. A handler is arbitrary code - the UI shell's
        // navigation, in practice - and running it while holding the lock would let it deadlock by
        // touching the session, and would hold every other thread's Key read behind whatever the
        // handler does. The snapshot below also means a handler that unsubscribes mid-dispatch
        // cannot null the delegate out from under this call.
        Locked?.Invoke(this, new VaultLockedEventArgs(reason, _timeProvider.GetUtcNow()));
    }

    /// <summary>Locks the session on the way out, reporting <see cref="VaultLockReason.Shutdown"/>.</summary>
    public void Dispose()
    {
        Lock(VaultLockReason.Shutdown);
        GC.SuppressFinalize(this);
    }
}
