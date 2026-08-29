using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Pine.Infrastructure.Diagnostics;

/// <summary>
/// Registers Pine's structured file logging (TD-14) into a composition root.
/// </summary>
/// <remarks>
/// <para>
/// ONE REGISTRATION, FIVE HEADS. Before this existed, Windows and macOS called
/// <c>AddLogging(b =&gt; b.AddConsole())</c> from a <c>WinExe</c> -- a subsystem with no console
/// attached, so every record was written to a handle that discards it -- and Web, Android and iOS
/// called a bare <c>AddLogging()</c>, which registers the factory but no provider at all, so their
/// records had nowhere to go either. Five heads, five ways of logging nothing. Replacing all five
/// call sites with one extension is what makes "Pine has logging" a property of the codebase rather
/// than of whichever head somebody last remembered to edit.
/// </para>
/// <para>
/// IT LIVES IN <c>Pine.Infrastructure</c>, NOT <c>Pine.Application</c>. The sink is a filesystem
/// concern -- directories, handles, rotation -- and Pine.Application deliberately references
/// nothing but Pine.Core. It is kept out of <c>AddPineApplication</c> for the same reason
/// <c>AddPineVaultAutoLock</c> is: adding a background file handle to every caller, the CLI and the
/// test hosts included, is not a surprise a shared registration should spring on them.
/// </para>
/// </remarks>
public static class PineLoggingServiceCollectionExtensions
{
    /// <summary>
    /// Adds the <see cref="ILoggerFactory"/> infrastructure and the Pine JSONL file provider.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configure">
    /// Optional hook to change the log directory, the minimum level, the rotation size or the
    /// retention count. Omit it for the shipped defaults -- <c>%LOCALAPPDATA%/Pine/logs</c>,
    /// <c>info</c> and above, 50 MB per file, 10 files -- which are the values
    /// <c>30-Observability</c> §4.4 specifies.
    /// </param>
    /// <returns>The same service collection, for chaining.</returns>
    /// <remarks>
    /// <c>TryAdd</c> throughout, so a head that calls this twice -- or that calls it after some
    /// other component has already added logging -- ends up with one options object and one
    /// provider, and therefore one file handle. Two providers over one directory would race on
    /// rotation.
    /// </remarks>
    public static IServiceCollection AddPineLogging(
        this IServiceCollection services,
        Action<PineLoggingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new PineLoggingOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(options);

        services.AddLogging(builder =>
        {
            // The factory's own filter chain is set from the same options, so a head that lowers the
            // minimum level actually gets the extra records rather than having them dropped before
            // the provider ever sees them. The provider filters again -- see PineFileLogger.
            builder.SetMinimumLevel(options.MinimumLevel);

            builder.Services.TryAddEnumerable(
                ServiceDescriptor.Singleton<ILoggerProvider, PineFileLoggerProvider>());
        });

        return services;
    }
}
