using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Pine.Tests.Unit.Build;

/// <summary>
/// Re-proves the refusals in <c>packaging/package.sh</c> on every <c>dotnet test</c>, without
/// producing a 1 GiB package (TD-107).
/// </summary>
/// <remarks>
/// <para>
/// HOW. The real script is executed under bash with a stub <c>dotnet</c> first on the PATH. The stub
/// answers <c>msbuild -getProperty</c> with a configurable version and, on <c>publish</c>, drops a
/// fake <c>Pine.Cli</c> that prints a configurable version. So the code under test is the shipped
/// script, not a re-implementation of it, and a guard deleted from the script fails here.
/// </para>
/// <para>
/// WHAT IS EXECUTED: the early <c>--require-signature</c> refusal (and that it fires before any
/// build), the empty-version refusal, the missing-CLI refusal, the version-agreement refusal, the
/// RID-pinned self-contained publish arguments, and a passing control run (a gate that only ever
/// says no is indistinguishable from a broken one). WHAT IS ONLY PINNED AS TEXT: the branch that
/// reads <c>sign.sh</c>'s facts file back, because reaching it needs a certificate and signtool;
/// that remains covered by <c>packaging/selftest-signing.sh</c> on a machine that has them.
/// </para>
/// </remarks>
public sealed class PackagingScriptGuardTests : IDisposable
{
    private const string Version = "0.1.0-alpha.1";

    private const string StubDotnet =
        "#!/usr/bin/env bash\n"
        + "echo \"$*\" >> \"$FAKE_LOG\"\n"
        + "case \"$1\" in\n"
        + "  msbuild)\n"
        + "    printf '%s\\r\\n' \"$FAKE_VERSION\"\n"
        + "    ;;\n"
        + "  publish)\n"
        + "    out=\"\"; project=\"$2\"\n"
        + "    while [ $# -gt 0 ]; do\n"
        + "      if [ \"$1\" = \"-o\" ]; then out=\"$2\"; fi\n"
        + "      shift\n"
        + "    done\n"
        + "    mkdir -p \"$out\"\n"
        + "    echo x > \"$out/Pine.dll\"\n"
        + "    case \"$project\" in\n"
        + "      *Pine.Cli*)\n"
        + "        if [ \"$FAKE_CLI\" != \"missing\" ]; then\n"
        + "          printf '#!/usr/bin/env bash\\necho \"%s\"\\n' \"$FAKE_REPORTED\" > \"$out/Pine.Cli\"\n"
        + "          chmod +x \"$out/Pine.Cli\"\n"
        + "        fi\n"
        + "        ;;\n"
        + "    esac\n"
        + "    ;;\n"
        + "esac\n"
        + "exit 0\n";

    private readonly string _work = Path.Combine(Path.GetTempPath(), "pine-pkgguard-" + Guid.NewGuid().ToString("N"));

