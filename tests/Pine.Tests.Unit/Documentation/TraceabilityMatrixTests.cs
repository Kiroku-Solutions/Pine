using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Pine.Tests.Unit.Documentation;

/// <summary>
/// Generates <c>docs/64-Traceability-Matrix-Generated.md</c> from the repository and fails when the
/// committed copy has drifted (TD-113).
/// </summary>
/// <remarks>
/// <para>
/// WHAT THE SOURCES OF TRUTH ARE. Requirement identities are the definitions in
/// <c>07-Functional-Requirements</c> (bold <c>FR-*</c> bullets) and <c>08-NonFunctional-Requirements</c>
/// (the <c>NFR-*</c> table rows). Evidence is whatever else in the tree NAMES the identifier: other
/// documents, files under <c>src/</c>, files under <c>tests/</c>. A struck-through definition
/// (<c>~~</c>) is reported as withdrawn or superseded, because that is how those documents retract
/// a requirement.
/// </para>
/// <para>
/// WHAT IT DOES NOT CLAIM. A mention is a LINK, not proof the requirement is met. Today almost no
/// code or test names a requirement identifier, so the matrix is mostly a list of requirements with
/// no machine-visible evidence, and it says so in its summary rather than hiding it. Closing that
/// gap means tagging tests with the identifiers they address, after which this matrix fills itself
/// in and the drift test keeps it honest.
/// </para>
/// <para>
/// TO REGENERATE after a legitimate change: set <c>PINE_REGENERATE_TRACEABILITY=1</c> and run this
/// class; it rewrites the file instead of failing, and the diff is the review.
/// </para>
/// </remarks>
public class TraceabilityMatrixTests
{
    private const string MatrixFileName = "64-Traceability-Matrix-Generated.md";

