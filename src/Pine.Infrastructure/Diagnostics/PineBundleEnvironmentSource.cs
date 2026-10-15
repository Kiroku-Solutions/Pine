using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pine.Core.Diagnostics;
using Pine.Core.Security;
using Pine.Infrastructure.Data;

namespace Pine.Infrastructure.Diagnostics;

/// <summary>
/// Reads the migration head and row counts for the diagnostic bundle (TD-84). Counts only: no query
/// here materialises a row. Any failure yields <see cref="BundleEnvironmentSnapshot.Unavailable"/>
/// rather than an exception message, which could carry the vault path (TD-23).
/// </summary>
public sealed class PineBundleEnvironmentSource : IBundleEnvironmentSource
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IVaultLockState _vaultLockState;

    /// <summary>Creates the source.</summary>
    /// <param name="scopeFactory">Scope the scoped <c>PineDbContext</c> is resolved from.</param>
    /// <param name="vaultLockState">Consulted first: a locked vault has no key to open it with.</param>
    public PineBundleEnvironmentSource(IServiceScopeFactory scopeFactory, IVaultLockState vaultLockState)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(vaultLockState);
        _scopeFactory = scopeFactory;
        _vaultLockState = vaultLockState;
    }

    /// <inheritdoc />
    public async Task<BundleEnvironmentSnapshot> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (!_vaultLockState.IsUnlocked)
        {
            return BundleEnvironmentSnapshot.Unavailable;
        }

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<PineDbContext>();

            var applied = await context.Database.GetAppliedMigrationsAsync(cancellationToken).ConfigureAwait(false);
            var accounts = await context.Accounts.CountAsync(cancellationToken).ConfigureAwait(false);
            var transactions = await context.Transactions.CountAsync(cancellationToken).ConfigureAwait(false);
            var budgets = await context.Budgets.CountAsync(cancellationToken).ConfigureAwait(false);

            return new BundleEnvironmentSnapshot(applied.LastOrDefault(), accounts, transactions, budgets);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return BundleEnvironmentSnapshot.Unavailable;
        }
    }
}