    public PackagingScriptGuardTests()
    {
        Directory.CreateDirectory(Path.Combine(_work, "bin"));
        Directory.CreateDirectory(Path.Combine(_work, "out"));
        File.WriteAllText(Path.Combine(_work, "bin", "dotnet"), StubDotnet);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_work, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record Run(int ExitCode, string Stdout, string Stderr, string[] DotnetCalls, string Out);

    private Run RunPackage(string version, string reported, bool cliMissing, params string[] arguments)
    {
        var log = Path.Combine(_work, "dotnet.log");
        File.Delete(log);
        var outDir = Path.Combine(_work, "out").Replace('\\', '/');

        var info = new ProcessStartInfo(BashExecutable())
        {
            WorkingDirectory = RepositoryRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        info.ArgumentList.Add("packaging/package.sh");
        info.ArgumentList.Add("--out");
        info.ArgumentList.Add(outDir);

        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        info.Environment["PATH"] = Path.Combine(_work, "bin") + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
        info.Environment["FAKE_LOG"] = log.Replace('\\', '/');
        info.Environment["FAKE_VERSION"] = version;
        info.Environment["FAKE_REPORTED"] = reported;
        info.Environment["FAKE_CLI"] = cliMissing ? "missing" : "present";
        info.Environment.Remove("PINE_SIGNTOOL");
        info.Environment.Remove("PINE_SIGNING_CERT");

        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        process.WaitForExit(120_000).Should().BeTrue("package.sh against a stub must finish quickly");

        var calls = File.Exists(log) ? File.ReadAllLines(log) : Array.Empty<string>();

        return new Run(process.ExitCode, stdout.Result, stderr.Result, calls, outDir);
    }

    [Fact]
    public void RequireSignatureWithoutACertificateRefusesBeforeBuildingAnything()
    {
        var run = RunPackage(Version, Version, cliMissing: false, "--require-signature");

        run.ExitCode.Should().Be(1, run.Stderr);
        run.Stderr.Should().Contain("--require-signature was given and no certificate is configured");
        run.DotnetCalls.Should().BeEmpty("the refusal is documented to happen before any build starts");
    }

    [Fact]
    public void AnUnresolvableVersionRefusesToProduceAnArtefact()
    {
        var run = RunPackage(version: string.Empty, reported: string.Empty, cliMissing: false);

        run.ExitCode.Should().Be(1, run.Stderr);
        run.Stderr.Should().Contain("could not resolve a version");
        run.DotnetCalls.Should().NotContain(call => call.StartsWith("publish", StringComparison.Ordinal));
    }

    [Fact]
    public void AMissingPublishedCliRefusesBecauseTheArtefactCannotBeAskedWhatItIs()
    {
        var run = RunPackage(Version, Version, cliMissing: true);

        run.ExitCode.Should().Be(1, run.Stderr);
        run.Stderr.Should().Contain("the published CLI is missing");
    }

    [Fact]
    public void AnArtefactWhoseVersionDisagreesWithItsFilenameIsRefused()
    {
        var run = RunPackage(Version, "9.9.9", cliMissing: false);

        run.ExitCode.Should().Be(1, run.Stderr);
        run.Stderr.Should().Contain("reports '9.9.9' but was built as '" + Version + "'");
        Directory.GetFiles(run.Out, "*.release.json").Should().BeEmpty("no manifest may describe a refused artefact");
    }

    [Fact]
    public void AMatchingBuildWithACommitSuffixIsAcceptedAndDescribedAsUnsigned()
    {
        var run = RunPackage(Version, Version + "+0123456789ab", cliMissing: false);

        run.ExitCode.Should().Be(0, run.Stderr + run.Stdout);

        var manifest = Directory.GetFiles(run.Out, "*.release.json").Should().ContainSingle().Subject;
        using var json = JsonDocument.Parse(File.ReadAllText(manifest));

        json.RootElement.GetProperty("version").GetString().Should().Be(Version);
        json.RootElement.GetProperty("signed").GetBoolean().Should().BeFalse("no certificate was configured");
        json.RootElement.GetProperty("signature").ValueKind.Should().Be(JsonValueKind.Null);
        json.RootElement.GetProperty("artifacts")[0].GetProperty("rid").GetString().Should().Be("win-x64");
    }

    [Fact]
    public void BothHeadsArePublishedSelfContainedAndPinnedToOneRuntimeIdentifier()
    {
        var run = RunPackage(Version, Version, cliMissing: false);

        run.ExitCode.Should().Be(0, run.Stderr);

        var publishes = run.DotnetCalls.Where(call => call.StartsWith("publish", StringComparison.Ordinal)).ToArray();

        publishes.Should().HaveCount(2);
        publishes.Should().Contain(call => call.Contains("Pine.Ui.Windows.csproj", StringComparison.Ordinal));
        publishes.Should().Contain(call => call.Contains("Pine.Cli.csproj", StringComparison.Ordinal));
        publishes.Should().OnlyContain(
            call => call.Contains("--runtime win-x64", StringComparison.Ordinal)
                && call.Contains("--self-contained true", StringComparison.Ordinal),
            "an unpinned publish emits 30 RID folders (1.78 GiB) and a framework-dependent one needs a runtime");
    }

    [Fact]
    public void TheProjectsAndVersionSourceThePackageScriptNamesExistInTheRepository()
    {
        var script = File.ReadAllText(Path.Combine(RepositoryRoot(), "packaging", "package.sh"));

        foreach (var name in new[] { "APP_PROJECT", "CLI_PROJECT" })
        {
            var match = Regex.Match(script, name + "=\"(?<path>[^\"]+)\"", RegexOptions.None, TimeSpan.FromSeconds(5));
            match.Success.Should().BeTrue(name + " must be assigned in package.sh");
            File.Exists(Path.Combine(RepositoryRoot(), match.Groups["path"].Value)).Should().BeTrue(
                name + " names a project that must exist");
        }

        var props = File.ReadAllText(Path.Combine(RepositoryRoot(), "Directory.Build.props"));
        props.Should().MatchRegex(
            @"<VersionPrefix>\d+\.\d+\.\d+</VersionPrefix>",
            "package.sh asks MSBuild for Version, which Directory.Build.props is the single source of");
    }

    [Theory]
    [InlineData("!= \"true\" ] || [ -z \"$SIGNING_THUMBPRINT\" ]", "a signature without signed=true or a thumbprint is refused")]
    [InlineData("sign.sh reported success and wrote no facts file", "a missing facts file is refused")]
    [InlineData("THE SIGNING CERTIFICATE DOES NOT CHAIN TO A TRUSTED ROOT", "untrusted roots are carried into the manifest")]
    public void TheSigningFactsReadBackGuardsAreStillInTheScript(string fragment, string because)
    {
        var script = File.ReadAllText(Path.Combine(RepositoryRoot(), "packaging", "package.sh"));

        script.Should().Contain(fragment, because);
    }

    private static string BashExecutable()
    {
        if (!OperatingSystem.IsWindows())
        {
            return "bash";
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var candidates = new[]
        {
            Path.Combine(programFiles, "Git", "bin", "bash.exe"),
            Path.Combine(programFiles, "Git", "usr", "bin", "bash.exe"),
        };

        return candidates.FirstOrDefault(File.Exists) ?? "bash";
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

        throw new DirectoryNotFoundException("Could not find Pine.slnx above " + AppContext.BaseDirectory);
    }
}
