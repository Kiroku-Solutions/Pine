using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pine.Core.Diagnostics;
using Pine.Core.Security;
using Pine.Infrastructure.Data;

namespace Pine.Infrastructure.Diagnostics;

/// <summary>
/// Asks the vault database, over a real connection, whether it opens and whether its schema is
/// current -- and reports the answer as a status, never as an exception message.
/// </summary>
/// <remarks>
/// <para>
/// TD-15: THE OLD PANEL WAS MOST CONFIDENT WHEN IT WAS MOST WRONG. <c>DiagnosticsViewModel</c> set
/// <c>DatabaseStatus = "Connected (Healthy)"</c> from a constant and printed a hardcoded
/// "Migrations are up to date." line, so a missing, corrupt or unmigrated vault produced exactly the
/// same reassuring screen as a healthy one. Every word this probe returns is now measured.
/// </para>
/// <para>
/// A SINGLETON THAT TAKES <see cref="IServiceScopeFactory"/>, NOT A <c>PineDbContext</c>, AND THIS
/// IS THE WHOLE DESIGN. <c>PineDbContext</c> is registered <c>Scoped</c> in every head, from a
/// factory that reads <c>IVaultSession</c> and THROWS <see cref="InvalidOperationException"/>
/// when the vault is locked. The consumer of this probe, <c>DiagnosticsViewModel</c>, is
/// <c>Transient</c> and is resolved straight from the root provider. Injecting the context -- or an
/// <c>IDbContextFactory</c> over it -- would therefore fail twice over: it would capture a scoped
/// service in a longer-lived graph, which every one of Pine's five composition roots rejects
/// outright because TD-01 turned <c>ValidateScopes</c> on in all of them; and on the locked vault
/// that is the panel's most likely state it would throw during construction, taking the diagnostics
/// screen down at precisely the moment someone opened it to find out what was wrong. Creating the
/// scope inside <see cref="CheckAsync"/>, only after the session says a vault is open, is what makes
/// the probe safe to hold for the life of the process and safe to call while locked.
/// </para>
/// <para>
/// THE EXCEPTION GOES TO THE LOG, NEVER INTO THE RETURN VALUE. <see cref="DatabaseHealth"/>
/// deliberately has no message field: TD-23 is open specifically to stop <c>ex.Message</c> reaching
/// users, and a SQLite or EF Core failure message routinely embeds the database file path -- which
/// on a side-loaded or portable install is a path straight through the user's home directory. So a
/// fault is logged at <see cref="LogLevel.Error"/> through <see cref="ILogger"/>, where
/// <see cref="LogRedactor"/> scrubs it on the way to disk (TD-14/TD-25), and the caller is handed a
/// bare <see cref="DatabaseHealthStatus.Faulted"/>. Do not add a message parameter to the reading.
/// </para>
/// <para>
/// A CANCELLED PROBE IS NOT A FAULT. <see cref="OperationCanceledException"/> is re-thrown ahead of
/// the general catch: a refresh the user navigated away from would otherwise paint the card red and
/// write an error record for something that was working perfectly.
/// </para>
/// </remarks>
public sealed partial class PineDatabaseHealthProbe : IDatabaseHealthProbe
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IVaultLockState _vaultLockState;
    private readonly ILogger<PineDatabaseHealthProbe> _logger;

    /// <param name="scopeFactory">
    /// Creates the short-lived scope the scoped <c>PineDbContext</c> is resolved from. See the type
    /// remarks: this is the dependency that keeps a singleton probe legal under
    /// <c>ValidateScopes</c>.
    /// </param>
    /// <param name="vaultLockState">
    /// Consulted BEFORE any scope is created, because on a locked vault the context factory throws
    /// by design and "locked" is a normal reading rather than a failure to report.
    /// <para>
    /// It is <see cref="IVaultLockState"/> and not <c>IVaultSession</c> because that interface lives
    /// in <c>Pine.Application</c>, which <c>Pine.Infrastructure</c> deliberately does not reference.
    /// <c>IVaultSession</c> EXTENDS this one, so the object bound here is the same session, not an
    /// adapter over it; <c>AddPineApplication</c> makes that binding once rather than each of the
    /// five composition roots repeating it (TD-29).
    /// </para>
    /// </param>
    /// <param name="logger">Where a fault's detail goes instead of into the returned reading.</param>
    public PineDatabaseHealthProbe(
        IServiceScopeFactory scopeFactory,
        IVaultLockState vaultLockState,
        ILogger<PineDatabaseHealthProbe> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(vaultLockState);
        ArgumentNullException.ThrowIfNull(logger);

        _scopeFactory = scopeFactory;
        _vaultLockState = vaultLockState;
        _logger = logger;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The order of the two database questions matters. <c>CanConnectAsync</c> first, because
    /// <c>GetPendingMigrationsAsync</c> has to read <c>__EFMigrationsHistory</c> and would report a
    /// database that is simply absent as a failure to read migrations -- a true statement that names
    /// the wrong problem.
    /// </remarks>
    public async Task<DatabaseHealth> CheckAsync(CancellationToken cancellationToken = default)
    {
        // Deliberately before the try, and before any scope exists: with the vault locked there is
        // no key in the process, opening the database is impossible by design, and resolving
        // PineDbContext would throw an InvalidOperationException that this method would then have to
        // launder back into the "normal state" it already knows it is in.
        if (!_vaultLockState.IsUnlocked)
        {
            return DatabaseHealth.Locked;
        }

        // An already-cancelled refresh does no work at all. Checked explicitly rather than left to
        // the providers, so that cancellation is a property of this method rather than of whichever
        // EF call happens to notice first.
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();

            var context = scope.ServiceProvider.GetRequiredService<PineDbContext>();

            if (!await context.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false))
            {
                return DatabaseHealth.Unreachable;
            }

            var pendingCount = (await context.Database
                .GetPendingMigrationsAsync(cancellationToken)
                .ConfigureAwait(false)).Count();

            return pendingCount == 0
                ? DatabaseHealth.Healthy
                : new DatabaseHealth(DatabaseHealthStatus.PendingMigrations, pendingCount);
        }
        catch (OperationCanceledException)
        {
            // Not a fault, and deliberately ahead of the general catch. See the type remarks.
            throw;
        }
        catch (Exception exception)
        {
            // The ONLY place the detail is allowed to go. Never into the returned reading (TD-23).
            LogProbeFailed(_logger, exception);

            return DatabaseHealth.Faulted;
        }
    }

    // The source-generated LoggerMessage delegate. CA1848 is an error in this repository, and the
    // generator is also what stamps the stable, dotted, snake_case event id 30-Observability §4.1
    // requires. The message carries no interpolated state at all: everything the reader needs is in
    // the exception object, which the sink redacts, and anything spelled out here would be a second,
    // unredacted copy of it.
    [LoggerMessage(
        EventId = 1,
        EventName = "diagnostics.database_probe_failed",
        Level = LogLevel.Error,
        Message = "The vault database health probe failed.")]
    private static partial void LogProbeFailed(ILogger logger, Exception exception);
}
