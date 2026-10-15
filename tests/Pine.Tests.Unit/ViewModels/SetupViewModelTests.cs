using CommunityToolkit.Mvvm.ComponentModel;
using FluentAssertions;
using NSubstitute;
using Pine.Application.Security;
using Pine.Core.Configuration;
using Pine.Core.Security;
using Pine.Ui.Avalonia.Services;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.ViewModels;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace Pine.Tests.Unit.ViewModels;

public class SetupViewModelTests
{
    /// <summary>
    /// A REAL <c>VaultManager</c> OVER A THROWAWAY DIRECTORY. It is sealed, so it cannot be
    /// substituted, and its own remarks explain why the data directory became injectable: without
    /// it, exercising this screen writes <c>vaults.json</c> into the developer's real
    /// <c>%LOCALAPPDATA%</c>. Every test here stops at an early validation refusal, so nothing is
    /// ever written -- the temporary path is what makes that true by construction rather than by
    /// luck.
    /// </summary>
    private readonly IVaultManager _vaultManager = new VaultManager(new PineStorageOptions
    {
        DataDirectory = Path.Combine(Path.GetTempPath(), "pine-setup-viewmodel-tests"),
    });

    private readonly IViewModelFactory _viewModels = Substitute.For<IViewModelFactory>();
    private readonly IVaultSession _vaultSession = Substitute.For<IVaultSession>();
    private readonly INavigationService _navigationService = Substitute.For<INavigationService>();
    private readonly IVaultHeaderStore _vaultHeaderStore = Substitute.For<IVaultHeaderStore>();
    private readonly IVaultKeyService _vaultKeyService = Substitute.For<IVaultKeyService>();
    private readonly IVaultDatabaseOpener _vaultDatabaseOpener = Substitute.For<IVaultDatabaseOpener>();

    private SetupViewModel CreateViewModel()
    {
        return new SetupViewModel(
            _vaultManager,
            _vaultSession,
            _navigationService,
            _viewModels,
            _vaultHeaderStore,
            _vaultKeyService,
            _vaultDatabaseOpener,
            new Localizer());
    }

    [Fact]
    public void Constructor_ShouldInitializeAtStep1()
    {
        // Act
        var vm = CreateViewModel();

        // Assert
        vm.CurrentStep.Should().Be(1);
        vm.IsStep1.Should().BeTrue();
        vm.IsStep2.Should().BeFalse();
    }

    [Fact]
    public void NextStepCommand_ShouldAdvanceToStep2()
    {
        // Arrange
        var vm = CreateViewModel();

        // Act
        vm.NextStepCommand.Execute(null);

        // Assert
        vm.CurrentStep.Should().Be(2);
        vm.IsStep1.Should().BeFalse();
        vm.IsStep2.Should().BeTrue();
    }

    [Fact]
    public void BackStepCommand_ShouldReturnToStep1()
    {
        // Arrange
        var vm = CreateViewModel();
        vm.CurrentStep = 2;

        // Act
        vm.BackStepCommand.Execute(null);

        // Assert
        vm.CurrentStep.Should().Be(1);
        vm.IsStep1.Should().BeTrue();
        vm.IsStep2.Should().BeFalse();
    }

    [Fact]
    public async Task InitializeVaultCommand_ShouldSetErrorMessage_WhenPasswordIsEmpty()
    {
        // Arrange
        var vm = CreateViewModel();
        vm.MasterPassword = "";

        // Act
        await vm.InitializeVaultCommand.ExecuteAsync(null);

        // Assert
        vm.StatusMessage.Should().Contain("cannot be empty");
        _navigationService.DidNotReceive().NavigateTo(Arg.Any<ObservableObject>());
    }

    /// <summary>
    /// TD-81: with <see cref="IVaultManager"/> substitutable, the happy path finally runs under test
    /// without touching the real data directory: a vault is registered, its header written BEFORE
    /// the session is unlocked and the schema created, and the shell is shown.
    /// </summary>
    [Fact]
    public async Task InitializeVaultCommand_ShouldCreateVaultAndNavigate_WithAFakeVaultManager()
    {
        var vaultDir = Path.Combine(Path.GetTempPath(), "pine-td81-" + Guid.NewGuid().ToString("N"));
        try
        {
            var fakeManager = Substitute.For<IVaultManager>();
            fakeManager.CreateVaultAsync("Home").Returns(new VaultProfile { Id = Guid.NewGuid(), Name = "Home", Path = vaultDir });

            var kdf = Pine.Core.Security.KdfParameters.Argon2idV1;
            var header = Substitute.For<IVaultKeyService>();
            var dataKey = new byte[] { 1, 2, 3, 4 };
            var vaultHeader = new VaultHeader(
                VaultHeader.CurrentVersion, Guid.NewGuid(), DateTimeOffset.UtcNow, kdf, new byte[16],
                new VaultKeyWrap(VaultKeyWrapAlgorithms.Aes256Gcm, new byte[12], new byte[32], new byte[16]));
            header.CreateVault(Arg.Any<string>(), Arg.Any<KdfParameters>()).Returns(new VaultKeyMaterial(vaultHeader, dataKey));

            var vm = new SetupViewModel(
                fakeManager, _vaultSession, _navigationService, _viewModels,
                _vaultHeaderStore, header, _vaultDatabaseOpener, new Localizer())
            {
                VaultName = "Home",
                MasterPassword = "correct horse battery staple 7391!",
                ConfirmPassword = "correct horse battery staple 7391!",
            };

            await vm.InitializeVaultCommand.ExecuteAsync(null);

            await fakeManager.Received(1).CreateVaultAsync("Home");
            _vaultHeaderStore.Received(1).Write(vaultDir, vaultHeader);
            _vaultSession.Received(1).Unlock(vaultDir, Arg.Any<byte[]>());
            await _vaultDatabaseOpener.Received(1).CreateSchemaAsync(Arg.Any<System.Threading.CancellationToken>());
            _viewModels.Received(1).CreateShell();
            _navigationService.Received(1).NavigateTo(Arg.Any<ObservableObject>());
        }
        finally
        {
            if (Directory.Exists(vaultDir)) Directory.Delete(vaultDir, true);
        }
    }
}
