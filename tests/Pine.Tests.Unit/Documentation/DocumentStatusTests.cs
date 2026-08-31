using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace Pine.Tests.Unit.Documentation;

/// <summary>
/// Every document in <c>docs/</c> must say, in its own header, whether it describes software that
/// exists (TD-39).
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS IS A TEST AND NOT AN AFTERNOON'S EDITING. §6.12 of the audit diagnosed the documentation
/// problem correctly and then prescribed the wrong remedy: "marking every unimplemented document
/// <c>Status: Aspirational — not implemented</c> in its header would recover most of the value in an
/// afternoon." The diagnosis in the same paragraph is the part that matters — *"there is no step in
/// the workflow that reconciles a specification with its implementation"* — and an afternoon of
/// editing is not a step in a workflow. It is a one-off that decays from the next commit onward,
/// which is exactly how fifty-eight documents came to sit at <c>Draft — pending review</c>, dated
/// 2026-07-08, through twenty iterations of remediation that made several of them true and several
/// others further from true.
/// </para>
/// <para>
/// So the remedy is the missing step. A document may say anything it likes about the future; it may
/// not stay silent about whether it is describing the present. These tests do not judge whether a
/// document is ACCURATE — no test can — they enforce that it has been classified, that the
/// classification comes from a closed vocabulary, and that the classification carries a date and a
/// reason. A new document with no status fails. A document restored to <c>Draft — pending review</c>
/// fails. That is a workflow step.
/// </para>
/// <para>
/// WHAT THIS DELIBERATELY DOES NOT CHECK. It cannot verify that a document marked
/// <c>Implemented</c> is telling the truth; that judgement lives in the <c>Reconciled</c> line's
/// evidence and in review. What it removes is the failure mode that actually occurred here, which is
/// not a wrong status but the total absence of one.
/// </para>
/// </remarks>
public class DocumentStatusTests
{
    /// <summary>
    /// The closed vocabulary. A status must BEGIN with one of these, so a document may append its own
    /// qualification after it — "Partially implemented — the write path only, see TD-52" — without
    /// escaping the classification.
    /// </summary>
    /// <remarks>
    /// Four values, chosen so that each one commits to something a reader can act on:
    /// <list type="bullet">
    /// <item><c>Implemented</c> — the code does what this document says. Cite where in Reconciled.</item>
    /// <item><c>Partially implemented</c> — some clauses hold and some do not. Name which.</item>
    /// <item><c>Aspirational</c> — nothing in the tree does this. It may still be a good plan.</item>
    /// <item><c>Non-normative</c> — the document does not specify software behaviour: a vision, a
    /// glossary, a persona set, a backlog, a working record. It cannot be implemented or
    /// unimplemented, and forcing one of the three labels above onto it would be a category error
    /// dressed as rigour.</item>
    /// </list>
    /// There is deliberately no "Draft" and no "pending review": both are statements about the
    /// document's editorial state, and a reader deciding whether to trust a specification needs a
    /// statement about the SOFTWARE.
    /// </remarks>
    private static readonly string[] AllowedStatusPrefixes =
    {
        "Implemented",
        "Partially implemented",
        "Aspirational",
        "Non-normative",
    };

    private const string BannedStatus = "Draft — pending review";

    /// <summary>How far into a file the header table may begin. Generous; every current file uses 6.</summary>
    private const int HeaderLineBudget = 30;

    [Fact]
    public void EveryDocumentDeclaresAStatus()
    {
        var missing = Documents()
            .Where(document => document.Status is null)
            .Select(document => document.Name)
            .ToList();

        missing.Should().BeEmpty(
            "a document that does not say whether it describes existing software is the defect TD-39 names");
    }

    [Fact]
    public void EveryStatusComesFromTheClosedVocabulary()
    {
        var unknown = Documents()
            .Where(document => document.Status is not null)
            .Where(document => !AllowedStatusPrefixes.Any(allowed =>
                document.Status!.StartsWith(allowed, StringComparison.Ordinal)))
            .Select(document => $"{document.Name}: {document.Status}")
            .ToList();

        unknown.Should().BeEmpty(
            "an open vocabulary is not a classification; it is a place to write a new evasion");
    }

    /// <summary>
    /// The exact string all fifty-eight specifications carried, pinned so the regression is loud
    /// rather than gradual.
    /// </summary>
    [Fact]
    public void NoDocumentSaysDraftPendingReviewAnyMore()
    {
        var stale = Documents()
            .Where(document => document.Status?.Contains(BannedStatus, StringComparison.Ordinal) == true)
            .Select(document => document.Name)
            .ToList();

        stale.Should().BeEmpty(
            "\"Draft — pending review\" describes the document's editorial state and says nothing "
            + "about whether the software exists, which is the only question a reader has");
    }

