using System;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Pine.Ui.Avalonia.Composition;

namespace Pine.Tests.Unit.Composition;

/// <summary>
/// The real composition root, pointed at a data directory that belongs to this test process.
/// </summary>
/// <remarks>
/// WHY THE TESTS DO NOT BUILD <c>PineCompositionRoot.Build(profile)</c> DIRECTLY ANY MORE (independent review of
/// post-audit plan item 3.1). The default data directory is the developer's real <c>%LOCALAPPDATA%\Pine</c>, and
/// resolving the handlers builds a database context, whose factory reads the installation's device id — so
/// the composition tests minted a <c>device-id</c> in the real data directory of every machine that ran them,
/// CI runners included. The graph under test is the same; only where it keeps its files differs.
/// </remarks>
internal static class TestContainer
{
    /// <summary>A directory unique to this test process, under the system's temporary directory.</summary>
    public static readonly string DataDirectory =
        Path.Combine(Path.GetTempPath(), "pine-composition-tests", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>Builds the head's real container with <see cref="DataDirectory"/> as its data directory.</summary>
    /// <param name="profile">The head.</param>
    /// <returns>The container.</returns>
    public static ServiceProvider Build(PineHeadProfile profile) =>
        PineCompositionRoot.Build(profile, options => options.Storage.DataDirectory = DataDirectory);
}
