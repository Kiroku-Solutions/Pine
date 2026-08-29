using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pine.Core.Diagnostics;

namespace Pine.Infrastructure.Diagnostics;

/// <summary>
/// Registers the three seams behind Pine's diagnostics screen (TD-15) into a composition root.
/// </summary>
/// <remarks>
/// <para>
/// SEPARATE FROM <c>AddPineLogging</c> ON PURPOSE, and the reason is
/// <see cref="IDatabaseHealthProbe"/>. Its implementation resolves <c>PineDbContext</c>, which only
/// the UI heads register; the CLI and the test hosts log without ever opening a vault, and folding
/// the probe into the logging registration would hand them a service that cannot be constructed and
/// a container that fails validation for a screen they do not have. Logging is universal,
/// diagnostics are not, so they are two calls.
/// </para>
/// <para>
/// REQUIRES <c>AddPineLogging()</c> TO HAVE BEEN CALLED FIRST. The tail reader is constructed from
/// the <c>PineLoggingOptions</c> instance already in the container, because it must read back the
/// files the sink is writing -- resolving them separately would let a head point the sink at one
/// directory and the reader at another, and the panel would then honestly report an empty log while
/// records piled up somewhere else. The factory below therefore uses
/// <c>GetRequiredService</c>: a missing options object is a composition-root bug and should fail
/// loudly at first resolve, not silently degrade to the default directory.
/// </para>
/// <para>
/// ALL THREE ARE SINGLETONS. None holds per-request state -- the runtime provider is stateless, the
/// tail reader holds a directory path, and the probe creates its own scope per call (see
/// <see cref="PineDatabaseHealthProbe"/> for why that is the only shape that survives
/// <c>ValidateScopes</c>).
/// </para>
/// </remarks>
public static class PineDiagnosticsServiceCollectionExtensions
{
    /// <summary>
    /// Adds the log tail reader, the runtime info provider and the database health probe.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The same service collection, for chaining.</returns>
    /// <remarks>
    /// <c>TryAdd</c> throughout, mirroring <c>AddPineLogging</c>: a head that calls this twice, or
    /// that has already substituted one of the three for a test double, keeps the registration it
    /// already had rather than acquiring a second one.
    /// </remarks>
    public static IServiceCollection AddPineDiagnostics(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<ILogTailReader>(provider =>
            new PineLogTailReader(provider.GetRequiredService<PineLoggingOptions>()));

        services.TryAddSingleton<IRuntimeInfoProvider, PineRuntimeInfoProvider>();
        services.TryAddSingleton<IDatabaseHealthProbe, PineDatabaseHealthProbe>();

        return services;
    }
}
