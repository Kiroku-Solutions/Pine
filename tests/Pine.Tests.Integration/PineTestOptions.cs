using Microsoft.Extensions.Options;
using Pine.Core.Configuration;

namespace Pine.Tests.Integration;

/// <summary>
/// The shipped default options, wrapped for the constructors that take <c>IOptions&lt;T&gt;</c>.
/// </summary>
/// <remarks>
/// A test that is not ABOUT configuration should say so by using the defaults, and should not have
/// to spell out an options wrapper to do it. Tests that are about a specific value construct their
/// own instead of reaching for these.
/// </remarks>
internal static class PineTestOptions
{
    /// <summary>Vault creation parameters and backup retention, at their defaults.</summary>
    public static IOptions<PineSecurityOptions> Security { get; } = Options.Create(new PineSecurityOptions());

    /// <summary>AI thresholds, at their defaults.</summary>
    public static IOptions<PineAiOptions> Ai { get; } = Options.Create(new PineAiOptions());
}
