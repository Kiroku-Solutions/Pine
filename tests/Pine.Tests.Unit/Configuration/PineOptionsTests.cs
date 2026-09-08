using System;
using System.Linq;
using FluentAssertions;
using Pine.Core.Configuration;
using Pine.Core.Security;
using Xunit;

namespace Pine.Tests.Unit.Configuration;

/// <summary>
/// The defaults <see cref="PineOptions"/> ships with, and the refusals it produces (TD-27).
/// </summary>
/// <remarks>
/// <para>
/// THE DEFAULTS ARE ASSERTED AGAINST THE CONSTANTS THEY REPLACED, not against themselves. A
/// configuration system's first duty is to change nothing: every value below was a literal
/// somewhere in the code an hour ago, and if binding an empty configuration produced anything else,
/// this change would have silently altered behaviour for every existing installation while
/// appearing to be a refactor.
/// </para>
/// </remarks>
public class PineOptionsTests
{
    [Fact]
    public void AnUnconfiguredPine_ShouldBehaveExactlyAsItDidBeforeConfigurationExisted()
    {
        var options = new PineOptions();

        // The Argon2id profile every existing vault was minted with. If this drifts, new vaults get
        // a different profile than the documented one -- which is legal, and must be deliberate.
        options.Security.NewVaultKdfMemoryKib.Should().Be(KdfParameters.Argon2idV1.MemoryKib);
        options.Security.NewVaultKdfIterations.Should().Be(KdfParameters.Argon2idV1.Iterations);
        options.Security.NewVaultKdfParallelism.Should().Be(KdfParameters.Argon2idV1.Parallelism);

        // Was VaultBackupService.DefaultBackupsToKeep.
        options.Security.BackupsToKeep.Should().Be(5);

        // Was VaultAutoLockOptions.DefaultIdleTimeout.
        options.Security.AutoLockIdleTimeout.Should().Be(TimeSpan.FromMinutes(10));

        // Was the literal 0.3, in two files.
        options.Ai.CategorisationConfidenceThreshold.Should().Be(0.3);

        // Was MlNetCategorizer.MinimumTrainingRows and CategorizationTrainingBackgroundService's
        // BatchThreshold -- two different constants that happened to be the same number.
        options.Ai.MinimumTrainingRows.Should().Be(10);
        options.Ai.TrainingBatchThreshold.Should().Be(10);
        options.Ai.TrainingStartupDelay.Should().Be(TimeSpan.FromMinutes(1));
        options.Ai.TrainingInterval.Should().Be(TimeSpan.FromMinutes(5));

        // Was TransactionsViewModel's const 30 -- NOT GetTransactionsQuery's 50, which no user ever
        // saw. See PineUiOptions.TransactionsPageSize's remarks.
        options.Ui.TransactionsPageSize.Should().Be(30);
        options.Ui.DiagnosticsLogEntryLimit.Should().Be(200);

        options.Validate().Should().BeEmpty("the shipped defaults must be a usable configuration");
    }

    [Fact]
    public void TheDataDirectory_ShouldBeTheOneDefinitionOfWherePineKeepsItsData()
    {
        var storage = new PineStorageOptions();

        storage.DataDirectory.Should().Be(PineStorageOptions.DefaultDataDirectory());
        storage.VaultsDirectory.Should().Be(System.IO.Path.Combine(storage.DataDirectory, "Vaults"));
        storage.LogsDirectory.Should().Be(System.IO.Path.Combine(storage.DataDirectory, "logs"));
    }

    /// <summary>
    /// A relative data directory is refused rather than resolved.
    /// </summary>
    /// <remarks>
    /// It would resolve against the process working directory, which differs between a shortcut, a
    /// debugger and a shell -- so the symptom of accepting one is a second, empty vault list rather
    /// than an error, and the user's vaults appear to have vanished.
    /// </remarks>
    [Fact]
    public void ARelativeDataDirectory_ShouldBeRefused()
    {
        var storage = new PineStorageOptions { DataDirectory = "pine-data" };

        storage.Validate().Should().ContainSingle()
            .Which.Should().Contain("absolute").And.Contain("pine-data");
    }

