using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Pine.Application.Security;

/// <summary>
/// Registers the vault idle auto-lock watchdog (TD-11) into a composition root.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS IS SEPARATE FROM <c>AddPineApplication</c>. The watchdog needs
/// <see cref="IVaultSession"/>, and that binding is made by each UI head rather than by the
/// application layer. Folding auto-lock into <c>AddPineApplication</c> would silently add a
/// background timer and an <see cref="IHostedService"/> to every root that calls it - including
/// <c>Pine.Cli</c> and the test hosts - which is a surprise a security control should not spring on
/// its callers. One explicit line in each head is the honest cost.
/// </para>
/// <para>
/// It must be called AFTER <c>IVaultSession</c> is registered only in the sense that the container
/// must eventually be able to resolve it; service registration order does not matter to the
/// container itself.
/// </para>
/// </remarks>
public static class VaultSecurityServiceCollectionExtensions
{
    /// <summary>
    /// Adds the idle auto-lock watchdog as a singleton, and as an <see cref="IHostedService"/> so a
    /// generic host starts and stops it automatically.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configure">
    /// Optional hook to change the idle timeout, the poll interval, or whether the watchdog arms
    /// itself on construction. Omit it for the shipped defaults - ten minutes, checked every fifteen
    /// seconds; see <see cref="VaultAutoLockOptions"/> for why those numbers.
    /// </param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddPineVaultAutoLock(
        this IServiceCollection services,
        Action<VaultAutoLockOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new VaultAutoLockOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(options);

        // TryAdd, not Add: Windows and macOS already register TimeProvider.System, and replacing
        // theirs would silently swap the clock every command handler reads out from under them.
        // The three heads that do not register it get the system clock here rather than failing to
        // resolve the watchdog at all (see the second VaultSession constructor for the same problem).
        services.TryAddSingleton(TimeProvider.System);

        // The CONCRETE type is the registration, and both the interface and the hosted service
        // resolve back to it, so there is exactly one timer no matter how many ways it is asked for.
        // Two registrations would mean two watchdogs racing to lock the same session.
        services.TryAddSingleton<VaultAutoLockService>();
        services.TryAddSingleton<IVaultAutoLockService>(
            sp => sp.GetRequiredService<VaultAutoLockService>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, VaultAutoLockService>(
            sp => sp.GetRequiredService<VaultAutoLockService>()));

        return services;
    }
}