    /// <summary>
    /// A status with no date and no evidence is an assertion. The <c>Reconciled</c> row is what makes
    /// it a claim someone can check and, later, notice has gone stale.
    /// </summary>
    [Fact]
    public void EveryDocumentRecordsWhenItsStatusWasLastReconciled()
    {
        var missing = Documents()
            .Where(document => document.Reconciled is null)
            .Select(document => document.Name)
            .ToList();

        missing.Should().BeEmpty(
            "a classification with no date cannot be seen to have decayed");
    }

    /// <summary>
    /// Every <c>ADR-NNNN</c> cited by source code must resolve to a file in <c>docs/adr/</c> (TD-40).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The register in <c>25-ADR</c> §11 seeds twenty ADRs and marks every one of them
    /// <c>Accepted</c>; seventeen have no file. A citation into that set therefore looks like a
    /// reference and resolves to nothing, which is how <c>Argon2idKeyDerivationService</c> came to
    /// cite <c>ADR-0013</c> for eight months as the authority for the vault's key derivation while
    /// <c>ADR-0013</c> was an eight-word table row.
    /// </para>
    /// <para>
    /// This checks the citations that live in CODE, not the ones in prose. Documents cite ADRs
    /// aspirationally — that is what a specification does, and now that each document carries a
    /// status a reader can weigh it. Code does not get that latitude: a comment that points a
    /// maintainer at a decision record has either got one or is misleading them.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryAdrCitedInSourceHasAFile()
    {
        var root = RepositoryRoot();
        var adrDirectory = Path.Combine(root, "docs", "adr");

        Directory.Exists(adrDirectory).Should().BeTrue("docs/adr/ is where ADR files live");

        var existing = Directory
            .GetFiles(adrDirectory, "*.md", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => name is not null && name.Length >= 4)
            .Select(name => name![..4])
            .ToHashSet(StringComparer.Ordinal);

        var citation = new System.Text.RegularExpressions.Regex(
            @"ADR-(\d{4})",
            System.Text.RegularExpressions.RegexOptions.None,
            TimeSpan.FromSeconds(5));

        var dangling = new List<string>();

        foreach (var file in SourceFiles(Path.Combine(root, "src")))
        {
            var text = File.ReadAllText(file);

            foreach (System.Text.RegularExpressions.Match match in citation.Matches(text))
            {
                var serial = match.Groups[1].Value;

                if (!existing.Contains(serial))
                {
                    dangling.Add($"{Path.GetRelativePath(root, file)} cites ADR-{serial}");
                }
            }
        }

        dangling.Should().BeEmpty(
            "a code comment that points a maintainer at a decision record which does not exist is "
            + "worse than no comment: it costs them the search before it costs them the answer");
    }

    private static IEnumerable<string> SourceFiles(string sourceRoot)
        => Directory
            .EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    // -----------------------------------------------------------------------------------------

    private sealed record DocumentHeader(string Name, string? Status, string? Reconciled);

    private static List<DocumentHeader> Documents()
    {
        var directory = DocsDirectory();

        var files = Directory
            .GetFiles(directory, "*.md", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        // A silently empty set would make every assertion above pass vacuously, which is the exact
        // shape of green-that-means-nothing this suite exists to avoid.
        files.Should().NotBeEmpty("docs/ was located but contains no markdown files");

        return files.Select(path =>
        {
            var lines = File.ReadAllLines(path).Take(HeaderLineBudget).ToArray();

            return new DocumentHeader(
                Path.GetFileName(path),
                FieldValue(lines, "Status"),
                FieldValue(lines, "Reconciled"));
        }).ToList();
    }

    /// <summary>
    /// Reads <c>| Field | Value |</c> out of a markdown header table, tolerating the bold spelling
    /// (<c>| **Status** | … |</c>) that files 58-61 use.
    /// </summary>
    private static string? FieldValue(IReadOnlyList<string> lines, string field)
    {
        foreach (var line in lines)
        {
            var cells = line.Split('|');

            if (cells.Length < 3)
            {
                continue;
            }

            var name = cells[1].Trim().Trim('*').Trim();

            if (string.Equals(name, field, StringComparison.OrdinalIgnoreCase))
            {
                var value = cells[2].Trim().Trim('*').Trim();
                return value.Length == 0 ? null : value;
            }
        }

        return null;
    }

    /// <summary>
    /// Walks up from the test binary to the repository root, identified by the solution file.
    /// </summary>
    /// <remarks>
    /// It THROWS rather than skipping when the root cannot be found. A skip would turn "these tests
    /// could not run" into a green suite, and the whole subject of this file is claims that are
    /// green without being true.
    /// </remarks>
    private static string DocsDirectory()
    {
        var docs = Path.Combine(RepositoryRoot(), "docs");

        if (!Directory.Exists(docs))
        {
            throw new DirectoryNotFoundException(
                $"Found the repository root but it has no docs/ directory: '{docs}'.");
        }

        return docs;
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
            + "be located. These tests read the repository's own documentation and cannot run outside it.");
    }
}
