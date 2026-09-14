using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Pine.Core.Common;
using Pine.Core.Configuration;
using Pine.Ui.Avalonia.Composition;
using Xunit;

namespace Pine.Tests.Unit.Configuration;

/// <summary>
/// Binding configuration onto <see cref="PineOptions"/>, and refusing it when it cannot be used
/// (TD-27).
/// </summary>
/// <remarks>
/// These build an in-memory <see cref="IConfiguration"/> rather than calling
/// <c>PineConfiguration.Build()</c>. `Build()` reads a real file path and the real environment, so a
/// test of it would depend on the developer's machine and would pass or fail for reasons that have
/// nothing to do with Pine. What is testable — and what actually carries the risk — is the binding
/// and the validation, which is everything between a key and a value in use.
/// </remarks>
public class PineConfigurationTests
{
    private static IConfiguration ConfigurationFrom(params (string Key, string Value)[] entries)
    {
        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in entries)
        {
            data[key] = value;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(data).Build();
    }

    [Fact]
    public void AnEmptyConfiguration_ShouldLeaveEveryDefaultInPlace()
    {
        var bound = PineConfiguration.Bind(ConfigurationFrom());

        bound.Should().BeEquivalentTo(new PineOptions());
    }

    /// <summary>
    /// A file naming one key changes one value.
    /// </summary>
    /// <remarks>
    /// This is the property that makes a partial configuration file safe to write. Binding onto an
    /// instance that already carries the defaults gives it; binding onto a fresh, zeroed object
    /// would silently reset every key the file did not mention — and the symptom would be a page
    /// size of zero because someone wanted to change a log directory.
    /// </remarks>
    [Fact]
    public void AConfigurationNamingOneKey_ShouldChangeOnlyThatValue()
    {
        var bound = PineConfiguration.Bind(
            ConfigurationFrom(("Pine:Ai:CategorisationConfidenceThreshold", "0.75")));

        bound.Ai.CategorisationConfidenceThreshold.Should().Be(0.75);

        bound.Ai.MinimumTrainingRows.Should().Be(10);
        bound.Ui.TransactionsPageSize.Should().Be(30);
        bound.Security.BackupsToKeep.Should().Be(5);
    }

