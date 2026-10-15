using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace Pine.Tests.Unit.Build;

/// <summary>
/// A restore is reproducible (TD-129): every restorable project carries a committed
/// <c>packages.lock.json</c>, the lock files agree with the central versions, and CI restores in
/// locked mode.
/// </summary>
/// <remarks>
/// <para>
/// WHAT IS NOT LOCKED. The <c>net10.0-android</c> and <c>net10.0-ios</c> heads cannot be restored
/// without their workloads (NETSDK1147), so no lock file for them can be produced or verified on a
/// bare machine. They opt out in their own <c>.csproj</c>. The tests below make that opt-out the
/// ONLY exception: a desktop or test project without a lock file, or one that quietly opts out,
/// fails here.
/// </para>
/// <para>
/// LIMIT. These tests read files; they do not restore. That a lock file still matches the resolved
/// graph is enforced by the restore itself in CI (<c>NU1004</c> under <c>RestoreLockedMode</c>).
/// What is checked here is that the lock file exists, that its direct entries match what the
/// project and <c>Directory.Packages.props</c> say, and that locked mode is switched on.
/// </para>
/// </remarks>
public partial class BuildHygieneTests
{
    private static string[] SolutionProjects()
    {
        var root = RepositoryRoot();

        return XDocument
            .Load(Path.Combine(root, "Pine.slnx"))
            .Descendants("Project")
            .Select(element => element.Attribute("Path")!.Value)
            .Select(path => Path.GetFullPath(Path.Combine(root, path)))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsPlatformHead(string projectPath) =>
        XDocument.Load(projectPath)
            .Descendants("TargetFramework")
            .Any(element => element.Value.Trim().Contains('-', StringComparison.Ordinal));

    private static bool OptsOutOfLockFile(string projectPath) =>
        XDocument.Load(projectPath)
            .Descendants("RestorePackagesWithLockFile")
            .Any(element => string.Equals(element.Value.Trim(), "false", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void EveryRestorableProjectHasALockFile_AndOnlyThePlatformHeadsOptOut()
    {
        var failures = new List<string>();

        foreach (var project in SolutionProjects())
        {
            var lockFile = Path.Combine(Path.GetDirectoryName(project)!, "packages.lock.json");
            var head = IsPlatformHead(project);
            var optsOut = OptsOutOfLockFile(project);

            if (head && !optsOut)
            {
                failures.Add($"{Relative(project)} is a platform head and must opt out (it cannot be restored without its workload).");
            }

            if (!head && optsOut)
            {
                failures.Add($"{Relative(project)} opts out of the lock file but is restorable on a bare machine.");
            }

            if (!head && !File.Exists(lockFile))
            {
                failures.Add($"{Relative(project)} has no packages.lock.json beside it.");
            }
        }

        failures.Should().BeEmpty("TD-129: transitive versions must not float between two restores of one commit");
    }

    [Fact]
    public void LockFileDirectEntriesMatchTheProjectAndTheCentralVersions()
    {
        var central = CentralVersions();
        var failures = new List<string>();

        foreach (var project in SolutionProjects().Where(p => !IsPlatformHead(p)))
        {
            var lockFile = Path.Combine(Path.GetDirectoryName(project)!, "packages.lock.json");

            if (!File.Exists(lockFile))
            {
                continue; // reported by the test above
            }

            var direct = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            using (var document = JsonDocument.Parse(File.ReadAllText(lockFile)))
            {
                foreach (var framework in document.RootElement.GetProperty("dependencies").EnumerateObject())
                {
                    foreach (var package in framework.Value.EnumerateObject())
                    {
                        if (package.Value.GetProperty("type").GetString() == "Direct")
                        {
                            direct[package.Name] = package.Value.GetProperty("resolved").GetString()!;
                        }
                    }
                }
            }

            var referenced = PackageReferences(project).Select(r => r.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

            foreach (var name in referenced)
            {
                if (!direct.TryGetValue(name, out var resolved))
                {
                    failures.Add($"{Relative(lockFile)} does not list the direct package {name}; regenerate it.");
                }
                else if (central.TryGetValue(name, out var version) && !string.Equals(version, resolved, StringComparison.OrdinalIgnoreCase))
                {
                    failures.Add($"{Relative(lockFile)} resolves {name} to {resolved} but Directory.Packages.props says {version}; regenerate it.");
                }
            }

            foreach (var name in direct.Keys.Except(referenced, StringComparer.OrdinalIgnoreCase))
            {
                failures.Add($"{Relative(lockFile)} lists {name} as direct, but {Relative(project)} no longer references it; regenerate it.");
            }
        }

        failures.Should().BeEmpty("a stale lock file is a lock file that no longer describes the build");
    }

    [Fact]
    public void LockedModeIsOnInCi_AndTheCacheKeyIsHashedOverTheLockFiles()
    {
        var root = RepositoryRoot();

        var props = XDocument.Load(Path.Combine(root, "Directory.Build.props"));

        props.Descendants("RestorePackagesWithLockFile")
            .Should().Contain(e => e.Value.Trim() == "true", "lock files are generated for every project by default");

        props.Descendants("RestoreLockedMode")
            .Any(e => e.Value.Trim() == "true"
                && (e.Attribute("Condition")?.Value.Replace(" ", string.Empty).Contains("'$(CI)'=='true'", StringComparison.Ordinal) ?? false))
            .Should().BeTrue("CI restores must fail when the resolved graph differs from the lock file");

        var workflow = File.ReadAllLines(Path.Combine(root, ".github", "workflows", "dotnet-ci.yml"));
        var keys = workflow.Where(l => l.TrimStart().StartsWith("key: nuget-", StringComparison.Ordinal)).ToArray();

        keys.Should().NotBeEmpty();
        keys.Should().OnlyContain(
            l => l.Contains("**/packages.lock.json", StringComparison.Ordinal),
            "the NuGet cache key must move when a resolved version moves");
    }
}
