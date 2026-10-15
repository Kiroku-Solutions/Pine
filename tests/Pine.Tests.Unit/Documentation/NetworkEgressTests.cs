using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Pine.Tests.Unit.Documentation;

/// <summary>
/// Pine performs no network I/O at all. This is the test that makes that a property rather than an
/// intention (ADR-0012).
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS EXISTS. ADR-0012 was written in iteration 20 from the code, and its own risk table
/// recorded the gap this file closes: <i>"A future dependency introduces network I/O unnoticed —
/// currently only a grep would catch it. There is no test asserting the absence."</i> The audit's
/// §11 calls the absence of egress the codebase's biggest strength, seven documents cite the ADR,
/// and the README now tells a user the property holds. All of that rested on nobody having typed
/// <c>new HttpClient()</c> yet.
/// </para>
/// <para>
/// WHAT IT CHECKS, AND WHY IT IS A TEXT SCAN. It reads the source and the project files rather than
/// the compiled assemblies, deliberately. A reflection-based check would see only what was
/// referenced, would need every head to be loadable in the test process (four of the six are not),
/// and would say nothing about a package added to a <c>.csproj</c>. Two coarse gates, both of which
/// a person adding networking would have to notice and delete:
/// </para>
/// <list type="number">
/// <item>No file under <c>src/</c> may name a networking type. Naming one in a comment fails too —
/// that is a false positive worth having, because the alternative is a scanner that can be talked
/// past by moving a string.</item>
/// <item>No project under <c>src/</c> may reference a package whose name carries a networking or
/// telemetry marker.</item>
/// </list>
/// <para>
/// WHAT IT DOES NOT CATCH, STATED PLAINLY: reflection built from string fragments, a native library
/// opening its own socket (PaddleInference and ONNX Runtime are unaudited native code), and an
/// <c>Process.Start</c> of something else. It raises the cost of adding egress from zero to
/// "delete a test that says why it exists", which is the whole of what a source scan can do.
/// </para>
/// </remarks>
public class NetworkEgressTests
{
    /// <summary>
    /// Types and namespaces that only appear in code that talks to a network. Matched as plain
    /// substrings, ordinal: the bare word <c>Socket</c> is deliberately NOT on the list, because it
    /// appears in unrelated prose, and every entry here is specific enough that a substring hit is a
    /// real hit.
    /// </summary>
    private static readonly string[] BannedIdentifiers =
    {
        "HttpClient",
        "HttpMessageHandler",
        "HttpRequestMessage",
        "HttpListener",
        "WebClient",
        "WebRequest",
        "SmtpClient",
        "TcpClient",
        "TcpListener",
        "UdpClient",
        "ClientWebSocket",
        "System.Net.Http",
        "System.Net.Sockets",
        "System.Net.WebSockets",
        "System.Net.Mail",
        "Dns.GetHost",
    };

    /// <summary>
    /// Package-name fragments that mean networking, telemetry or crash reporting. Matched
    /// case-insensitively against the <c>Include</c> of every <c>PackageReference</c>.
    /// </summary>
    private static readonly string[] BannedPackageFragments =
    {
        "Http",
        "Sockets",
        "Grpc",
        "RestSharp",
        "Refit",
        "SignalR",
        "Sentry",
        "ApplicationInsights",
        "AppCenter",
        "Bugsnag",
        "Segment",
        "Mixpanel",
        "Amplitude",
        "Firebase",
        "Telemetry",
        "Analytics",
    };

    [Fact]
    public void NoSourceFileNamesANetworkingType()
    {
        var offences = new List<string>();

        foreach (var file in SourceFiles())
        {
            var lines = File.ReadAllLines(file);

            for (var index = 0; index < lines.Length; index++)
            {
                foreach (var identifier in BannedIdentifiers)
                {
                    if (lines[index].Contains(identifier, StringComparison.Ordinal))
                    {
                        offences.Add(
                            $"{Path.GetRelativePath(RepositoryRoot(), file)}:{index + 1} names '{identifier}'");
                    }
                }
            }
        }

        offences.Should().BeEmpty(
            "ADR-0012 states that Pine performs no network I/O at all, the README tells users so, "
            + "and the audit calls it the codebase's biggest strength. If network access is now "
            + "wanted, that is a decision to record in an ADR that supersedes 0012 — and then this "
            + "test is deleted deliberately, which is the point of it existing");
    }

    [Fact]
    public void NoProjectReferencesANetworkingOrTelemetryPackage()
    {
        var reference = new Regex(
            "PackageReference\\s+Include=\"([^\"]+)\"",
            RegexOptions.None,
            TimeSpan.FromSeconds(5));

        var offences = new List<string>();

        foreach (var project in ProjectFiles())
        {
            foreach (Match match in reference.Matches(File.ReadAllText(project)))
            {
                var package = match.Groups[1].Value;

                foreach (var fragment in BannedPackageFragments)
                {
                    if (package.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                    {
                        offences.Add(
                            $"{Path.GetRelativePath(RepositoryRoot(), project)} references '{package}'");
                    }
                }
            }
        }

        offences.Should().BeEmpty(
            "a telemetry or HTTP package in a project file is how the absence of egress stops being "
            + "true without anyone writing a line of code");
    }

    /// <summary>
    /// The scan is worthless if it walks an empty tree, which is the failure mode a source-text test
    /// is most likely to have and least likely to show.
    /// </summary>
    [Fact]
    public void TheScanActuallyReadsTheSourceTree()
    {
        SourceFiles().Should().HaveCountGreaterThan(
            100,
            "src/ holds well over a hundred .cs files; a smaller number means the walk is not "
            + "reaching them and both assertions above are passing vacuously");

        ProjectFiles().Should().HaveCountGreaterThanOrEqualTo(
            9,
            "there are nine projects under src/ — Core, Application, Infrastructure, Cli, the shared "
            + "UI and four OS heads. It was fourteen until iteration 27 deleted the two AI and two "
            + "plugin projects, which had held zero .cs files each (TD-38), and ten until iteration "
            + "31 deleted the Browser head (Phase 3, item 1). This count is a deliberate tripwire "
            + "and it has now done its job twice: both deletions had to come past this assertion "
            + "rather than past nobody. Lowering it is the deliberate act it is meant to require");
    }

    private static List<string> SourceFiles()
        => Enumerate("*.cs");

    private static List<string> ProjectFiles()
        => Enumerate("*.csproj");

    private static List<string> Enumerate(string pattern)
    {
        var source = Path.Combine(RepositoryRoot(), "src");

        return Directory
            .EnumerateFiles(source, pattern, SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Pine.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not find Pine.slnx above '{AppContext.BaseDirectory}'. This test reads the "
            + "repository's own source and cannot run outside it.");
    }
}