    private static readonly Regex FunctionalDefinition = new(
        @"^\s*-\s*(?<strike>~~)?\*\*(?<id>FR-[A-Z]+-\d+)\.\*\*",
        RegexOptions.Multiline | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    private static readonly Regex NonFunctionalDefinition = new(
        @"^\|\s*(?<id>NFR-[A-Z]+-\d+)\s*\|",
        RegexOptions.Multiline | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    private static readonly Regex AnyRequirementId = new(
        @"\b(?:NFR|FR)-[A-Z]+-\d+\b",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    private static readonly string[] SearchRoots = { "src", "tests" };

    private sealed record Requirement(string Id, string Source, bool Struck);

    [Fact]
    public void TheCommittedMatrixIsWhatTheRepositoryGenerates()
    {
        var path = Path.Combine(RepositoryRoot(), "docs", MatrixFileName);
        var generated = Generate();

        if (Environment.GetEnvironmentVariable("PINE_REGENERATE_TRACEABILITY") == "1")
        {
            File.WriteAllText(path, generated, new UTF8Encoding(false));
            return;
        }

        File.Exists(path).Should().BeTrue(
            $"docs/{MatrixFileName} must be committed; generate it with PINE_REGENERATE_TRACEABILITY=1");

        Normalise(File.ReadAllText(path)).Should().Be(
            generated,
            "the traceability matrix is generated from docs 07/08 and the tree, so it is out of date: re-run "
            + "this test class with PINE_REGENERATE_TRACEABILITY=1 and commit the diff");
    }

    [Fact]
    public void EveryRequirementIdentifierIsDefinedExactlyOnce()
    {
        var requirements = Requirements();

        requirements.Should().NotBeEmpty();
        requirements.GroupBy(r => r.Id).Where(g => g.Count() > 1).Select(g => g.Key).Should().BeEmpty(
            "a duplicated definition would make the matrix ambiguous");
        requirements.Count(r => r.Id.StartsWith("FR-", StringComparison.Ordinal)).Should().BeGreaterThan(100);
        requirements.Count(r => r.Id.StartsWith("NFR-", StringComparison.Ordinal)).Should().BeGreaterThan(50);
    }

    [Fact]
    public void EveryRequirementIdentifierCitedInCodeOrTestsIsDefined()
    {
        var root = RepositoryRoot();
        var defined = Requirements().Select(r => r.Id).ToHashSet(StringComparer.Ordinal);

        var dangling = SourceAndTestFiles(root)
            .SelectMany(file => AnyRequirementId.Matches(File.ReadAllText(file))
                .Select(match => (File: Rel(root, file), match.Value)))
            .Where(hit => !defined.Contains(hit.Value))
            .Select(hit => $"{hit.File} cites {hit.Value}")
            .Distinct()
            .ToList();

        dangling.Should().BeEmpty("a citation of a requirement that does not exist is a broken trace link");
    }

    private static string Generate()
    {
        var root = RepositoryRoot();
        var requirements = Requirements();

        var docMentions = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        var codeMentions = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        var testMentions = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);

        var excludedDocs = new[] { "07-", "08-", "19-", MatrixFileName[..3] };

        foreach (var file in Directory.GetFiles(Path.Combine(root, "docs"), "*.md", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(file);

            if (excludedDocs.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal))
                && Path.GetDirectoryName(file) == Path.Combine(root, "docs"))
            {
                continue;
            }

            var label = Path.GetDirectoryName(file) == Path.Combine(root, "docs")
                ? name[..2]
                : Rel(root, file);

            Collect(docMentions, label, File.ReadAllText(file));
        }

        foreach (var file in SourceAndTestFiles(root))
        {
            var relative = Rel(root, file);
            Collect(relative.StartsWith("src/", StringComparison.Ordinal) ? codeMentions : testMentions, relative, File.ReadAllText(file));
        }

        var builder = new StringBuilder();
        builder.Append(Header);

        var functional = requirements.Where(r => r.Id.StartsWith("FR-", StringComparison.Ordinal)).ToList();
        var nonFunctional = requirements.Where(r => r.Id.StartsWith("NFR-", StringComparison.Ordinal)).ToList();

        bool Active(Requirement r) => !r.Struck;
        bool Evidenced(Requirement r) => codeMentions.ContainsKey(r.Id) || testMentions.ContainsKey(r.Id);

        builder.Append("\n## 1. Summary\n\n");
        builder.Append("| Measure | FR | NFR |\n|---|---|---|\n");
        builder.Append(CultureInfo.InvariantCulture, $"| Defined | {functional.Count} | {nonFunctional.Count} |\n");
        builder.Append(CultureInfo.InvariantCulture, $"| Struck through (withdrawn or superseded) | {functional.Count(r => r.Struck)} | {nonFunctional.Count(r => r.Struck)} |\n");
        builder.Append(CultureInfo.InvariantCulture, $"| Active and named by at least one file under `src/` or `tests/` | {functional.Count(r => Active(r) && Evidenced(r))} | {nonFunctional.Count(r => Active(r) && Evidenced(r))} |\n");
        builder.Append(CultureInfo.InvariantCulture, $"| Active with **no** machine-visible code or test reference | {functional.Count(r => Active(r) && !Evidenced(r))} | {nonFunctional.Count(r => Active(r) && !Evidenced(r))} |\n");

        builder.Append("\nA requirement in the last row is not necessarily unmet; it is a requirement for which this repository ");
        builder.Append("cannot show a link. Tag the tests that address it with its identifier and it moves.\n");

        Table(builder, "## 2. Functional requirements", functional, docMentions, codeMentions, testMentions);
        Table(builder, "## 3. Non-functional requirements", nonFunctional, docMentions, codeMentions, testMentions);

        return builder.ToString();
    }

    private static void Table(
        StringBuilder builder,
        string heading,
        List<Requirement> rows,
        Dictionary<string, SortedSet<string>> docs,
        Dictionary<string, SortedSet<string>> code,
        Dictionary<string, SortedSet<string>> tests)
    {
        builder.Append('\n').Append(heading).Append("\n\n");
        builder.Append("| ID | Defined in | State | Other documents | Code | Tests |\n|---|---|---|---|---|---|\n");

        foreach (var row in rows.OrderBy(r => r.Id, StringComparer.Ordinal))
        {
            builder.Append(CultureInfo.InvariantCulture, $"| {row.Id} | {row.Source} | {(row.Struck ? "struck through" : "active")} | ")
                .Append(Cell(docs, row.Id)).Append(" | ")
                .Append(Cell(code, row.Id)).Append(" | ")
                .Append(Cell(tests, row.Id)).Append(" |\n");
        }
    }

    private static string Cell(Dictionary<string, SortedSet<string>> map, string id) =>
        map.TryGetValue(id, out var set) ? string.Join(", ", set.Select(s => $"`{s}`")) : "—";

    private static void Collect(Dictionary<string, SortedSet<string>> map, string label, string text)
    {
        foreach (var id in AnyRequirementId.Matches(text).Select(m => m.Value).Distinct())
        {
            if (!map.TryGetValue(id, out var set))
            {
                set = new SortedSet<string>(StringComparer.Ordinal);
                map[id] = set;
            }

            set.Add(label);
        }
    }

    private static List<Requirement> Requirements()
    {
        var root = RepositoryRoot();
        var result = new List<Requirement>();

        var functional = Normalise(File.ReadAllText(Path.Combine(root, "docs", "07-Functional-Requirements.md")));

        foreach (Match match in FunctionalDefinition.Matches(functional))
        {
            var lineEnd = functional.IndexOf('\n', match.Index);
            var line = functional[match.Index..(lineEnd < 0 ? functional.Length : lineEnd)];
            result.Add(new Requirement(match.Groups["id"].Value, "`07`", line.Contains("~~", StringComparison.Ordinal)));
        }

        var nonFunctional = Normalise(File.ReadAllText(Path.Combine(root, "docs", "08-NonFunctional-Requirements.md")));

        foreach (Match match in NonFunctionalDefinition.Matches(nonFunctional))
        {
            var lineEnd = nonFunctional.IndexOf('\n', match.Index);
            var line = nonFunctional[match.Index..(lineEnd < 0 ? nonFunctional.Length : lineEnd)];
            result.Add(new Requirement(match.Groups["id"].Value, "`08`", line.Contains("~~", StringComparison.Ordinal)));
        }

        return result;
    }

    private static IEnumerable<string> SourceAndTestFiles(string root) =>
        SearchRoots
            .SelectMany(top => Directory.GetFiles(Path.Combine(root, top), "*.cs", SearchOption.AllDirectories))
            .Where(path =>
            {
                var relative = Rel(root, path);
                return !relative.Contains("/obj/", StringComparison.Ordinal)
                    && !relative.Contains("/bin/", StringComparison.Ordinal)
                    && !relative.Contains("/TestResults/", StringComparison.Ordinal);
            })
            .OrderBy(path => path, StringComparer.Ordinal);

    private static string Rel(string root, string path) =>
        Path.GetRelativePath(root, path).Replace('\\', '/');

    private static string Normalise(string text) => text.Replace("\r\n", "\n");

    private const string Header =
        "# 64 — Traceability Matrix (generated)\n"
        + "\n"
        + "| Field            | Value                                                                                |\n"
        + "|------------------|--------------------------------------------------------------------------------------|\n"
        + "| Document ID      | `PINE-DOC-64-TRACEABILITY-MATRIX-GENERATED`                                          |\n"
        + "| Status           | Non-normative — generated file; do not edit by hand                                  |\n"
        + "| Reconciled       | 2026-10-11 · Generated by `TraceabilityMatrixTests` from the definitions in `07` and `08` and from which files under `docs/`, `src/` and `tests/` name each identifier. A link is a mention, not proof the requirement is met. `19-Traceability-Matrix` remains the aspirational specification (TD-113). |\n"
        + "| Owner            | Engineering Review Board                                                             |\n"
        + "| Created          | 2026-10-11                                                                           |\n"
        + "| Last Updated     | Regenerated by the test; see git history                                             |\n"
        + "| Related Documents| `07-Functional-Requirements`, `08-NonFunctional-Requirements`, `19-Traceability-Matrix` |\n"
        + "\n"
        + "Regenerate: `PINE_REGENERATE_TRACEABILITY=1 dotnet test tests/Pine.Tests.Unit --filter TraceabilityMatrixTests`.\n"
        + "A normal run fails when this file differs from what the repository generates.\n";

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

        throw new DirectoryNotFoundException("Could not find Pine.slnx above " + AppContext.BaseDirectory);
    }
}
