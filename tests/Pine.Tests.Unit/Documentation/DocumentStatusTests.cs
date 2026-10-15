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
/// SCOPE (TD-112): the rule covers design, product and architecture documents - every top-level
/// <c>docs/*.md</c> plus the root README, STATUS, PRODUCT and CHANGELOG. GitHub/AI meta-files
/// (CONTRIBUTING, CODE_OF_CONDUCT, TRADEMARK, GEMINI, <c>docs/superpowers/</c>) are formally exempt;
/// see <c>docs/24-Contributing</c> section 14. The exemption does not touch <c>docs/*</c>.
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
    /// <item><c>Withdrawn</c> — this will not be built, and the decision is recorded in an ADR. See
    /// the remarks below on why this had to be a fifth value rather than a shade of the third.</item>
    /// </list>
    /// There is deliberately no "Draft" and no "pending review": both are statements about the
    /// document's editorial state, and a reader deciding whether to trust a specification needs a
    /// statement about the SOFTWARE.
    /// <para>
    /// WHY <c>Withdrawn</c> EXISTS, AND WHY ITERATION 27's ARGUMENT AGAINST IT WAS WRONG. §12.56
    /// considered adding this value for the plugin documents and rejected it, reasoning that
    /// <c>Aspirational</c> already covered the case because deleting the empty projects made the
    /// documents "more precisely `Aspirational` than they were". The premise is true and the
    /// conclusion does not follow. <c>Aspirational</c> is defined immediately above as "nothing in
    /// the tree does this. **It may still be a good plan**" — it is a statement about the SCAFFOLDING
    /// plus an open question about the PLAN. Retiring a document answers that question in the
    /// negative, and no amount of precision about the scaffolding can express it. A reader who opens
    /// a 646-line specification marked <c>Aspirational</c> is entitled to conclude somebody still
    /// intends to build it; after ADR-0021 that conclusion is false, and a vocabulary that cannot
    /// say so is a vocabulary that misleads. The fifth value was not evasion — it was the missing
    /// word.
    /// </para>
    /// </remarks>
    private static readonly string[] AllowedStatusPrefixes =
    {
        "Implemented",
        "Partially implemented",
        "Aspirational",
        "Non-normative",
        "Withdrawn",
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
    /// A document may only be <c>Withdrawn</c> if an ADR file says so.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS IS WHAT STOPS THE FIFTH VALUE BECOMING THE EVASION THE OTHER FOUR WERE DESIGNED AGAINST.
    /// <c>Aspirational</c> costs nothing to write and commits nobody; that is appropriate, because
    /// it is the honest label for a plan not yet built. <c>Withdrawn</c> makes the opposite claim —
    /// that a decision was taken — and a claim about a decision with no record of the decision is
    /// exactly the defect `docs/adr/INDEX.md` documents at length: "An 'Accepted' decision with no
    /// record is not a decision that was made and written down badly — it is a claim about the
    /// project's history that nothing supports."
    /// </para>
    /// <para>
    /// So the label has a price, and the price is writing the ADR. The citation must resolve to a
    /// FILE, on the same rule <see cref="EveryAdrCitedInSourceHasAFile"/> applies to source: a
    /// pointer to `25-ADR` §11's seeded backlog is not a decision record, and the numbering there is
    /// already known to be unreliable.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryWithdrawnDocumentCitesAnAdrThatExists()
    {
        var adrDirectory = Path.Combine(RepositoryRoot(), "docs", "adr");

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

        var offenders = new List<string>();

        foreach (var document in Documents().Where(d =>
                     d.Status?.StartsWith("Withdrawn", StringComparison.Ordinal) == true))
        {
            // THE STATUS ROW, NOT THE WHOLE HEADER, AND A MUTATION IS WHY. The first version of this
            // rule read the Reconciled line too, on the reasoning that the evidence could sit in
            // either row. It could — but so could an ADR citation that has nothing to do with the
            // withdrawal: removing `12-Plugin-Architecture`'s ADR-0021 citation left the test green,
            // because the same paragraph mentions ADR-0012 in passing while explaining an unrelated
            // collision. "Cites some ADR somewhere" is not the property worth having. The status
            // itself must name the decision that withdrew the document.
            var cited = citation.Matches(document.Status ?? string.Empty)
                .Select(match => match.Groups[1].Value)
                .ToList();

            if (cited.Count == 0)
            {
                offenders.Add($"{document.Name}: Withdrawn, but its Status names no ADR");
                continue;
            }

            foreach (var serial in cited.Where(serial => !existing.Contains(serial)))
            {
                offenders.Add($"{document.Name}: cites ADR-{serial}, which has no file");
            }
        }

        offenders.Should().BeEmpty(
            "Withdrawn asserts that a decision was taken, so it must point at the record of it — "
            + "otherwise it is the same unsupported claim about the project's history that "
            + "docs/adr/INDEX.md exists to warn about. Write it as "
            + "\"Withdrawn - ADR-NNNN; short reason\"");
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


    // -----------------------------------------------------------------------------------------
    // Iteration 21. The four additions below close gaps that were found by re-verifying iteration
    // 20's own work rather than by reading its summary, which is the practice §12 asks for.
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// The documents a person actually reads first are at the repository root, and the docs-only
    /// pass did not reach them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the gap iteration 20 left. <c>STATUS.md</c> is the file the audit's own §6.12
    /// contradiction table cites TWICE — once for a Diagnostics claim that was false when written,
    /// and once for "Phase 1 and Phase 2 ✅ 100% Completed" — and it was not touched by a pass that
    /// classified all sixty-two files in <c>docs/</c>. It had by then gone seven remediation
    /// iterations without an edit, still naming item 4 as "next" and still quoting a test count of
    /// 542 against an actual 891.
    /// </para>
    /// <para>
    /// The failure mode is the same one the rest of this file exists for, but the exposure is worse:
    /// a specification in <c>docs/</c> misleads a contributor who goes looking, whereas the README
    /// misleads everyone who arrives.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("README.md")]
    [InlineData("STATUS.md")]
    [InlineData("PRODUCT.md")]
    [InlineData("CHANGELOG.md")]
    public void EveryRootDocumentDeclaresAStatusAndWhenItWasReconciled(string fileName)
    {
        var path = Path.Combine(RepositoryRoot(), fileName);

        File.Exists(path).Should().BeTrue($"{fileName} is one of the documents this rule covers");

        var lines = File.ReadAllLines(path).Take(HeaderLineBudget).ToArray();
        var status = FieldValue(lines, "Status");
        var reconciled = FieldValue(lines, "Reconciled");

        status.Should().NotBeNull(
            $"{fileName} is read before anything else in this repository and must say whether it "
            + "describes software that exists");

        AllowedStatusPrefixes.Any(allowed => status!.StartsWith(allowed, StringComparison.Ordinal))
            .Should().BeTrue($"{fileName} carries the status '{status}', which is outside the closed vocabulary");

        reconciled.Should().NotBeNull(
            $"{fileName}'s status needs a date, or nobody can see it go stale — which is exactly "
            + "what happened to STATUS.md for seven iterations");
    }

    /// <summary>
    /// TD-112: the status rule's scope is stated, not implied. Every markdown file at the repository
    /// root is either covered by the status rule (listed in the theory above) or is a GitHub/AI
    /// meta-file on the explicit exempt list. A new root file that is neither fails, which is how the
    /// unstated exemption that once missed <c>STATUS.md</c> is prevented from recurring.
    /// </summary>
    /// <remarks>
    /// The exemption is for root meta-files and <c>docs/superpowers/</c> ONLY. Nothing here relaxes
    /// the check on <c>docs/*</c>: <see cref="Documents"/> still requires a status on every file there.
    /// </remarks>
    [Fact]
    public void EveryRootMarkdownFileIsEitherCoveredByTheStatusRuleOrExplicitlyExempt()
    {
        string[] covered = ["README.md", "STATUS.md", "PRODUCT.md", "CHANGELOG.md"];
        string[] exemptMetaFiles = ["CONTRIBUTING.md", "CODE_OF_CONDUCT.md", "TRADEMARK.md", "GEMINI.md"];

        var rootFiles = Directory
            .GetFiles(RepositoryRoot(), "*.md", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .ToList();

        rootFiles.Should().Contain(covered, "the covered root documents must exist");

        var unclassified = rootFiles
            .Where(name => !covered.Contains(name!) && !exemptMetaFiles.Contains(name!))
            .ToList();

        unclassified.Should().BeEmpty(
            "a root markdown file is either a design/product document (add it to the covered list and "
            + "give it a Status) or a GitHub/AI meta-file (add it to the exempt list) - docs/24-Contributing "
            + "section 14 states the rule");

        exemptMetaFiles.Intersect(covered).Should().BeEmpty("a file cannot be both covered and exempt");

        File.Exists(Path.Combine(DocsDirectory(), "24-Contributing.md")).Should().BeTrue();
    }

    /// <summary>
    /// The document set must not shrink silently.
    /// </summary>
    /// <remarks>
    /// Every assertion above is of the form "no document violates X". Deleting a document satisfies
    /// all of them. The floor is deliberately a floor and not an equality: adding a document is
    /// ordinary, and removing one is a decision that should have to be taken in the open, by
    /// lowering this number in a commit that says why.
    /// </remarks>
    [Fact]
    public void TheDocumentSetHasNotShrunk()
    {
        Documents().Should().HaveCountGreaterThanOrEqualTo(
            62,
            "there were 62 documents in docs/ when this floor was set. If one was deliberately "
            + "deleted, lower this number in the same commit and say which and why");
    }

    /// <summary>
    /// ADRs carry their own status vocabulary, and it is not the one above.
    /// </summary>
    /// <remarks>
    /// <c>docs/adr/</c> sits outside <see cref="Documents"/>, which reads the top level of
    /// <c>docs/</c> only. That is correct — an ADR is not "Implemented" or "Aspirational", it is a
    /// decision with a lifecycle — but it left the ADR files with no check at all, so this applies
    /// the ADR vocabulary from <c>25-ADR</c> §3 to them.
    /// </remarks>
    [Fact]
    public void EveryAdrDeclaresAStatusFromTheAdrVocabulary()
    {
        string[] allowed = { "Proposed", "Accepted", "Rejected", "Deprecated", "Superseded" };

        var offences = new List<string>();

        foreach (var path in AdrFiles())
        {
            var lines = File.ReadAllLines(path).Take(HeaderLineBudget).ToArray();
            var status = FieldValue(lines, "Status");

            if (status is null)
            {
                offences.Add($"{Path.GetFileName(path)} declares no status");
                continue;
            }

            if (!allowed.Any(value => status.StartsWith(value, StringComparison.Ordinal)))
            {
                offences.Add($"{Path.GetFileName(path)}: '{status}'");
            }
        }

        offences.Should().BeEmpty(
            "an ADR whose status is not Proposed, Accepted, Rejected, Deprecated or Superseded "
            + "cannot be acted on: a reader cannot tell whether it binds them");
    }

    /// <summary>
    /// Every quotation an ADR uses to cite a document must still appear in that document (TD-40).
    /// </summary>
    /// <remarks>
    /// <para>
    /// ADR-0012 first cited its callers by line number — <c>30-Observability:22,251,296</c> and so
    /// on. Every one of those numbers was wrong within the same commit, because the
    /// <c>Reconciled</c> row that the same change added to each document's header shifted every line
    /// below it by one. The citations were not merely stale; they were born stale, by an edit made
    /// minutes later in the same commit.
    /// </para>
    /// <para>
    /// So the ADRs cite by quotation instead, and this test is what keeps a quotation honest. A
    /// quotation survives insertions, deletions and re-flowing above it, and fails loudly and
    /// specifically when the sentence it points at is actually edited away — which is the moment
    /// somebody should look at whether the decision still describes the document.
    /// </para>
    /// <para>
    /// The format is a <c>## Citations</c> section containing lines of the form
    /// <c>- `NN-Document.md` — "quoted text"</c>. An ADR with no such section is fine; an ADR with
    /// one that no longer matches is not.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryAdrQuotationStillAppearsInTheDocumentItCites()
    {
        var citation = new System.Text.RegularExpressions.Regex(
            "^- `([^`]+)` — \"(.+)\"\\s*$",
            System.Text.RegularExpressions.RegexOptions.Multiline,
            TimeSpan.FromSeconds(5));

        var offences = new List<string>();
        var quotationsChecked = 0;

        foreach (var adr in AdrFiles())
        {
            var text = File.ReadAllText(adr);
            var start = text.IndexOf("## Citations", StringComparison.Ordinal);

            if (start < 0)
            {
                continue;
            }

            var rest = text[start..];
            var next = rest.IndexOf("\n## ", 1, StringComparison.Ordinal);
            var section = next < 0 ? rest : rest[..next];

            foreach (System.Text.RegularExpressions.Match match in citation.Matches(section))
            {
                var name = match.Groups[1].Value;
                var document = Path.Combine(DocsDirectory(), name);
                var quotation = match.Groups[2].Value;

                quotationsChecked++;

                if (!File.Exists(document))
                {
                    offences.Add($"{Path.GetFileName(adr)} cites '{name}', which does not exist");
                    continue;
                }

                if (!File.ReadAllText(document).Contains(quotation, StringComparison.Ordinal))
                {
                    offences.Add(
                        $"{Path.GetFileName(adr)} quotes \"{quotation}\" from {name}, "
                        + "which no longer contains it");
                }
            }
        }

        offences.Should().BeEmpty(
            "a decision record that quotes a document which has since been edited is pointing a "
            + "reader at something that is no longer there");

        quotationsChecked.Should().BeGreaterThan(
            0,
            "ADR-0012 carries a Citations section; finding none means the parser stopped matching "
            + "and this test is passing on an empty set");
    }

    private static List<string> AdrFiles()
        => Directory
            .GetFiles(Path.Combine(RepositoryRoot(), "docs", "adr"), "*.md", SearchOption.TopDirectoryOnly)
            .Where(path => !string.Equals(Path.GetFileName(path), "INDEX.md", StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

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
