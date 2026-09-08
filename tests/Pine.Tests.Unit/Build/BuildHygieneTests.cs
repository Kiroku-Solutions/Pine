using System;
using System.Collections.Generic;
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
public class BuildHygieneTests
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

    /// <summary>
    /// TD-41. Every file shipped from <c>Assets/Ai</c> must be named by production code.
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
    public void ShippedModelAssetsAreNamedByProductionCode()
    {
        var assetDirectory = Path.Combine(RepositoryRoot(), "src", "Pine.Infrastructure", "Assets", "Ai");

        Directory.Exists(assetDirectory).Should().BeTrue("the OCR ships a model from this folder");

        var assets = Directory
            .GetFiles(assetDirectory, "*", SearchOption.AllDirectories)
            .Select(Path.GetFileName)
            .Where(name => name is not null)
            .Select(name => name!)
            .ToArray();

        assets.Should().NotBeEmpty("finding no assets means the scan is broken, not that none ship");

        var sources = Directory
            .GetFiles(Path.Combine(RepositoryRoot(), "src"), "*.cs", SearchOption.AllDirectories)
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