    [Fact]
    public void ATimeSpanValue_ShouldBindFromItsUsualTextForm()
    {
        var bound = PineConfiguration.Bind(
            ConfigurationFrom(("Pine:Security:AutoLockIdleTimeout", "00:02:00")));

        bound.Security.AutoLockIdleTimeout.Should().Be(TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void TheKdfProfileForNewVaults_ShouldBeConfigurable()
    {
        var bound = PineConfiguration.Bind(ConfigurationFrom(
            ("Pine:Security:NewVaultKdfMemoryKib", "131072"),
            ("Pine:Security:NewVaultKdfIterations", "4")));

        bound.Security.NewVaultKdfMemoryKib.Should().Be(131_072);
        bound.Security.NewVaultKdfIterations.Should().Be(4);
        bound.Security.NewVaultKdfParallelism.Should().Be(4, "an unnamed key keeps its default");
    }

    /// <summary>
    /// An unusable configuration is refused at bind time, with every problem in one exception.
    /// </summary>
    [Fact]
    public void AnUnusableConfiguration_ShouldBeRefusedWithEveryProblemAtOnce()
    {
        var bind = () => PineConfiguration.Bind(ConfigurationFrom(
            ("Pine:Ai:CategorisationConfidenceThreshold", "5"),
            ("Pine:Ui:TransactionsPageSize", "0")));

        var thrown = bind.Should().Throw<PineOptionsValidationException>().Which;

        thrown.Problems.Should().HaveCount(2);
        thrown.Message.Should().Contain("CategorisationConfidenceThreshold").And.Contain("TransactionsPageSize");
    }

    /// <summary>
    /// The refusal is safe to show, and says so through the taxonomy rather than by convention.
    /// </summary>
    /// <remarks>
    /// Every sentence names a configuration key, a bound and the value that was read — three things
    /// the reader typed themselves, so none of them is a disclosure. That is exactly the test
    /// <see cref="IUserFacingFailure"/>'s remarks set, which is why this exception is allowed on it
    /// when most are not.
    /// </remarks>
    [Fact]
    public void TheRefusal_ShouldNameTheKey_InTheMessageTheCrashLogRecords()
    {
        var bind = () => PineConfiguration.Bind(ConfigurationFrom(("Pine:Security:BackupsToKeep", "0")));

        var thrown = bind.Should().Throw<PineOptionsValidationException>().Which;

        // Off the user-facing taxonomy since TD-139: it is thrown while the container is built, before
        // any screen exists, and no path from it reaches UserFacingError. See the type's remarks.
        thrown.Should().NotBeAssignableTo<IUserFacingFailure>();
        thrown.Message.Should().Contain("BackupsToKeep");
    }

    /// <summary>
    /// The override hook runs after binding and is still validated.
    /// </summary>
    /// <remarks>
    /// It exists so a test or a head can set a value without writing a file. If it ran after
    /// validation it would be a hole straight through the guard.
    /// </remarks>
    [Fact]
    public void TheOverrideHook_ShouldRunAfterBinding_AndStillBeValidated()
    {
        var bound = PineConfiguration.Bind(
            ConfigurationFrom(("Pine:Ui:TransactionsPageSize", "10")),
            options => options.Ui.TransactionsPageSize = 25);

        bound.Ui.TransactionsPageSize.Should().Be(25);

        var invalid = () => PineConfiguration.Bind(
            ConfigurationFrom(),
            options => options.Ui.TransactionsPageSize = 0);

        invalid.Should().Throw<PineOptionsValidationException>();
    }

    /// <summary>
    /// The configuration file is looked for in the data directory, not beside the executable.
    /// </summary>
    /// <remarks>
    /// <c>packaging/package.sh</c> checksums every file it ships and records the list in a
    /// manifest, so a config file inside the artefact would be a file whose purpose is to be edited
    /// and whose edit breaks the artefact's own integrity claim. Pinning the location here means
    /// moving it back into the package is a visible decision.
    /// </remarks>
    [Fact]
    public void TheConfigurationFile_ShouldLiveOutsideTheSignedArtefact()
    {
        PineConfiguration.FilePath.Should().StartWith(PineStorageOptions.DefaultDataDirectory());
        PineConfiguration.FilePath.Should().EndWith("appsettings.json");
        PineConfiguration.FilePath.Should().NotStartWith(AppContext.BaseDirectory);
    }

    [Fact]
    public void TheSources_ShouldBeDescribable_WithoutNamingAnyValue()
    {
        var sources = PineConfiguration.DescribeSources();

        sources.Should().HaveCount(3);
        sources.Should().Contain(s => s.Contains("defaults", StringComparison.Ordinal));
        sources.Should().Contain(s => s.Contains("appsettings.json", StringComparison.Ordinal));
        sources.Should().Contain(s => s.Contains("PINE_", StringComparison.Ordinal));

        // The description reaches a log, and a data directory is a path the user chose.
        sources.Should().NotContain(s => s.Contains(PineStorageOptions.DefaultDataDirectory(), StringComparison.Ordinal));
    }

    /// <summary>
    /// The misspelling TD-117 names changes nothing, so it is refused, and the refusal says what was meant.
    /// </summary>
    [Fact]
    public void AMisspelledKey_ShouldBeRefused_NamingTheKeyThatWasMeant()
    {
        var bind = () => PineConfiguration.Bind(ConfigurationFrom(("Pine:Ai:ConfidenceThreshold", "0.75")));

        var thrown = bind.Should().Throw<PineOptionsValidationException>().Which;

        thrown.Problems.Should().ContainSingle().Which.Should()
            .StartWith("Pine:Ai:ConfidenceThreshold is not a setting Pine reads")
            .And.EndWith("Did you mean Pine:Ai:CategorisationConfidenceThreshold?");
    }

    [Theory]
    [InlineData("Pine:Security:BackupsToKep", "Did you mean Pine:Security:BackupsToKeep?")]
    [InlineData("Pine:Secruity:BackupsToKeep", "Did you mean Pine:Security?")]
    [InlineData("Pine:Ui:Theme", "The settings in Pine:Ui are: DiagnosticsLogEntryLimit, TransactionsPageSize.")]
    [InlineData("Pine:Storage:VaultsDirectory", "Did you mean Pine:Storage:VaultsDirectoryName?")]
    [InlineData("Pine:Ai:CategorisationConfidenceThreshold:Value", "is a single value, so the keys beneath it would be ignored")]
    [InlineData("Ai:CategorisationConfidenceThreshold", "Did you mean Pine:Ai? As an environment variable, that is PINE_Pine__Ai__<setting>.")]
    public void EveryShapeOfUnknownKey_ShouldBeRefused(string key, string expected)
    {
        var problems = PineConfiguration.FindUnknownKeys(ConfigurationFrom((key, "1")));

        problems.Should().ContainSingle().Which.Should().Contain(expected);
    }

    /// <summary>
    /// Keys are matched the way the binder matches them: case does not make a key unknown.
    /// </summary>
    [Fact]
    public void AKnownKeyInAnotherCase_ShouldNotBeRefused()
    {
        var bound = PineConfiguration.Bind(ConfigurationFrom(("pine:ai:categorisationconfidencethreshold", "0.75")));

        bound.Ai.CategorisationConfidenceThreshold.Should().Be(0.75);
    }

    /// <summary>
    /// The <c>PINE_</c> prefix is shared with the packaging scripts, so Pine must not refuse to start on a
    /// machine that has built a signed release.
    /// </summary>
    [Fact]
    public void TheSigningScriptsEnvironmentVariables_ShouldNotStopPineStarting()
    {
        var configuration = ConfigurationFrom(
            ("SIGNTOOL", @"C:\sdk\signtool.exe"),
            ("SIGNING_CERT", @"C:\temp\cert.pfx"),
            ("TIMESTAMP_URL", "http://timestamp.example"));

        PineConfiguration.FindUnknownKeys(configuration).Should().BeEmpty();
        var bind = () => PineConfiguration.Bind(configuration);
        bind.Should().NotThrow();
    }

    /// <summary>
    /// Unknown keys and out-of-bounds values are reported together, so one edit fixes a file.
    /// </summary>
    [Fact]
    public void UnknownKeysAndBadValues_ShouldBeReportedInOneRefusal()
    {
        var bind = () => PineConfiguration.Bind(ConfigurationFrom(
            ("Pine:Ai:ConfidenceThreshold", "0.75"),
            ("Pine:Ui:TransactionsPageSize", "0")));

        bind.Should().Throw<PineOptionsValidationException>().Which.Problems.Should().HaveCount(2);
    }

    /// <summary>
    /// Every setting the options tree has is accepted, so the check cannot refuse a correct file. The
    /// keys are derived from the example file's leaves rather than restated here.
    /// </summary>
    [Fact]
    public void TheExampleFile_ShouldBeAcceptedAsIs_AndChangeNothing()
    {
        var configuration = new ConfigurationBuilder().AddJsonFile(ExampleFilePath(), optional: false).Build();

        PineConfiguration.FindUnknownKeys(configuration).Should().BeEmpty();
        PineConfiguration.Bind(configuration).Should().BeEquivalentTo(new PineOptions(),
            "the example documents the defaults; copying it whole must not change a single value");
    }

    /// <summary>
    /// The example names every setting there is, so adding a setting without documenting it fails here.
    /// </summary>
    [Fact]
    public void TheExampleFile_ShouldNameEverySetting()
    {
        var text = System.IO.File.ReadAllText(ExampleFilePath());

        foreach (var (section, property) in Leaves())
        {
            text.Should().Contain($"\"{section}\"", "the example has a block for every section");
            text.Should().Contain($"\"{property}\"", $"Pine:{section}:{property} is a setting and the example must name it");
        }
    }

    private static IEnumerable<(string Section, string Property)> Leaves() =>
        typeof(PineOptions).GetProperties()
            .Where(section => section.SetMethod is { IsPublic: true })
            .SelectMany(section => section.PropertyType.GetProperties()
                .Where(property => property.SetMethod is { IsPublic: true })
                .Select(property => (section.Name, property.Name)));

    private static string ExampleFilePath()
    {
        var directory = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !System.IO.File.Exists(System.IO.Path.Combine(directory.FullName, "Pine.slnx")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the test reads the repository's example file");
        return System.IO.Path.Combine(directory!.FullName, "packaging", "appsettings.example.json");
    }
}
