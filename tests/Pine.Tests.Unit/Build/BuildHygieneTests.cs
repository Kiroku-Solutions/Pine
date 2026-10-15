using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace Pine.Tests.Unit.Build;

/// <summary>
/// The build's own inputs are checked here: where package versions are written, which packages are
/// referenced at all, and which project files exist (TD-41, TD-42).
/// </summary>
/// <remarks>
/// <para>
/// WHY THESE ARE TESTS AND NOT A CONVENTION. TD-42 is the record of what a convention achieves.
/// Every version in this repository used to be written in the <c>.csproj</c> that used it, so a
/// package referenced by two projects carried two independent literals and nothing compared them:
/// <c>Microsoft.EntityFrameworkCore.Sqlite.Core</c> sat at <c>10.0.9</c> while
/// <c>Microsoft.EntityFrameworkCore.Design</c> sat at <c>10.0.11</c>, both pulling the same
/// <c>Relational</c> assembly, and NuGet unified them silently. The build was green and the
/// versions on disk were not the versions in the files.
/// </para>
/// <para>
/// Central package management fixes half of that by itself — <c>NU1008</c> makes a <c>Version</c>
/// attribute in a <c>.csproj</c> a build error. It does NOT stop
/// <c>Directory.Packages.props</c> from drifting internally, does not stop a family of packages
/// that must move together from moving apart inside that one file, and does not stop a removed
/// reference leaving its version behind. Those are the assertions below.
/// </para>
/// <para>
/// WHAT THIS DELIBERATELY DOES NOT CHECK. It does not assert that any version is CURRENT. Deciding
/// when to take a new release is a judgement with a changelog behind it, and a test that demanded
/// the latest version would fail on a schedule set by other people's publishing.
/// </para>
/// </remarks>
public partial class BuildHygieneTests
{
    /// <summary>
    /// Package families whose members share assemblies and must therefore share a version.
    /// </summary>
    /// <remarks>
    /// The EF Core entry is TD-42's own finding, kept as a rule so it cannot recur: a
    /// <c>Sqlite.Core</c>/<c>Design</c> split is not a build failure, it is a silent unification
    /// whose result is invisible in every file a reader would open.
    /// </remarks>
    private static readonly string[] SingleVersionFamilies =
    {
        "Microsoft.EntityFrameworkCore.",
        "Avalonia.",
    };

    /// <summary>
    /// The PaddleOCR model packages this repository is permitted to reference (TD-41).
    /// </summary>
    /// <remarks>
    /// <para>
    /// TD-41 recorded four — <c>Models.Local</c>, <c>.LocalV3</c>, <c>.LocalV4</c>, <c>.LocalV5</c>
    /// — and prescribed "keep one". Two is the minimum the dependency graph permits, and that is
    /// the correction. <c>Sdcb.PaddleOCR.Models.Local</c> is a 36 MB facade: it is where the type
    /// <c>LocalFullModels</c> lives, which is the only thing <c>PaddleOcrEngine</c> names. The
    /// weights <c>LocalFullModels.LatinV5</c> hands out live in
    /// <c>Sdcb.PaddleOCR.Models.LocalV5</c>, which the facade's own <c>.nuspec</c> declares as a
    /// dependency at <c>&gt;= 3.0.0</c>. Removing it from the file would not remove it from the
    /// graph; it would only float it down to that floor.
    /// </para>
    /// <para>
    /// <c>LocalV3</c> (238 MB) and <c>LocalV4</c> (234 MB) were the genuine duplication. Neither
    /// defines <c>LocalFullModels</c> — each defines only its own <c>KnownModels</c> — neither is
    /// a dependency of anything else referenced here, and no file in <c>src/</c> or <c>tests/</c>
    /// named either.
    /// </para>
    /// </remarks>
    private static readonly string[] AllowedPaddleModelPackages =
    {
        "Sdcb.PaddleOCR.Models.Local",
        "Sdcb.PaddleOCR.Models.LocalV5",
    };

    [Fact]
    public void CentralPackageManagementIsTurnedOn()
    {
        var path = Path.Combine(RepositoryRoot(), "Directory.Packages.props");

        File.Exists(path).Should().BeTrue(
            "Directory.Packages.props is the single place a package version may be written (TD-42)");

        var value = XDocument
            .Load(path)
            .Descendants("ManagePackageVersionsCentrally")
            .Select(element => element.Value.Trim())
            .FirstOrDefault();

        value.Should().Be(
            "true",
            "without this property the file is inert and every .csproj is free to pin its own version again");
    }

    [Fact]
    public void NoProjectWritesItsOwnPackageVersion()
    {
        var offenders = new List<string>();

        foreach (var project in ProjectFiles())
        {
            foreach (var reference in PackageReferences(project))
            {
                if (reference.Version is not null)
                {
                    offenders.Add($"{Relative(project)}: {reference.Name} = {reference.Version}");
                }
            }
        }

        offenders.Should().BeEmpty(
            "a version written next to a reference is a second opinion about that package, and the "
            + "two opinions are what TD-42 recorded drifting apart");
    }

    [Fact]
    public void EveryPackageReferenceHasACentralVersion()
    {
        var central = CentralVersions();
        var missing = new List<string>();

        foreach (var project in ProjectFiles())
        {
            foreach (var reference in PackageReferences(project))
            {
                if (!central.ContainsKey(reference.Name))
                {
                    missing.Add($"{Relative(project)}: {reference.Name}");
                }
            }
        }

        missing.Should().BeEmpty(
            "a reference with no PackageVersion entry does not restore, so this can only fail while "
            + "a file is half-edited -- which is exactly when it is useful");
    }

