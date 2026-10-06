using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace Pine.Tests.Integration.Data;

/// <summary>
/// TD-97's fix, proved directly rather than argued. <see cref="TempVault.WithRetries"/> is the seam
/// both <c>CreateDirectoryWithRetries</c> and <c>DeleteDirectoryWithRetries</c> share, and it is
/// <c>internal</c> rather than <c>private</c> for exactly this file: simulating the transient
/// Windows file-locking exception a sibling <see cref="TempVault"/>'s own create/delete causes does
/// not need to win an actual filesystem race.
/// </summary>
/// <remarks>
/// <para>
/// WHY A RETRY RATHER THAN SERIALISING THE VAULT-BACKED CLASSES INTO ONE COLLECTION. The fix column
/// this row has carried since iteration 15 offered both. Forty-four test classes construct a
/// <see cref="TempVault"/> (directly or through <c>AtomicityHost</c>), carrying 268 of this
/// project's 327 integration <c>[Fact]</c>/<c>[Theory]</c> cases -- 82% of the suite. Collecting all
/// of them into one xUnit collection would make that 82% single-threaded, which is not "serialise
/// the flaky part," it is "serialise the integration suite," for a contention window that, by this
/// row's own count, loses roughly once in four full runs. A bounded retry keeps the parallelism and
/// only ever spends its two seconds when the race is actually lost.
/// </para>
/// <para>
/// THE LAST TEST BELOW IS THE ONE THAT TRIES TO WIN THE RACE FOR REAL, rather than simulating it:
/// sixty-four <see cref="TempVault"/> constructors, fired at once, against the one shared
/// <c>%TEMP%\pine-tests</c> parent every vault in the suite hangs off. It is not guaranteed to
/// reproduce TD-97's contention on every machine -- that is the nature of a race -- but it is a
/// standing, repeatable attempt rather than a hope that a thirteen-times-observed flake shows up
/// again on its own schedule.
/// </para>
/// </remarks>
public partial class TempVaultRetryTests
{
    /// <summary>
    /// The structural half of the proof, because the race-for-real test above is probabilistic and,
    /// on this machine, did not lose the race against a mutant that quietly bypassed the retry (the
    /// constructor calling <c>Directory.CreateDirectory</c> straight, with <c>CreateDirectoryWithRetries</c>
    /// left in the file but unreferenced). This reads <c>TempVault.cs</c>'s own source and asserts the
    /// constructor routes through the retry helper rather than merely trusting a race to be lost in
    /// the time this suite runs.
    /// </summary>
    [Fact]
    public void Constructor_RoutesDirectoryCreationThroughTheRetryHelper_NotABareCreateDirectoryCall()
    {
        var path = Path.Combine(RepositoryRoot(), "tests", "Pine.Tests.Integration", "Data", "TempVault.cs");
        File.Exists(path).Should().BeTrue($"TempVault's own source should exist at '{path}'");

        var source = File.ReadAllText(path);
        var match = ConstructorBody().Match(source);
        match.Success.Should().BeTrue("TempVault's public parameterless constructor should be found in its own source");

        var body = match.Groups["body"].Value;

        body.Should().Contain(
            "CreateDirectoryWithRetries(VaultDirectory)",
            "the constructor must create its directory through the TD-97 retry helper -- a bare "
            + "Directory.CreateDirectory can take a transient IOException/UnauthorizedAccessException "
            + "from a sibling TempVault's own create or delete, under xUnit's default parallelism, "
            + "that a lone test run would never see");

        body.Should().NotContain(
            "Directory.CreateDirectory(",
            "a bare Directory.CreateDirectory call in the constructor bypasses the TD-97 retry "
            + "entirely, even sitting next to a call to the retry helper");
    }

    [GeneratedRegex(@"public TempVault\(\)\s*\{(?<body>.*?)\r?\n    \}", RegexOptions.Singleline)]
    private static partial Regex ConstructorBody();

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
            $"Could not find Pine.slnx above '{AppContext.BaseDirectory}', so the repository could "
            + "not be located. This test reads TempVault's own source and cannot run outside it.");
    }

    [Fact]
    public void WithRetries_RecoversFromATransientIOException_WithinTheBound()
    {
        var attempts = 0;
        const int failuresBeforeSuccess = 7; // comfortably inside the 20-attempt bound

        TempVault.WithRetries(
            () =>
            {
                attempts++;
                if (attempts <= failuresBeforeSuccess)
                {
                    throw new IOException("simulated transient sharing violation");
                }
            },
            failureMessage: "should not be thrown -- the action succeeds before the bound is reached");

        attempts.Should().Be(
            failuresBeforeSuccess + 1,
            "retrying must stop the instant the action succeeds, not run to the bound regardless");
    }

    [Fact]
    public void WithRetries_RecoversFromUnauthorizedAccessException_TheOtherExceptionTheDiagnosisNames()
    {
        var attempts = 0;

        TempVault.WithRetries(
            () =>
            {
                attempts++;
                if (attempts == 1)
                {
                    throw new UnauthorizedAccessException("simulated locked handle");
                }
            },
            failureMessage: "should not be thrown");

        attempts.Should().Be(2);
    }

    [Fact]
    public void WithRetries_IsBoundedNotInfinite_AndWrapsTheLastException()
    {
        var attempts = 0;
        Action act = () => TempVault.WithRetries(
            () =>
            {
                attempts++;
                throw new IOException($"simulated permanent failure #{attempts}");
            },
            failureMessage: "TD-97 guard: permanently locked");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("TD-97 guard: permanently locked")
            .WithInnerException<IOException>()
            .WithMessage($"simulated permanent failure #{TempVault.MaxRetryAttempts}");

        attempts.Should().Be(
            TempVault.MaxRetryAttempts,
            "a bounded retry must give up at a fixed count -- an infinite retry would hang this test");
    }

    [Fact]
    public void WithRetries_DoesNotCatchExceptionsUnrelatedToFileLocking()
    {
        Action act = () => TempVault.WithRetries(
            () => throw new InvalidOperationException("not a locking exception"),
            failureMessage: "should not be reached");

        act.Should().Throw<InvalidOperationException>().WithMessage("not a locking exception");
    }

    [Fact]
    public async Task ManyVaults_BuiltConcurrentlyAgainstTheSameSharedParent_AllSucceed()
    {
        // TD-97's own mechanism, raced directly: every TempVault's leaf is a fresh GUID, so no two
        // vaults ever target the same leaf, but all of them hang off the one shared parent,
        // "%TEMP%\pine-tests", and xUnit's default per-class parallelism puts dozens of these
        // constructors and a comparable number of Dispose() deletions against that one parent
        // directory at once. This forces that contention window directly instead of waiting for a
        // full suite run to happen to lose it.
        const int concurrency = 64;

        var vaults = new TempVault?[concurrency];
        try
        {
            await Task.WhenAll(Enumerable.Range(0, concurrency).Select(i => Task.Run(() =>
            {
                vaults[i] = new TempVault();
            })));

            foreach (var vault in vaults)
            {
                vault.Should().NotBeNull("every constructor must have survived the shared-parent race");
                Directory.Exists(vault!.VaultDirectory).Should().BeTrue();
            }
        }
        finally
        {
            foreach (var vault in vaults)
            {
                vault?.Dispose();
            }
        }
    }
}
