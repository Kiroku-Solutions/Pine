using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace Pine.Tests.Unit.Build;

/// <summary>
/// No test harness in this repository clears every connection pool in the process (TD-140).
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS IS A TEST AND NOT A COMMENT. <c>SqliteConnection.ClearAllPools()</c> is the natural
/// thing to reach for in a teardown: the docs present it as the way to let go of a database file,
/// and on Windows a pooled handle is what stops the temp directory being deleted. It is also
/// PROCESS-WIDE. xUnit runs test classes in parallel, so one class's teardown disposes the pooled
/// handles of every other class running at that moment, and the next <c>Open()</c> in those classes
/// can be handed a connection whose handle is already gone:
/// <c>SqliteConnection.Open()</c> → <c>SqliteConnectionInternal.Close()</c> →
/// <c>SqliteConnection.Deactivate()</c> → <c>ObjectDisposedException</c> on a
/// <c>SQLitePCL.sqlite3</c>. That is TD-140, and it cost a re-run of the integration suite in four
/// separate iterations before the stack was captured.
/// </para>
/// <para>
/// THE FIX IS ONE CHARACTER AWAY FROM THE DEFECT — <c>ClearPool(connection)</c> clears the pool for
/// that connection's connection string and no other — which is exactly why a guard is worth having.
/// Nothing else in the repository would notice the difference: removing this test and putting
/// <c>ClearAllPools</c> back leaves the whole suite green most of the time, which is the property
/// that let the defect live for twelve iterations.
/// </para>
/// <para>
/// It scans <c>src/</c> as well as <c>tests/</c>. The first version scanned only the harnesses, on
/// the reasoning that production has no parallel test classes — which the independent review
/// answered: production code RUNS INSIDE the integration test process, so a <c>ClearAllPools</c>
/// reintroduced in <c>src/</c> reproduces TD-140 exactly, and is a product defect besides. That is
/// not hypothetical either: the same mistake was found and fixed in <c>VaultRestoreService</c>
/// while closing plan item 1.1.
/// </para>
/// </remarks>
public class TestHarnessPoolingTests
{
    [Fact]
    public void NoTestHarnessClearsEveryConnectionPoolInTheProcess()
    {
        var root = RepositoryRoot();
        var scanned = new[] { Path.Combine(root, "tests"), Path.Combine(root, "src") };

        var offenders = scanned
            .SelectMany(directory => Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories))
            .Where(file => !IsBuildOutput(file))
            // This file names the method in its own prose, and asserting on itself would be
            // unkillable: it would fail whatever the harnesses do.
            .Where(file => !Path.GetFileName(file).Equals("TestHarnessPoolingTests.cs", StringComparison.Ordinal))
            .Where(file => CallsClearAllPools(File.ReadAllLines(file)))
            .Select(file => Path.GetRelativePath(root, file))
            .OrderBy(file => file, StringComparer.Ordinal)
            .ToArray();

        offenders.Should().BeEmpty(
            "SqliteConnection.ClearAllPools() is process-wide and xUnit runs test classes in parallel, "
            + "so it disposes pooled handles belonging to other classes (TD-140). Clear this harness's "
            + "own connection string instead: SqliteConnection.ClearPool(connectionForThisDatabase). "
            + "Found in: {0}",
            string.Join(", ", offenders));
    }

    /// <summary>
    /// True when the file CALLS it, as opposed to naming it in prose.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The harnesses this row fixed all explain the defect in their own doc comments, so a plain
    /// substring search over the file would fail on exactly the files that are CORRECT -- which
    /// would make the guard useless and, worse, teach the next reader to delete the explanation.
    /// Comments are therefore stripped before the search.
    /// </para>
    /// <para>
    /// THE FIRST VERSION OF THIS STRIPPER HAD A FALSE NEGATIVE AND ITS OWN COMMENT DENIED IT. On
    /// seeing <c>/*</c> it entered block-comment mode without checking for a <c>*/</c> later on the
    /// same line, so a single one-line block comment switched scanning off for the whole rest of the
    /// file. The independent review wrote
    /// <c>/* TD-140 */ SqliteConnection.ClearAllPools();</c> into <c>TempVault</c> -- the real
    /// defect, reintroduced, compiling, and natural-looking -- and this test stayed green. That
    /// mutation is now in the falsification set.
    /// </para>
    /// <para>
    /// WHAT THIS STILL DOES NOT CATCH, said plainly rather than implied: it is a text scan, so
    /// <c>"ClearAll" + "Pools"</c>, an invocation by reflection, or a helper living outside the
    /// directories scanned below all evade it. It raises the cost of reintroducing the defect by
    /// accident, which is how it was introduced; it is not a proof that the defect cannot return.
    /// </para>
    /// </remarks>
    private static bool CallsClearAllPools(string[] lines)
    {
        var inBlockComment = false;

        foreach (var line in lines)
        {
            var code = string.Empty;
            var rest = line;

            while (rest.Length > 0)
            {
                if (inBlockComment)
                {
                    var close = rest.IndexOf("*/", StringComparison.Ordinal);
                    if (close < 0)
                    {
                        rest = string.Empty;
                        break;
                    }

                    rest = rest[(close + 2)..];
                    inBlockComment = false;
                    continue;
                }

                var lineComment = rest.IndexOf("//", StringComparison.Ordinal);
                var blockStart = rest.IndexOf("/*", StringComparison.Ordinal);

                if (lineComment >= 0 && (blockStart < 0 || lineComment < blockStart))
                {
                    code += rest[..lineComment];
                    rest = string.Empty;
                    break;
                }

                if (blockStart >= 0)
                {
                    code += rest[..blockStart];
                    rest = rest[(blockStart + 2)..];
                    inBlockComment = true;
                    continue;
                }

                code += rest;
                rest = string.Empty;
            }

            if (code.Contains("ClearAllPools", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsBuildOutput(string file)
    {
        var segments = file.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Contains("bin") || segments.Contains("obj");
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
            $"Could not find Pine.slnx above '{AppContext.BaseDirectory}', so the repository could not "
            + "be located. This test reads the repository's own test sources and cannot run outside it.");
    }
}
