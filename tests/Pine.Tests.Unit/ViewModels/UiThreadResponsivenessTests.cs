using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MediatR;
using NSubstitute;
using Pine.Application.Security;
using Pine.Core.Security;
using Pine.Tests.Unit.TestSupport;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.Services;
using Pine.Ui.Avalonia.ViewModels;
using Xunit;

namespace Pine.Tests.Unit.ViewModels;

/// <summary>
/// The view-model entry points that do the heaviest work yield the UI thread (TD-93, born of TD-92).
/// </summary>
/// <remarks>
/// <para>
/// Each test makes the slow dependency REALLY slow (a fake that sleeps, standing in for Argon2id, for
/// vault creation, for (not a photo decode - see below)), calls the real command on a real pumping UI
/// thread, and asserts two things: the slow dependency did not run on the UI thread, and the UI
/// thread was never held for anything like the injected delay. The first is exact; the second is
/// time-based and uses a limit well under half of the injected delay.
/// </para>
/// <para>
/// NOT COVERED: the receipt photo decode that TD-92 moved off the UI thread. Measured: a 20 MB
/// incompressible PNG constructs a headless <c>Bitmap</c> in about 8 ms (decoding is deferred), so no
/// image can make that line slow enough to tell on-thread from off-thread here.
/// </para>
/// <para>
/// MUTATION CHECK. Removing the <c>Task.Run</c> around the unlock body or the vault-creation body
/// makes the matching test fail; the harness's own tests fail if the stall
/// measurement is disabled.
/// </para>
/// </remarks>
public class UiThreadResponsivenessTests
{
    /// <summary>How long the injected dependency blocks its thread.</summary>
    private static readonly TimeSpan Injected = TimeSpan.FromMilliseconds(700);

    /// <summary>The most the UI thread may be held. Under half of <see cref="Injected"/>, so a blocked UI thread cannot hide.</summary>
    private static readonly TimeSpan Budget = TimeSpan.FromMilliseconds(300);

    [Fact]
    public async Task TheHarness_DetectsAnEntryPointThatBlocksTheUiThread()
    {
        var observation = await UiThreadHarness.Observe(() =>
        {
            Thread.Sleep(Injected); // the TD-92 shape: an async method whose body never yields
            return Task.CompletedTask;
        });

        observation.ReturnedAfter.Should().BeGreaterThan(Budget);
        observation.LongestStall.Should().BeGreaterThan(Budget);
    }

    [Fact]
    public async Task TheHarness_DetectsAContinuationThatBlocksAfterTheFirstAwait()
    {
        var observation = await UiThreadHarness.Observe(async () =>
        {
            await Task.Delay(20);
            Thread.Sleep(Injected); // resumes on the UI thread, then holds it
        });

        observation.ReturnedAfter.Should().BeLessThan(Budget, "the synchronous prefix was short");
        observation.LongestStall.Should().BeGreaterThan(Budget, "the heartbeat sees the held continuation");
    }

    [Fact]
    public async Task TheHarness_PassesAnEntryPointThatMovesItsWorkOffTheUiThread()
    {
        var observation = await UiThreadHarness.Observe(() => Task.Run(() => Thread.Sleep(Injected)));

        observation.ReturnedAfter.Should().BeLessThan(Budget);
        observation.LongestStall.Should().BeLessThan(Budget);
    }

    [Fact]
    public async Task UnlockVault_RunsKeyDerivationOffTheUiThread()
    {
        var keyService = Substitute.For<IVaultKeyService>();
        var workThread = -1;
        keyService.UnwrapDataKey(Arg.Any<string>(), Arg.Any<VaultHeader>()).Returns(_ =>
        {
            workThread = Environment.CurrentManagedThreadId;
            Thread.Sleep(Injected); // stands in for the Argon2id derivation
            return new byte[32];
        });

        var viewModel = new UnlockViewModel(
            Substitute.For<ISender>(),
            Substitute.For<IVaultSession>(),
            Substitute.For<INavigationService>(),
            Substitute.For<IViewModelFactory>(),
            Substitute.For<IVaultHeaderStore>(),
            keyService,
            Substitute.For<IVaultDatabaseOpener>(),
            new Localizer());
        viewModel.Initialize(new VaultProfile { Name = "Personal", Path = Path.Combine(Path.GetTempPath(), "pine-td93-unlock") });
        viewModel.MasterPassword = "correct horse battery staple";

        var observation = await UiThreadHarness.Observe(() => viewModel.UnlockVaultCommand.ExecuteAsync(null));

        workThread.Should().NotBe(-1, "the key derivation must actually have been reached");
        workThread.Should().NotBe(observation.UiThreadId, "key derivation is the slowest thing unlock does");
        observation.ReturnedAfter.Should().BeLessThan(Budget);
        observation.LongestStall.Should().BeLessThan(Budget);
    }

    [Fact]
    public async Task InitializeVault_CreatesTheVaultOffTheUiThread()
    {
        var vaultDir = Path.Combine(Path.GetTempPath(), "pine-td93-" + Guid.NewGuid().ToString("N"));
        try
        {
            var manager = Substitute.For<IVaultManager>();
            manager.CreateVaultAsync("Home").Returns(new VaultProfile { Id = Guid.NewGuid(), Name = "Home", Path = vaultDir });

            var vaultHeader = new VaultHeader(
                VaultHeader.CurrentVersion, Guid.NewGuid(), DateTimeOffset.UtcNow, KdfParameters.Argon2idV1, new byte[16],
                new VaultKeyWrap(VaultKeyWrapAlgorithms.Aes256Gcm, new byte[12], new byte[32], new byte[16]));
            var workThread = -1;
            var keyService = Substitute.For<IVaultKeyService>();
            keyService.CreateVault(Arg.Any<string>(), Arg.Any<KdfParameters>()).Returns(_ =>
            {
                workThread = Environment.CurrentManagedThreadId;
                Thread.Sleep(Injected); // stands in for minting the Argon2id-wrapped key
                return new VaultKeyMaterial(vaultHeader, new byte[32]);
            });

            var viewModel = new SetupViewModel(
                manager,
                Substitute.For<IVaultSession>(),
                Substitute.For<INavigationService>(),
                Substitute.For<IViewModelFactory>(),
                Substitute.For<IVaultHeaderStore>(),
                keyService,
                Substitute.For<IVaultDatabaseOpener>(),
                new Localizer())
            {
                VaultName = "Home",
                MasterPassword = "correct horse battery staple 7391!",
                ConfirmPassword = "correct horse battery staple 7391!",
            };

            var observation = await UiThreadHarness.Observe(() => viewModel.InitializeVaultCommand.ExecuteAsync(null));

            workThread.Should().NotBe(-1, "vault creation must actually have been reached");
            workThread.Should().NotBe(observation.UiThreadId);
            observation.ReturnedAfter.Should().BeLessThan(Budget);
            observation.LongestStall.Should().BeLessThan(Budget);
        }
        finally
        {
            if (Directory.Exists(vaultDir))
            {
                Directory.Delete(vaultDir, true);
            }
        }
    }
}