    [Fact]
    public void NoCentralVersionIsUnreferenced()
    {
        var referenced = ProjectFiles()
            .SelectMany(PackageReferences)
            .Select(reference => reference.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Guard against a vacuous pass: an empty set of references would make every entry orphaned,
        // and an empty set of entries would make this assertion trivially true.
        referenced.Should().NotBeEmpty("the project scan must have found package references");

        var central = CentralVersions();
        central.Should().NotBeEmpty("Directory.Packages.props must have been parsed");

        var orphans = central.Keys
            .Where(name => !referenced.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        orphans.Should().BeEmpty(
            "a PackageVersion nothing references is a version nobody maintains and a package a "
            + "reader believes is in use. Deleting the last reference must delete the entry too");
    }

    [Fact]
    public void EachSingleVersionFamilyHoldsOneVersion()
    {
        var central = CentralVersions();

        foreach (var family in SingleVersionFamilies)
        {
            // The bare package name counts as a member: "Avalonia." would otherwise match every
            // Avalonia.* package and miss `Avalonia` itself, which is the one they all sit on top
            // of. A rule that cannot see the root of the family it names is worse than no rule.
            var bareName = family.TrimEnd('.');

            var members = central
                .Where(entry =>
                    entry.Key.StartsWith(family, StringComparison.OrdinalIgnoreCase)
                    || entry.Key.Equals(bareName, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            members.Should().NotBeEmpty($"'{family}' names a family this repository actually uses");

            members
                .Select(entry => entry.Value)
                .Distinct(StringComparer.Ordinal)
                .Should()
                .ContainSingle(
                    $"every package under '{family}' ships against the same shared assemblies. "
                    + "A split resolves silently rather than failing, which is why TD-42's "
                    + "10.0.9/10.0.11 EF Core mismatch survived unnoticed. Members: "
                    + string.Join(", ", members.Select(entry => $"{entry.Key}={entry.Value}")));
        }
    }

    [Fact]
    public void OnlyTheTwoNecessaryPaddleModelPackagesAreReferenced()
    {
        var referenced = ProjectFiles()
            .SelectMany(PackageReferences)
            .Select(reference => reference.Name)
            .Where(name => name.StartsWith("Sdcb.PaddleOCR.Models.", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        referenced.Should().Equal(
            AllowedPaddleModelPackages,
            "each of these packages embeds a full det+rec model set of a few hundred megabytes, and "
            + "the artefact carries every one that is referenced. Adding a third means a reader "
            + "cannot tell which model version the OCR actually runs -- which is the state TD-41 "
            + "recorded, with four of them referenced at once");
    }

    private static readonly string[] ScriptDirectories = ["scripts", "packaging"];

    // Files under src/**/Assets that may exist unnamed. Each entry is a decision with a reason.
    //  - PineLogo.svg: the vector source of PineLogo.png (which SetupView names). Not loaded by any
    //    code, but kept as the editable master of the logo; it is a few KB. Remove it, or load it,
    //    and delete this entry (TD-131).
    private static readonly HashSet<string> UnreferencedAssetAllowList =
        new(StringComparer.Ordinal) { "PineLogo.svg" };

    // A file name must appear in code or markup. Project files, .md and .json are excluded: a csproj
    // comment or a doc mentioning a dead asset (the Assets/Ai csproj does) must not make it look used.
    private static readonly HashSet<string> SourceExtensionsThatCanNameAnAsset =
        new(StringComparer.OrdinalIgnoreCase) { ".cs", ".axaml", ".xaml" };

    /// <summary>
    /// TD-41, TD-131. Every file shipped from any <c>Assets</c> folder under <c>src/</c> must be named by production code.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHAT THIS CAUGHT. The folder held four files and the <c>.csproj</c> copied all of them with
    /// a glob. Exactly one — <c>yolo-receipt-nano.onnx</c> — was read by anything:
    /// <c>ReceiptDetector</c> names it in a <c>Path.Combine</c>. The other three were
    /// <c>PP-OCRv4-det.onnx</c> (113 MB), <c>PP-OCRv4-rec.onnx</c> (90 MB) and <c>dict.txt</c>, and
    /// <c>git log -S</c> over the whole history, all branches, returns ZERO commits naming
    /// <c>dict.txt</c> or <c>det.onnx</c>. They were never wired, not even once and later removed.
    /// </para>
    /// <para>
    /// WHY A GREP AND NOT SOMETHING CLEVERER. A model file is loaded by path, so its name appears
    /// as a string literal in the code that loads it or it cannot be loaded at all. That makes the
    /// crude check the exact one: if no source file contains the file's name, no source file can
    /// open it. The failure mode this rules out is not a subtle one — it is 203 MB of weights in
    /// every artefact for a code path that does not exist.
    /// </para>
    /// <para>
    /// THIS IS NOT A CHECK THAT THE MODEL IS CORRECT, or current, or that the code which names it
    /// runs. It is a check that the shipping list and the reading list are the same list.
    /// </para>
    /// </remarks>
    [Fact]
    public void ShippedAssetsAreNamedByProductionCode()
    {
        // TD-131: the rule covers EVERY `Assets` folder under src/, not only Assets/Ai. A folder added
        // later is picked up automatically; none has to be remembered.
        var srcRoot = Path.Combine(RepositoryRoot(), "src");
        var sep = Path.DirectorySeparatorChar;

        var assetDirectories = Directory
            .GetDirectories(srcRoot, "Assets", SearchOption.AllDirectories)
            .Where(dir => !dir.Contains($"{sep}obj{sep}", StringComparison.Ordinal))
            .Where(dir => !dir.Contains($"{sep}bin{sep}", StringComparison.Ordinal))
            .ToArray();

        assetDirectories.Should().Contain(
            dir => dir.EndsWith($"{sep}Assets{sep}Ai", StringComparison.Ordinal)
                || dir.EndsWith($"{sep}Pine.Infrastructure{sep}Assets", StringComparison.Ordinal),
            "the OCR ships a model from Pine.Infrastructure/Assets/Ai; not finding it means the scan is broken");

        var assets = assetDirectories
            .SelectMany(dir => Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{sep}obj{sep}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{sep}bin{sep}", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .Where(name => name is not null)
            .Select(name => name!)
            .Where(name => !UnreferencedAssetAllowList.Contains(name))
            .ToArray();

        assets.Should().NotBeEmpty("finding no assets means the scan is broken, not that none ship");

        var sources = Directory
            .GetFiles(Path.Combine(RepositoryRoot(), "src"), "*", SearchOption.AllDirectories)
            .Where(path => SourceExtensionsThatCanNameAnAsset.Contains(Path.GetExtension(path)))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(File.ReadAllText)
            .ToArray();

        sources.Should().NotBeEmpty("the source scan must have found files");

        var unreferenced = assets
            .Where(asset => !sources.Any(text => text.Contains(asset, StringComparison.Ordinal)))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        unreferenced.Should().BeEmpty(
            "an asset no source file names cannot be opened by any code path, yet it is copied into "
            + "every build output and every artefact. Three such files shipped for months, totalling "
            + "203 MB, superseded by the model weights embedded in the PaddleOCR package before they "
            + "were ever wired");
    }

    /// <summary>
    /// TD-130. Every shell script the repository owns runs with <c>-u</c> and <c>pipefail</c>, and
    /// with <c>-e</c> unless it has deliberately chosen explicit exit-code capture.
    /// </summary>
    /// <remarks>
    /// A broken pipeline and a true negative look identical to a reader, and §12.58 of the audit
    /// recorded a false "no blob over 500 KB" from exactly that. <c>selftest-signing.sh</c> is the one
    /// script that omits <c>-e</c> on purpose (it runs cases and records each exit code itself).
    /// </remarks>
    [Fact]
    public void EveryShellScriptFailsLoudlyOnAMaskedPipeline()
    {
        var root = RepositoryRoot();
        var scripts = ScriptDirectories
            .Select(dir => Path.Combine(root, dir))
            .Where(Directory.Exists)
            .SelectMany(dir => Directory.GetFiles(dir, "*.sh"))
            .ToArray();

        scripts.Should().NotBeEmpty("finding no scripts means the scan is broken, not that none exist");

        var explicitCapture = new HashSet<string>(StringComparer.Ordinal) { "selftest-signing.sh" };

        foreach (var script in scripts)
        {
            var name = Path.GetFileName(script);
            var setLine = File.ReadAllLines(script)
                .FirstOrDefault(line => Regex.IsMatch(line, @"^set\s+-[a-z]+\b", RegexOptions.None, TimeSpan.FromSeconds(5)));

            setLine.Should().NotBeNull($"{name} must declare its shell options");
            setLine.Should().Contain("pipefail", $"{name}: a failure inside a pipeline must not be masked");
            setLine.Should().Contain("u", $"{name}: an unset variable must not expand to nothing");

            if (!explicitCapture.Contains(name))
            {
                Regex.IsMatch(setLine!, @"^set\s+-[a-z]*e", RegexOptions.None, TimeSpan.FromSeconds(5))
                    .Should().BeTrue($"{name} must stop at the first failing command");
            }
        }
    }

    /// <summary>
    /// The coverage gate exists, is armed in CI, and its floors are real numbers (Phase 2 item 9).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A coverage floor has exactly two failure modes, and neither is "the number was wrong". The
    /// first is that the gate is deleted or commented out; the second is that a floor is quietly
    /// lowered to whatever today's number happens to be, which converts a ratchet into a
    /// thermometer. This test catches the first and makes the second a visible edit.
    /// </para>
    /// <para>
    /// It deliberately does NOT assert the floor VALUES. Choosing them is a judgement, and a test
    /// that pinned them would have to be edited in the same commit as the thing it guards — which
    /// is no guard at all. What it asserts is that they are present, parseable, and inside 1..100:
    /// a floor of 0 passes every possible run and is the tidiest way to disarm a gate while leaving
    /// it looking armed.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheCoverageGateExistsAndIsArmedInCi()
    {
        var root = RepositoryRoot();
        var gate = Path.Combine(root, "scripts", "coverage-gate.sh");

        File.Exists(gate).Should().BeTrue("scripts/coverage-gate.sh is the coverage floor (Phase 2 item 9)");

        var script = File.ReadAllText(gate);

        var floors = Regex
            .Matches(script, @"^FLOOR_[A-Z_]+=(\d+)$", RegexOptions.Multiline, TimeSpan.FromSeconds(5))
            .Select(match => int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();

        floors.Should().NotBeEmpty("the gate must declare at least one floor");
        floors.Should().OnlyContain(
            floor => floor > 0 && floor <= 100,
            "a floor of 0 passes every conceivable run while leaving the gate looking armed, which is "
            + "the quietest way to switch a coverage gate off");

        // COMMENT LINES ARE STRIPPED FIRST, AND THAT IS NOT FUSSINESS. The first version of this
        // assertion searched the whole file for "scripts/coverage-gate.sh". A falsification mutation
        // replaced the step's `run:` with `echo skipped` and the test stayed GREEN, because the
        // workflow explains the gate in a comment directly above it -- so the test was reading the
        // explanation and calling it the mechanism.
        var workflowLines = File.ReadAllLines(Path.Combine(root, ".github", "workflows", "dotnet-ci.yml"))
            .Where(line => !line.TrimStart().StartsWith('#'))
            .ToArray();

        workflowLines.Should().Contain(
            line => line.Contains("run:", StringComparison.Ordinal)
                 && line.Contains("scripts/coverage-gate.sh", StringComparison.Ordinal),
            "the gate has to be RUN by CI, not merely mentioned in it. Note that this asserts the "
            + "step exists -- it cannot assert the workflow ever fires, which is TD-07 and is why "
            + "the gate is also a one-command local script");
    }

    /// <summary>
    /// CI is armed: the workflow fires on a pull request into <c>main</c> and on a push to
    /// <c>main</c>, not on a human remembering to dispatch it (TD-07, TD-136).
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS TEST EXISTS BECAUSE THE TRIGGER HAS ALREADY BEEN DELETED ONCE. Commit <c>79c5364</c>
    /// removed <c>push</c> and <c>pull_request</c> to save compute minutes; nothing failed, nothing
    /// went red, and for thirty-one iterations the entire regression net -- the architecture rules,
    /// the coverage floor, the egress scan, the document-status tests and the whole suite -- was
    /// inert behind a trigger block nobody was looking at. The audit then spent three iterations
    /// reading past the comment that said so.
    /// </para>
    /// <para>
    /// A guard that can be switched off in one line, silently, is the failure this repository has
    /// actually suffered, so the one line is asserted. Deleting either trigger now reddens the suite
    /// that the trigger runs. That is not circular, it is the only arrangement available: the
    /// assertion lives in the tests, and the tests are what the trigger fires.
    /// </para>
    /// <para>
    /// COMMENT LINES ARE STRIPPED FIRST, and the <c>on:</c> block is isolated before anything is
    /// matched. Both matter here. This workflow's header discusses <c>pull_request</c> at length, so
    /// a test reading the raw file would stay green on the prose alone after the trigger itself had
    /// been deleted -- the exact falsification that
    /// <see cref="TheCoverageGateExistsAndIsArmedInCi"/> records having survived once already.
    /// </para>
    /// <para>
    /// WHAT IT DOES NOT ASSERT: which jobs run on a pull request. <c>package</c> deliberately does
    /// not (it publishes a ~680 MB artefact no reviewer reads), and pinning a job list here would
    /// make every future job a two-file edit for no gain. Nor can it assert that GitHub ever ran
    /// anything: that is proved by opening a pull request and watching it go red, which is the
    /// definition of done this row was closed against.
    /// </para>
    /// </remarks>
    [Fact]
    public void CiIsArmedOnPullRequestAndPushToMain()
    {
        var lines = File
            .ReadAllLines(Path.Combine(RepositoryRoot(), ".github", "workflows", "dotnet-ci.yml"))
            .Where(line => !line.TrimStart().StartsWith('#'))
            .ToArray();

        var start = Array.FindIndex(lines, line => line.TrimEnd() == "on:");

        start.Should().BeGreaterThanOrEqualTo(0, "the workflow must declare an `on:` block");

        // The block ends at the next line that starts in column zero: YAML's own rule, and the only
        // one that keeps `push:` inside `on:` distinguishable from a `push:` written anywhere else.
        var triggers = lines
            .Skip(start + 1)
            .TakeWhile(line => line.Length == 0 || char.IsWhiteSpace(line[0]))
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToArray();

        triggers.Should().Contain(
            "pull_request:",
            "the gate ON THE WAY IN is the one that matters: without it a red branch merges, and the "
            + "defect is found later by someone who did not write it. This is TD-07");

        triggers.Should().Contain(
            "push:",
            "pull_request cannot see a direct push, an admin merge, or a squash whose result differs "
            + "from the head that was reviewed");

        triggers
            .Count(line => line == "branches: [main]")
            .Should()
            .Be(2, "both triggers must be scoped to `main`, and an unscoped trigger is not the same "
                 + "guard: it fires on every branch, costs six runner-legs each time, and is the "
                 + "shape of the compute bill that got the triggers deleted in `79c5364`");
    }

    [Fact]
    public void EveryProjectFileIsInTheSolution()
    {
        var solution = File.ReadAllText(Path.Combine(RepositoryRoot(), "Pine.slnx"));

        var listed = Regex
            .Matches(solution, @"Path=""([^""]+)""", RegexOptions.None, TimeSpan.FromSeconds(5))
            .Select(match => Path.GetFileName(match.Groups[1].Value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        listed.Should().NotBeEmpty("Pine.slnx must have been parsed");

        var orphans = ProjectFiles()
            .Where(project => !listed.Contains(Path.GetFileName(project)))
            .Select(Relative)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        orphans.Should().BeEmpty(
            "a project outside the solution is built by nothing, tested by nothing and reviewed by "
            + "nobody, while still being a tracked file carrying its own dependencies. One such "
            + "project -- a committed reflection probe under scratch/ -- was found by this row and "
            + "deleted");
    }

    /// <summary>
    /// Every project in <c>Pine.slnx</c> is built by exactly one CI leg, and no leg builds a project
    /// twice (TD-54, plan item 0.2).
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the second half of a chain, and it is worth naming both halves because neither is
    /// worth much alone. <see cref="EveryProjectFileIsInTheSolution"/> says every <c>.csproj</c> on
    /// disk is in the solution. This says every project in the solution is on a CI leg. Together
    /// they say the thing anyone actually cares about: **a project cannot be added to this
    /// repository and stay outside CI**. Before item 0.2 the second half did not exist, and the
    /// workflow answered it with three hand-written lists that were correct on the day they were
    /// written and could never notice a twelfth project.
    /// </para>
    /// <para>
    /// IT RUNS THE SCRIPT RATHER THAN REIMPLEMENTING IT, which is the only arrangement that proves
    /// anything. A C# copy of the classification rules would be a second implementation that agrees
    /// with the first until someone edits one of them, and the test would then be asserting its own
    /// opinion about which projects CI ought to build rather than the answer CI is actually given.
    /// The cost is a dependency on <c>bash</c> — already a hard dependency of this repository, since
    /// the coverage gate, the packaging script and every non-trivial workflow step are bash.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryProjectInTheSolutionIsBuiltBySomeCiLeg()
    {
        var solution = File.ReadAllText(Path.Combine(RepositoryRoot(), "Pine.slnx"));

        var listed = Regex
            .Matches(solution, @"Path=""([^""]+)""", RegexOptions.None, TimeSpan.FromSeconds(5))
            .Select(match => match.Groups[1].Value.Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        listed.Should().NotBeEmpty("Pine.slnx must have been parsed");

        // The legs the workflow actually has. If a leg is added there and not here, the union check
        // below fails with the projects it builds listed as uncovered, which is the right way round:
        // this test is not a list of legs, it is a list of what the workflow is known to run.
        var legs = new[] { "desktop", "test", "android", "ios" };

        var covered = legs
            .SelectMany(leg => CiProjectsFor(leg).Select(project => (Leg: leg, Project: project)))
            .ToArray();

        var uncovered = listed
            .Except(covered.Select(entry => entry.Project), StringComparer.Ordinal)
            .ToArray();

        uncovered.Should().BeEmpty(
            "a project in the solution that no CI leg builds is compiled by nothing and tested by "
            + "nothing, while the pipeline reports green over it. That is TD-54, and it is the exact "
            + "shape of the three broken platform heads TD-07 recorded");

        var duplicated = covered
            .GroupBy(entry => entry.Project, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key + " -> " + string.Join(", ", group.Select(entry => entry.Leg)))
            .ToArray();

        duplicated.Should().BeEmpty(
            "a project on two legs is built twice on every run. It is not a correctness defect, it "
            + "is a bill, and it means the classification rules overlap where they were meant to be "
            + "ordered");

        // The other half of the drift: the workflow must not go back to naming projects itself. A
        // hand-written path here would still build and still pass, and would silently stop being
        // answerable by the script.
        var workflow = File
            .ReadAllLines(Path.Combine(RepositoryRoot(), ".github", "workflows", "dotnet-ci.yml"))
            .Where(line => !line.TrimStart().StartsWith('#'))
            .Where(line => line.Contains(".csproj", StringComparison.Ordinal))
            .Where(line => !line.Contains("basename", StringComparison.Ordinal))
            .Where(line => !line.Contains("hashFiles", StringComparison.Ordinal))
            .ToArray();

        workflow.Should().BeEmpty(
            "the workflow must ask scripts/ci-projects.sh which projects to build rather than "
            + "naming any of them. The two exemptions above are the `basename` that derives a .trx "
            + "file name and the `hashFiles` glob in the cache key, neither of which enumerates "
            + "anything");
    }

    /// <summary>
    /// Each CI leg is claimed by exactly one job, and that job builds what it claims (TD-54).
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS TEST EXISTS BECAUSE THE FIRST VERSION OF THE ROW'S GUARD DID NOT CATCH THE OBVIOUS
    /// MUTATION. <see cref="EveryProjectInTheSolutionIsBuiltBySomeCiLeg"/> proves the script agrees
    /// with the solution, and proves the workflow names no project of its own. It says nothing about
    /// whether any job still calls the script — an adversarial pass deleted the entire
    /// <c>desktop</c> job from the workflow and the suite stayed green, which is TD-54's own failure
    /// mode moved one layer up: seven projects would have stopped being built, under a green tick.
    /// </para>
    /// <para>
    /// EXACTLY ONE JOB PER LEG, and not merely "at least one", because at-least-one is what let the
    /// second mutation through: pointing the <c>desktop</c> job's restore step at the <c>test</c>
    /// leg leaves every leg referenced by something while restoring the wrong projects. Two jobs
    /// claiming one leg is either duplicated work or a mis-typed leg name, and neither is intended.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryCiLegIsClaimedByExactlyOneJobThatBuildsIt()
    {
        var jobs = WorkflowJobs();

        jobs.Should().NotBeEmpty("the workflow must declare jobs");

        var heads = jobs
            .SelectMany(job => job.Value)
            .Where(line => line.TrimStart().StartsWith("- head:", StringComparison.Ordinal))
            .Select(line => line.Split(':', 2)[1].Trim())
            .ToArray();

        heads.Should().NotBeEmpty(
            "the platform-heads matrix names its legs as `head:` values, and the workflow passes "
            + "`${{ matrix.head }}` to the script rather than a literal leg name");

        foreach (var leg in new[] { "desktop", "test", "android", "ios" })
        {
            var claiming = jobs
                .Where(job => job.Value.Any(line => InvokesLeg(line, leg, heads)))
                .Select(job => job.Key)
                .ToArray();

            claiming.Should().HaveCount(
                1,
                "leg '{0}' must be built by exactly one job, and is claimed by [{1}]. Zero means "
                + "those projects are built by nothing while CI still reports green; two means one "
                + "of the jobs is running the wrong leg",
                leg,
                string.Join(", ", claiming));

            var steps = jobs[claiming[0]];

            steps.Should().Contain(
                line => line.Contains("dotnet restore", StringComparison.Ordinal),
                "the job claiming the '{0}' leg must restore it", leg);

            steps.Should().Contain(
                line => line.Contains("dotnet build", StringComparison.Ordinal),
                "the job claiming the '{0}' leg must build it", leg);

            if (leg == "test")
            {
                steps.Should().Contain(
                    line => line.Contains("dotnet test", StringComparison.Ordinal),
                    "a test project that is built and never run is the green-light-over-an-empty-"
                    + "file case iteration 27 deleted three projects over");
            }
        }
    }

    /// <summary>
    /// Every job that runs a gate ends by asserting that the gate actually ran, and that assertion
    /// can never itself be skipped (plan item 0.3).
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE FAILURE BEING GUARDED IS NOT A BROKEN GATE, IT IS AN ABSENT ONE. A step carrying an
    /// <c>if:</c> that evaluates false is skipped, and a skipped step does not fail its job; a loop
    /// over an empty list iterates zero times; a <c>run:</c> replaced by <c>echo skipped</c> exits 0.
    /// All three report green. The coverage floor is the live case — it runs on ubuntu only, so on
    /// windows it is skipped by design, and until this item nothing anywhere said it had to run on
    /// ubuntu.
    /// </para>
    /// <para>
    /// So each job records a receipt at the moment a gate succeeds and verifies the expected set at
    /// the end, and this test asserts the arrangement is intact: a job that records must verify, and
    /// a verify step must carry no condition. **A verify step with an `if:` is a gate that can be
    /// skipped**, which is the exact shape of the problem it was added to solve.
    /// </para>
    /// <para>
    /// It also asserts the workflow never writes the `coverage` receipt itself. That receipt is
    /// written inside <c>scripts/coverage-gate.sh</c>, after the floors have been compared, because
    /// a receipt written from the workflow would prove only that the workflow reached a line —
    /// exactly the falsification that survived once already, when a guard matched the comment above
    /// the coverage step instead of the step.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryJobWithAGateAssertsThatTheGateRan()
    {
        var jobs = WorkflowJobs();

        // A JOB HAS A GATE IF IT RUNS ONE -- not if it happens to record a receipt. Deriving the set
        // from the `record` calls was the first spelling of this test, and an adversarial pass
        // deleted the desktop job's only `record` call: the job dropped out of the set it was being
        // judged by, no assertion applied to it, and the suite stayed green. A test whose scope is
        // defined by the thing it is checking cannot notice that thing's absence.
        var gateCommands = new[] { "dotnet build", "dotnet test", "coverage-gate.sh", "package.sh" };

        var gated = jobs
            .Where(job => job.Value.Any(line => gateCommands.Any(gate => IsUncommented(line, gate))))
            .ToArray();

        gated.Should().NotBeEmpty("the workflow must run something worth gating");

        foreach (var (job, lines) in gated)
        {
            lines.Should().Contain(
                line => IsUncommented(line, "ci-receipts.sh reset"),
                "job '{0}' must start from an empty receipt log, or a receipt proves only that some "
                + "run somewhere once did the work", job);

            lines.Should().Contain(
                line => IsUncommented(line, "ci-receipts.sh record"),
                "job '{0}' runs a gate and records nothing, so nothing can prove the gate ran", job);

            lines.Should().Contain(
                line => IsUncommented(line, "ci-receipts.sh verify"),
                "job '{0}' records that its gates ran and never checks the receipts, which is the "
                + "same green tick over nothing that the receipts exist to prevent", job);

            var steps = WorkflowSteps(lines);

            // EVERY RECEIPT RECORDED IS A RECEIPT VERIFIED. Narrowing `verify weights artefact` to
            // `verify weights` used to pass: the test asked whether a verify call existed, never
            // what it checked, so a gate could keep recording into a void. The names compared are
            // the literal text as written -- `build:$p` here matches `build:$p` there -- because
            // both sides are built from the same expansion and a difference between them is exactly
            // the defect.
            var recorded = steps
                .SelectMany(step => step.Lines)
                .Where(line => IsUncommented(line, "ci-receipts.sh record"))
                .Select(line => ReceiptArgument(line, "record"))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            var verifying = steps
                .Where(step => step.Lines.Any(line => IsUncommented(line, "ci-receipts.sh verify")))
                .ToArray();

            verifying.Should().HaveCount(
                1,
                "job '{0}' must have exactly one verify step; the receipts are consumed by the first "
                + "one that passes, so a second could only ever fail or check nothing", job);

            var verified = string.Join('\n', verifying[0].Lines);

            foreach (var receipt in recorded)
            {
                verified.Should().Contain(
                    receipt,
                    "job '{0}' records the receipt '{1}' and its verify step never names it, so that "
                    + "gate could stop running without anything noticing", job, receipt);
            }

            // ORDER IS PART OF THE MECHANISM. A verify step placed above the gates it checks would
            // read an empty log and could only pass by checking nothing -- or, worse, pass on a
            // stale one. It must come last.
            var verifyIndex = steps.FindIndex(step => step.Name == verifying[0].Name);
            var lastRecord = steps.FindLastIndex(
                step => step.Lines.Any(line => IsUncommented(line, "ci-receipts.sh record")));

            verifyIndex.Should().BeGreaterThan(
                lastRecord,
                "the verify step in job '{0}' runs before the gates it verifies", job);
        }

        foreach (var (job, lines) in jobs)
        {
            foreach (var (name, steps) in WorkflowSteps(lines))
            {
                if (!steps.Any(line => IsUncommented(line, "ci-receipts.sh verify")))
                {
                    continue;
                }

                steps.Should().NotContain(
                    line => Regex.IsMatch(line, @"^\s+if:", RegexOptions.None, TimeSpan.FromSeconds(5)),
                    "the verify step '{0}' in job '{1}' carries a condition. A verification that can "
                    + "be skipped verifies nothing, and skipping is what it is there to catch",
                    name,
                    job);
            }
        }

        var workflowWritesTheCoverageReceipt = jobs
            .SelectMany(job => job.Value)
            .Any(line => IsUncommented(line, "ci-receipts.sh record coverage"));

        workflowWritesTheCoverageReceipt.Should().BeFalse(
            "the coverage receipt must be written by scripts/coverage-gate.sh, after it has compared "
            + "the floors. Written from the workflow it would prove only that the workflow reached a "
            + "line, which is true of a step whose command does nothing");
    }

    /// <summary>
    /// The receipt name a <c>ci-receipts.sh record</c> or <c>verify</c> line passes, as written.
    /// </summary>
    private static string ReceiptArgument(string line, string verb)
    {
        var after = line[(line.IndexOf("ci-receipts.sh " + verb, StringComparison.Ordinal)
            + ("ci-receipts.sh " + verb).Length)..].Trim();

        return after.Trim('"').Trim();
    }

    /// <summary>
    /// True when <paramref name="line"/> contains <paramref name="text"/> outside a YAML comment.
    /// </summary>
    private static bool IsUncommented(string line, string text)
        => !line.TrimStart().StartsWith('#') && line.Contains(text, StringComparison.Ordinal);

    /// <summary>
    /// Splits one job's lines into its steps, as step name to the lines belonging to it.
    /// </summary>
    private static List<(string Name, List<string> Lines)> WorkflowSteps(IEnumerable<string> jobLines)
    {
        var steps = new List<(string, List<string>)>();
        var current = (List<string>?)null;

        foreach (var line in jobLines)
        {
            var header = Regex.Match(
                line,
                @"^\s+- name:\s*(?<name>.+)$",
                RegexOptions.None,
                TimeSpan.FromSeconds(5));

            if (header.Success)
            {
                current = new List<string>();
                steps.Add((header.Groups["name"].Value.Trim(), current));
                continue;
            }

            current?.Add(line);
        }

        return steps;
    }

    /// <summary>
    /// True when this workflow line hands <paramref name="leg"/> to <c>scripts/ci-projects.sh</c>,
    /// resolving <c>${{ matrix.head }}</c> against the matrix's declared heads.
    /// </summary>
    private static bool InvokesLeg(string line, string leg, IReadOnlyCollection<string> heads)
    {
        if (line.TrimStart().StartsWith('#') || !line.Contains("ci-projects.sh", StringComparison.Ordinal))
        {
            return false;
        }

        // Everything after the script name up to the closing paren of the command substitution.
        // `${{ matrix.head }}` contains braces but no parenthesis, so this survives it.
        var argument = line[(line.IndexOf("ci-projects.sh", StringComparison.Ordinal) + "ci-projects.sh".Length)..]
            .Split(')')[0]
            .Trim();

        return argument == leg
            || (argument.Contains("matrix.head", StringComparison.Ordinal) && heads.Contains(leg));
    }

    /// <summary>
    /// The workflow's jobs, as job name to the lines belonging to it.
    /// </summary>
    /// <remarks>
    /// Text, not a YAML parser: this repository references no YAML library, and adding one so a test
    /// can read four job names would be a dependency taken for a convenience. A job is a line
    /// indented by exactly two spaces under <c>jobs:</c> and ending in a colon; the job owns every
    /// line until the next one.
    /// </remarks>
    private static Dictionary<string, List<string>> WorkflowJobs()
    {
        var lines = File.ReadAllLines(
            Path.Combine(RepositoryRoot(), ".github", "workflows", "dotnet-ci.yml"));

        var jobs = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var current = (List<string>?)null;
        var inJobs = false;

        foreach (var line in lines)
        {
            if (line.StartsWith("jobs:", StringComparison.Ordinal))
            {
                inJobs = true;
                continue;
            }

            if (!inJobs)
            {
                continue;
            }

            var isJobHeader = Regex.IsMatch(
                line,
                @"^  [A-Za-z][A-Za-z0-9_-]*:\s*$",
                RegexOptions.None,
                TimeSpan.FromSeconds(5));

            if (isJobHeader)
            {
                current = new List<string>();
                jobs[line.Trim().TrimEnd(':')] = current;
                continue;
            }

            current?.Add(line);
        }

        return jobs;
    }

    /// <summary>
    /// Runs <c>scripts/ci-projects.sh</c> for one leg and returns the projects it names, failing the
    /// test with the script's own stderr if it refuses.
    /// </summary>
    private static string[] CiProjectsFor(string leg)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(BashExecutable())
            {
                WorkingDirectory = RepositoryRoot(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };

        process.StartInfo.ArgumentList.Add("scripts/ci-projects.sh");
        process.StartInfo.ArgumentList.Add(leg);

        process.Start();

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();

        process.WaitForExit(milliseconds: 60_000).Should().BeTrue(
            "scripts/ci-projects.sh reads a handful of small files and must not hang");

        process.ExitCode.Should().Be(
            0,
            "scripts/ci-projects.sh refused the '{0}' leg. Its own message: {1}",
            leg,
            string.IsNullOrWhiteSpace(error) ? "(nothing on stderr)" : error.Trim());

        return output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();
    }

    /// <summary>
    /// Finds a <c>bash</c> that understands this repository's paths.
    /// </summary>
    /// <remarks>
    /// On Windows, <c>bash</c> on the PATH may be WSL's, which would resolve <c>J:\Kiroku\Pine</c>
    /// into a filesystem the script cannot see. Git for Windows ships the bash that every other
    /// bash-shaped thing in this repository is run under, so it is preferred explicitly and the PATH
    /// is only the fallback.
    /// </remarks>
    private static string BashExecutable()
    {
        if (!OperatingSystem.IsWindows())
        {
            return "bash";
        }

        var candidates = new[]
        {
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Git", "bin", "bash.exe"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Git", "usr", "bin", "bash.exe"),
        };

        return candidates.FirstOrDefault(File.Exists) ?? "bash";
    }

    private readonly record struct PackageReferenceEntry(string Name, string? Version);

    private static IEnumerable<PackageReferenceEntry> PackageReferences(string projectPath)
    {
        foreach (var element in XDocument.Load(projectPath).Descendants("PackageReference"))
        {
            var name = element.Attribute("Include")?.Value ?? element.Attribute("Update")?.Value;

            if (!string.IsNullOrWhiteSpace(name))
            {
                yield return new PackageReferenceEntry(name!.Trim(), element.Attribute("Version")?.Value);
            }
        }
    }

    private static Dictionary<string, string> CentralVersions()
    {
        var path = Path.Combine(RepositoryRoot(), "Directory.Packages.props");

        return XDocument
            .Load(path)
            .Descendants("PackageVersion")
            .Where(element => element.Attribute("Include") is not null)
            .ToDictionary(
                element => element.Attribute("Include")!.Value.Trim(),
                element => element.Attribute("Version")?.Value.Trim() ?? string.Empty,
                StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Every <c>.csproj</c> tracked in the repository, excluding build output.
    /// </summary>
    /// <remarks>
    /// The whole tree is scanned rather than the solution's project list, deliberately: a project
    /// outside the solution is precisely the case <see cref="EveryProjectFileIsInTheSolution"/>
    /// exists to catch, and a scan seeded from the solution could not see it.
    /// </remarks>
    private static string[] ProjectFiles()
    {
        var root = RepositoryRoot();

        var projects = Directory
            .GetFiles(root, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        projects.Should().NotBeEmpty("the repository contains project files; finding none means the scan is broken");

        return projects;
    }

    /// <summary>
    /// TD-115. The capability table's stated reason is checked against the package graph it claims
    /// to describe.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHAT TD-115 ASKED FOR AND WHAT THIS ACTUALLY DELIVERS. The row's complaint is that
    /// <c>PineHeadCapabilities</c> makes a claim about which native assets exist and nothing checks
    /// it, so "if a runtime package is added for another RID, the table silently becomes wrong in
    /// the direction that withholds a working feature". That specific drift is what this closes:
    /// add <c>OpenCvSharp4.runtime.osx.arm64</c> to the pins and this test fails until the table is
    /// revisited. It does NOT verify a published artefact per RID, which is the rest of the row.
    /// </para>
    /// <para>
    /// THE OTHER HALF OF THE TABLE'S OLD REASONING WAS FALSE AND IS ASSERTED AGAINST HERE. The
    /// Windows arm of the table used to say it was "the one head whose Assets/Ai/*.onnx models are
    /// copied to the output". The model is a <c>&lt;Content CopyToOutputDirectory&gt;</c> item in
    /// <c>Pine.Infrastructure.csproj</c> and every head has a <c>ProjectReference</c> to that
    /// project, so MSBuild copies it into every head's output -- confirmed in iteration 31 by
    /// listing three heads' build folders. Vision is Windows-only because of the two RID-pinned
    /// NATIVE packages and nothing else, and this test states that in the only place that can go
    /// stale silently.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheOnlyRidPinnedNativeRuntimes_AreTheWindowsOnes_WhichIsWhatTheCapabilityTableClaims()
    {
        var pins = File.ReadAllText(Path.Combine(RepositoryRoot(), "Directory.Packages.props"));

        var declared = CentrallyPinnedPackageId()
            .Matches(pins)
            .Select(match => match.Groups["id"].Value)
            .ToArray();

        declared.Should().NotBeEmpty("central package management is where every version lives");

        var ridPinned = declared
            .Where(id => id.Contains(".runtime.", StringComparison.OrdinalIgnoreCase)
                         || id.Contains(".official.runtime.", StringComparison.OrdinalIgnoreCase))
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        ridPinned.Should().Equal(
            ExpectedRidPinnedNativeRuntimes,
            "PineHeadCapabilities says receipt vision exists on the Windows head and nowhere else, and "
            + "these two win-only native runtime packages are the entire reason. Adding an osx, linux, "
            + "android or ios runtime here without revisiting that table would leave it claiming a "
            + "capability is absent on a head that had just gained it -- TD-115");
    }

    /// <summary>
    /// TD-115, the corrected half: the ONNX model is shipped by the shared project, so it reaches
    /// every head.
    /// </summary>
    /// <remarks>
    /// A head that declared its own copy of the model would make the old, false reason true again
    /// by accident. The asset belongs to <c>Pine.Infrastructure</c>, which is the project that
    /// reads it.
    /// </remarks>
    [Fact]
    public void TheModelAsset_IsShippedByTheSharedProject_AndByNoHead()
    {
        var infrastructure = File.ReadAllText(
            Path.Combine(RepositoryRoot(), "src", "Pine.Infrastructure", "Pine.Infrastructure.csproj"));

        infrastructure.Should().Contain("yolo-receipt-nano.onnx",
            "the project that reads the model is the project that ships it");

        foreach (var head in ShippedHeads)
        {
            var project = File.ReadAllText(
                Path.Combine(RepositoryRoot(), "src", head, $"{head}.csproj"));

            project.Should().NotContain(".onnx",
                $"'{head}' must not ship model assets of its own: the model already reaches every head "
                + "through the Pine.Infrastructure ProjectReference, which is why 'only Windows copies "
                + "the ONNX models' was never a true reason for the capability table");

            project.Should().Contain("Pine.Infrastructure.csproj",
                $"'{head}' reaches the model, and everything else, through the shared project");
        }
    }

    /// <summary>
    /// The two win-only native runtime packages the capability table's Windows row rests on.
    /// </summary>
    private static readonly string[] ExpectedRidPinnedNativeRuntimes =
        ["OpenCvSharp4.runtime.win", "Sdcb.PaddleInference.runtime.win64.mkl"];

    /// <summary>The four heads that survived the Phase 3 platform decision (iteration 31).</summary>
    private static readonly string[] ShippedHeads =
        ["Pine.Ui.Windows", "Pine.Ui.Macos", "Pine.Ui.Android", "Pine.Ui.Ios"];

    [GeneratedRegex(@"<PackageVersion\s+Include=""(?<id>[^""]+)""")]
    private static partial Regex CentrallyPinnedPackageId();

    private static string Relative(string path) =>
        Path.GetRelativePath(RepositoryRoot(), path).Replace('\\', '/');

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
            + "be located. These tests read the repository's own build files and cannot run outside it.");
    }
}
