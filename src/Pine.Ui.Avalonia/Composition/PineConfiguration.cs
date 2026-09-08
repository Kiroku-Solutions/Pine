using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Configuration;
using Pine.Core.Configuration;

namespace Pine.Ui.Avalonia.Composition;

/// <summary>
/// Builds the configuration Pine reads at startup, and turns it into a validated
/// <see cref="PineOptions"/> (TD-27).
/// </summary>
/// <remarks>
/// <para>
/// THE SOURCES, IN PRECEDENCE ORDER: the defaults written on <see cref="PineOptions"/>'s
/// properties, then an optional JSON file, then environment variables prefixed <c>PINE_</c>. Each
/// layer only overrides what it actually names, so a file containing one key changes one value.
/// </para>
/// <para>
/// WHY THE FILE IS NOT SHIPPED INSIDE THE ARTEFACT, which is the arrangement TD-27's row
/// ("`IOptions` + appsettings") would most naturally suggest. `packaging/package.sh` checksums
/// **every file** it ships and records the list in a manifest, so an `appsettings.json` inside the
/// package would be a file whose entire purpose is to be edited and whose edit invalidates the
/// artefact's own integrity claim. The file therefore lives in Pine's per-user data directory,
/// beside <c>vaults.json</c> and <c>logs/</c> — a directory the application already owns and
/// already writes to, outside anything that gets signed. It is optional, and Pine runs with no
/// configuration file at all, which is the normal case.
/// </para>
/// <para>
/// THE FILE'S LOCATION IS FIXED EVEN THOUGH THE DATA DIRECTORY IS CONFIGURABLE, and that is not an
/// oversight. <see cref="PineStorageOptions.DataDirectory"/> can be moved, but the file that moves
/// it has to be found before it is read, so it is always looked for under
/// <see cref="PineStorageOptions.DefaultDataDirectory"/>. Configuration that can hide itself is
/// configuration nobody can support.
/// </para>
/// <para>
/// ENVIRONMENT VARIABLES USE THE STANDARD DOUBLE-UNDERSCORE PATH:
/// <c>PINE_Pine__Ai__CategorisationConfidenceThreshold=0.5</c>. They exist for the case a config
/// file cannot serve — a CI run, a test host, a support session where writing a file into someone's
/// profile is the wrong ask.
/// </para>
/// </remarks>
public static class PineConfiguration
{
    /// <summary>The name of the optional configuration file.</summary>
    public const string FileName = "appsettings.json";

    /// <summary>The prefix every Pine environment variable carries.</summary>
    public const string EnvironmentVariablePrefix = "PINE_";

    /// <summary>The full path Pine looks for its optional configuration file at.</summary>
    public static string FilePath => Path.Combine(PineStorageOptions.DefaultDataDirectory(), FileName);

    /// <summary>
    /// Builds the configuration root from the optional file and the environment.
    /// </summary>
    /// <returns>The configuration, which may legitimately be empty.</returns>
    public static IConfigurationRoot Build()
    {
        var builder = new ConfigurationBuilder();

        // optional: true, reloadOnChange: false. Optional because running with no configuration is
        // the normal case. NOT reloading because a change to a KDF cost or a data directory
        // mid-session would be read by whichever component happened to look next, and half the
        // process would then disagree with the other half about where the vault lives. Restarting
        // is the honest reload.
        var file = FilePath;
        if (!string.IsNullOrWhiteSpace(file))
        {
            builder.AddJsonFile(file, optional: true, reloadOnChange: false);
        }

        builder.AddEnvironmentVariables(EnvironmentVariablePrefix);

        return builder.Build();
    }

    /// <summary>
    /// Binds and validates <see cref="PineOptions"/> from a configuration root.
    /// </summary>
    /// <param name="configuration">The configuration to bind from.</param>
    /// <param name="configure">
    /// Applied after binding and before validation, so a test or a head can override a value
    /// without writing a file.
    /// </param>
    /// <returns>The validated options.</returns>
    /// <exception cref="PineOptionsValidationException">
    /// The resulting configuration is unusable. Every problem is reported at once.
    /// </exception>
    public static PineOptions Bind(IConfiguration configuration, Action<PineOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new PineOptions();
        configuration.GetSection(PineOptions.SectionName).Bind(options);
        configure?.Invoke(options);

        // VALIDATION HAPPENS ONCE, HERE, AND THROWS WITH EVERY PROBLEM. The alternative -- letting
        // each component check its own values when it first reads them -- turns one editing mistake
        // into several unrelated-looking defects discovered weeks apart on different screens.
        var problems = options.Validate();
        if (problems.Count > 0)
        {
            throw new PineOptionsValidationException(problems);
        }

        return options;
    }

    /// <summary>
    /// Builds and binds in one step, for a head that wants no say in the matter.
    /// </summary>
    /// <param name="configure">Applied after binding, before validation.</param>
    /// <returns>The validated options.</returns>
    public static PineOptions Load(Action<PineOptions>? configure = null)
        => Bind(Build(), configure);

    /// <summary>
    /// A description of where the configuration came from, for the diagnostics panel and the log.
    /// </summary>
    /// <returns>One line per source that was actually present.</returns>
    /// <remarks>
    /// A configuration system whose effective values cannot be traced back to a source is a second
    /// mystery rather than a solved one. This says whether the file existed, not what it contained
    /// -- the values themselves may name a directory the user chose, and this string reaches a log.
    /// </remarks>
    public static IReadOnlyList<string> DescribeSources()
    {
        var sources = new List<string> { "defaults (compiled in)" };

        if (File.Exists(FilePath))
        {
            sources.Add($"file: {FileName} (present)");
        }
        else
        {
            sources.Add($"file: {FileName} (absent)");
        }

        sources.Add($"environment: {EnvironmentVariablePrefix}*");

        return sources;
    }
}
