using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Pine.Tests.Unit.Build;

/// <summary>
/// Guards TD-46 (model files must be anchored to the app base directory, never the process working
/// directory), TD-51 (no stream-of-consciousness comments) and TD-55 (NuGet audit configured so a
/// new advisory cannot turn an unchanged build red).
/// </summary>
public sealed partial class ModelPathAndAuditHygieneTests
{
    [Fact]
    public void ShippedModelPathsAreAnchoredToTheAppBaseDirectoryNotTheWorkingDirectory()
    {
        var offenders = new List<string>();

        foreach (var file in SourceFiles("src/Pine.Infrastructure/Ai"))
        {
            var text = File.ReadAllText(file);
            foreach (var line in text.Split('\n'))
            {
                var code = line.TrimStart();
                if (code.StartsWith("//", StringComparison.Ordinal) || code.StartsWith("///", StringComparison.Ordinal))
                {
                    continue;
                }

                if (RelativeModelLiteral().IsMatch(code) && !code.Contains("AppContext.BaseDirectory", StringComparison.Ordinal))
                {
                    offenders.Add($"{Path.GetRelativePath(RepositoryRoot(), file).Replace(Path.DirectorySeparatorChar, '/')}: {code.Trim()}");
                }
            }
        }

        offenders.Should().BeEmpty("a relative model path resolves against the CWD (TD-46)");
    }

    [Fact]
    public void ReceiptDetectorModelPathIsIndependentOfTheWorkingDirectory()
    {
        var original = Directory.GetCurrentDirectory();
        var elsewhere = Path.Combine(Path.GetTempPath(), "pine-cwd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(elsewhere);

        try
        {
            Directory.SetCurrentDirectory(elsewhere);
            var field = typeof(Pine.Infrastructure.Ai.Vision.ReceiptDetector)
                .GetField("YoloModelPath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

            field.Should().NotBeNull();
            var path = (string)field!.GetValue(null)!;
            Path.IsPathRooted(path).Should().BeTrue();
            path.Should().StartWith(AppContext.BaseDirectory);
        }
        finally
        {
            Directory.SetCurrentDirectory(original);
            Directory.Delete(elsewhere, recursive: true);
        }
    }

    [Fact]
    public void NoDraftCommentsRemainInSourceOrSolution()
    {
        var banned = new[] { "Wait, source needs", "should the line item totals be negative", "asdasda" };
        var files = SourceFiles("src").Append(Path.Combine(RepositoryRoot(), "Pine.slnx"));

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (var phrase in banned)
            {
                text.Contains(phrase, StringComparison.OrdinalIgnoreCase)
                    .Should().BeFalse($"'{phrase}' is a draft comment (TD-51) in {file}");
            }
        }
    }

    [Fact]
    public void NuGetAuditIsOnButItsAdvisoryWarningsAreNotPromotedToErrors()
    {
        var props = File.ReadAllText(Path.Combine(RepositoryRoot(), "Directory.Build.props"));

        props.Should().Contain("<NuGetAuditMode>all</NuGetAuditMode>");
        props.Should().Contain("<NuGetAudit>true</NuGetAudit>");
        foreach (var id in new[] { "NU1900", "NU1901", "NU1902", "NU1903", "NU1904" })
        {
            props.Should().Contain(id, "TreatWarningsAsErrors must not turn a new advisory into a red build (TD-55)");
        }
    }

    private static IEnumerable<string> SourceFiles(string relativeDirectory) =>
        Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), relativeDirectory), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    [GeneratedRegex(@"""[^""]*[/\\][^""]*\.(onnx|zip|model)""")]
    private static partial Regex RelativeModelLiteral();

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

        throw new DirectoryNotFoundException($"Could not find Pine.slnx above '{AppContext.BaseDirectory}'.");
    }
}