    [Theory]
    [InlineData(7)]
    [InlineData(16 * 1024 * 1024 + 1)]
    public void AnOutOfRangeKdfMemoryCost_ShouldBeRefused(int memoryKib)
    {
        var security = new PineSecurityOptions { NewVaultKdfMemoryKib = memoryKib };

        security.Validate().Should().ContainSingle()
            .Which.Should().Contain("NewVaultKdfMemoryKib");
    }

    [Fact]
    public void ABackupRetentionOfZero_ShouldBeRefused()
    {
        var security = new PineSecurityOptions { BackupsToKeep = 0 };

        security.Validate().Should().ContainSingle()
            .Which.Should().Contain("cannot be recovered");
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void AConfidenceThresholdOutsideZeroToOne_ShouldBeRefused(double threshold)
    {
        var ai = new PineAiOptions { CategorisationConfidenceThreshold = threshold };

        ai.Validate().Should().ContainSingle()
            .Which.Should().Contain("probability");
    }

    /// <summary>
    /// A zero training interval is refused here rather than inside the background service.
    /// </summary>
    /// <remarks>
    /// <c>PeriodicTimer</c> throws on a non-positive period, and it would do so inside
    /// <c>ExecuteAsync</c> on a background thread, where the failure is a silently dead trainer
    /// rather than a startup refusal.
    /// </remarks>
    [Fact]
    public void AZeroTrainingInterval_ShouldBeRefused()
    {
        var ai = new PineAiOptions { TrainingInterval = TimeSpan.Zero };

        ai.Validate().Should().ContainSingle()
            .Which.Should().Contain("TrainingInterval");
    }

    /// <summary>
    /// Every problem is reported at once, not the first one.
    /// </summary>
    /// <remarks>
    /// A person fixing a configuration file should need one round trip, not five. This is the only
    /// property of the validation design that a caller can actually observe, so it is the one worth
    /// pinning.
    /// </remarks>
    [Fact]
    public void ManyProblems_ShouldAllBeReportedTogether()
    {
        var options = new PineOptions
        {
            Storage = new PineStorageOptions { DataDirectory = "relative" },
            Security = new PineSecurityOptions { BackupsToKeep = 0, NewVaultKdfIterations = 0 },
            Ai = new PineAiOptions { CategorisationConfidenceThreshold = 9 },
            Ui = new PineUiOptions { TransactionsPageSize = 0 },
        };

        var problems = options.Validate();

        problems.Should().HaveCount(5);
        problems.Should().Contain(p => p.Contains("DataDirectory", StringComparison.Ordinal));
        problems.Should().Contain(p => p.Contains("BackupsToKeep", StringComparison.Ordinal));
        problems.Should().Contain(p => p.Contains("NewVaultKdfIterations", StringComparison.Ordinal));
        problems.Should().Contain(p => p.Contains("CategorisationConfidenceThreshold", StringComparison.Ordinal));
        problems.Should().Contain(p => p.Contains("TransactionsPageSize", StringComparison.Ordinal));
    }

    /// <summary>
    /// Every problem names its configuration key, so the sentence is actionable.
    /// </summary>
    [Fact]
    public void EveryProblem_ShouldNameTheConfigurationKeyThatCausedIt()
    {
        var options = new PineOptions
        {
            Storage = new PineStorageOptions { DataDirectory = string.Empty },
            Security = new PineSecurityOptions { NewVaultKdfParallelism = 0, AutoLockIdleTimeout = TimeSpan.FromDays(2) },
            Ai = new PineAiOptions { MinimumTrainingRows = 0 },
            Ui = new PineUiOptions { DiagnosticsLogEntryLimit = 0 },
        };

        options.Validate().Should().OnlyContain(problem => problem.StartsWith("Pine:", StringComparison.Ordinal));
    }
}
