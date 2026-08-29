using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Pine.Application.Security;
using Pine.Core.Security;
using Pine.Ui.Avalonia.Services;
using Pine.Ui.Avalonia.ViewModels;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Pine.Tests.Unit.ViewModels;

/// <summary>
/// Covers the split between the AUTH phase and the VAULT-METADATA phase of unlocking (TD-05/TD-10).
/// Collapsing every failure into "Incorrect password or corrupted vault" is correct for genuine
/// authentication failures - it denies an attacker an oracle - but it is actively harmful when the
/// real cause is a header this build cannot read or a schema upgrade that failed, because it sends
/// the user to reset a password that was never the problem.
/// </summary>
public class UnlockViewModelTests
{
    private const string GenericAuthMessage = "Incorrect password or corrupted vault.";

    private readonly IServiceProvider _serviceProvider = Substitute.For<IServiceProvider>();
    private readonly IVaultSession _vaultSession = Substitute.For<IVaultSession>();
    private readonly INavigationService _navigationService = Substitute.For<INavigationService>();
    private readonly IVaultHeaderStore _vaultHeaderStore = Substitute.For<IVaultHeaderStore>();
    private readonly IVaultKeyService _vaultKeyService = Substitute.For<IVaultKeyService>();

    private UnlockViewModel CreateViewModel()
    {
        var vm = new UnlockViewModel(
            _serviceProvider,
            _vaultSession,
            _navigationService,
            _vaultHeaderStore,
            _vaultKeyService);

        vm.Initialize(new VaultProfile { Name = "Personal", Path = @"C:\vaults\personal" });
        vm.MasterPassword = "correct horse battery staple";
        return vm;
    }

    [Fact]
    public async Task UnlockVaultCommand_ShouldSetErrorMessage_WhenPasswordIsEmpty()
    {
        // Arrange
        var vm = CreateViewModel();
        vm.MasterPassword = string.Empty;

        // Act
        await vm.UnlockVaultCommand.ExecuteAsync(null);

        // Assert
        vm.StatusMessage.Should().Contain("cannot be empty");
        _vaultHeaderStore.DidNotReceiveWithAnyArgs().Read(default!);
    }

    [Fact]
    public async Task UnlockVaultCommand_ShouldReportUnsupportedHeader_AndNotBlameThePassword()
    {
        // Arrange
        var vm = CreateViewModel();
        _vaultHeaderStore.Read(Arg.Any<string>())
            .Throws(UnsupportedVaultHeaderException.ForKdfId("scrypt"));

        // Act
        await vm.UnlockVaultCommand.ExecuteAsync(null);

        // Assert
        vm.StatusMessage.Should().NotBe(GenericAuthMessage);
        vm.StatusMessage.Should().Contain("newer version of Pine");
        vm.IsBusy.Should().BeFalse();
        _vaultSession.Received().Lock();
        _navigationService.DidNotReceive().NavigateTo<ShellViewModel>();
        // Refusing an unknown KDF must happen BEFORE any key is recovered: unwrapping under guessed
        // parameters derives the wrong auth key, which is the failure TD-05 exists to kill.
        _vaultKeyService.DidNotReceiveWithAnyArgs().UnwrapDataKey(default!, default!);
    }

    [Fact]
    public async Task UnlockVaultCommand_ShouldReportCorruptHeader_AndNotBlameThePassword()
    {
        // Arrange
        var vm = CreateViewModel();
        _vaultHeaderStore.Read(Arg.Any<string>())
            .Throws(new VaultHeaderCorruptException(@"C:\vaults\personal\pine.vault.json", "truncated"));

        // Act
        await vm.UnlockVaultCommand.ExecuteAsync(null);

        // Assert
        vm.StatusMessage.Should().NotBe(GenericAuthMessage);
        vm.StatusMessage.Should().Contain("damaged");
        _vaultSession.Received().Lock();
    }

    [Fact]
    public async Task UnlockVaultCommand_ShouldNotLeakInternalDetails_ForAnyFailure()
    {
        // Arrange - an unexpected internal failure must collapse to the generic auth message and
        // must never surface ex.Message, which leaks paths and internals (TD-24).
        var vm = CreateViewModel();
        _vaultHeaderStore.Read(Arg.Any<string>())
            .Throws(new InvalidOperationException(@"connection to C:\secret\path\pine.db failed"));

        // Act
        await vm.UnlockVaultCommand.ExecuteAsync(null);

        // Assert
        vm.StatusMessage.Should().Be(GenericAuthMessage);
        vm.StatusMessage.Should().NotContain(@"C:\secret");
        _vaultSession.Received().Lock();
    }
}
