using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MediatR;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Pine.Application.Security;
using Pine.Core.Security;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.Services;
using Pine.Ui.Avalonia.ViewModels;
using Xunit;

namespace Pine.Tests.Unit.ViewModels;

/// <summary>TD-87 wiring: opt-in platform unlock never replaces the password, and its key is confirmed against the database.</summary>
public class PlatformUnlockViewModelTests
{
    private const string VaultDir = @"C:\vaults\personal";

    private readonly IPlatformUnlock _platform = Substitute.For<IPlatformUnlock>();
    private readonly IVaultSession _session = Substitute.For<IVaultSession>();
    private readonly IVaultDatabaseOpener _opener = Substitute.For<IVaultDatabaseOpener>();
    private readonly INavigationService _navigation = Substitute.For<INavigationService>();
    private readonly IViewModelFactory _factory = Substitute.For<IViewModelFactory>();

    public PlatformUnlockViewModelTests()
    {
        _platform.IsAvailable.Returns(true);
        _platform.IsEnabled(VaultDir).Returns(true);
    }

    private UnlockViewModel Unlock()
    {
        var vm = new UnlockViewModel(
            Substitute.For<ISender>(), _session, _navigation, _factory,
            Substitute.For<IVaultHeaderStore>(), Substitute.For<IVaultKeyService>(), _opener,
            new Localizer(), _platform);
        vm.Initialize(new VaultProfile { Name = "Personal", Path = VaultDir });
        return vm;
    }

    [Fact]
    public async Task UnlockWithPlatform_ShouldNavigate_OnlyAfterTheKeyOpenedTheDatabase()
    {
        byte[]? seen = null;
        _platform.TryUnlock(VaultDir).Returns(Enumerable.Repeat((byte)7, 32).ToArray());
        _session.When(s => s.Unlock(VaultDir, Arg.Any<byte[]>())).Do(c => seen = (byte[])c.Arg<byte[]>().Clone());
        var vm = Unlock();

        await vm.UnlockWithPlatformCommand.ExecuteAsync(null);

        seen.Should().Equal(Enumerable.Repeat((byte)7, 32));
        await _opener.Received(1).OpenExistingAsync(Arg.Any<CancellationToken>());
        _navigation.ReceivedWithAnyArgs(1).NavigateTo(default!);
        vm.StatusMessage.Should().BeEmpty();
    }

    [Fact]
    public async Task UnlockWithPlatform_ShouldFallBackSilently_WhenNoKeyIsReturned()
    {
        _platform.TryUnlock(VaultDir).Returns((byte[]?)null);
        var vm = Unlock();

        await vm.UnlockWithPlatformCommand.ExecuteAsync(null);

        _session.DidNotReceiveWithAnyArgs().Unlock(default!, default!);
        await _opener.DidNotReceiveWithAnyArgs().OpenExistingAsync(default);
        _navigation.DidNotReceiveWithAnyArgs().NavigateTo(default!);
        vm.StatusMessage.Should().BeEmpty();
        vm.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task UnlockWithPlatform_ShouldLockAndFallBackSilently_WhenTheKeyDoesNotOpenTheDatabase()
    {
        _platform.TryUnlock(VaultDir).Returns(new byte[32]);
        _opener.OpenExistingAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException());
        var vm = Unlock();

        await vm.UnlockWithPlatformCommand.ExecuteAsync(null);

        _session.Received(1).Lock();
        _navigation.DidNotReceiveWithAnyArgs().NavigateTo(default!);
        vm.StatusMessage.Should().BeEmpty();
        vm.CanUnlockWithPlatform.Should().BeTrue("the password path and the button both remain");
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void UnlockScreen_ShouldNotOfferPlatformUnlock_WhenUnavailableOrNotEnabled(bool available, bool enabled)
    {
        _platform.IsAvailable.Returns(available);
        _platform.IsEnabled(VaultDir).Returns(enabled);

        Unlock().CanUnlockWithPlatform.Should().BeFalse();
    }

    private SettingsViewModel Settings()
    {
        _session.DbPath.Returns(VaultDir);
        _session.Key.Returns(Enumerable.Repeat((byte)9, 32).ToArray());
        return new SettingsViewModel(
            Substitute.For<ISender>(), new Localizer(), new LanguagePreferenceStore(path: null),
            null, _session, _platform);
    }

    [Fact]
    public void Settings_ShouldSealTheSessionKey_WhenToggledOn_AndDeleteTheWrap_WhenOff()
    {
        _platform.IsEnabled(VaultDir).Returns(false);
        using var vm = Settings();
        vm.IsPlatformUnlockAvailable.Should().BeTrue();
        vm.PlatformUnlockEnabled.Should().BeFalse();

        vm.PlatformUnlockEnabled = true;
        _platform.Received(1).Enable(VaultDir, Arg.Is<byte[]>(k => k.Length == 32));
        vm.PlatformUnlockEnabled.Should().BeTrue();

        vm.PlatformUnlockEnabled = false;
        _platform.Received(1).Disable(VaultDir);
    }

    [Fact]
    public void Settings_ShouldRevertTheToggleAndReport_WhenEnableFails()
    {
        _platform.IsEnabled(VaultDir).Returns(false);
        _platform.When(p => p.Enable(Arg.Any<string>(), Arg.Any<byte[]>())).Do(_ => throw new PlatformUnlockUnavailableException());
        using var vm = Settings();

        vm.PlatformUnlockEnabled = true;

        vm.PlatformUnlockEnabled.Should().BeFalse();
        vm.ErrorMessage.Should().NotBeNullOrEmpty();
        _platform.DidNotReceive().Disable(Arg.Any<string>());
    }

    [Fact]
    public void Settings_ShouldHideTheToggle_WhenThePlatformIsUnavailable()
    {
        _platform.IsAvailable.Returns(false);
        using var vm = Settings();

        vm.IsPlatformUnlockAvailable.Should().BeFalse();
        vm.PlatformUnlockEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Settings_ShouldRefreshTheToggle_AfterAPasswordChange()
    {
        _platform.IsEnabled(VaultDir).Returns(true);
        using var vm = Settings();
        vm.PlatformUnlockEnabled.Should().BeTrue();

        // The handler (faked here) destroyed the wrap.
        _platform.IsEnabled(VaultDir).Returns(false);
        vm.CurrentPassword = "correct horse battery staple";
        vm.NewPassword = vm.ConfirmNewPassword = "seven tired lemons argue quietly";
        await vm.ChangePasswordCommand.ExecuteAsync(null);

        vm.PlatformUnlockEnabled.Should().BeFalse();
        _platform.DidNotReceive().Disable(Arg.Any<string>());
    }

    [Fact]
    public async Task Unlock_ShouldRecoverAnInterruptedRekeyBeforeUnwrapping_AndUseTheRecoveredKey()
    {
        var rotation = Substitute.For<IVaultKeyRotationService>();
        rotation.HasPendingRekey(VaultDir).Returns(true);
        var recovered = Enumerable.Repeat((byte)3, 32).ToArray();
        rotation.RecoverAndUnwrap(VaultDir, "pw").Returns(recovered);
        var keyService = Substitute.For<IVaultKeyService>();
        byte[]? seen = null;
        _session.When(s => s.Unlock(VaultDir, Arg.Any<byte[]>())).Do(c => seen = (byte[])c.Arg<byte[]>().Clone());
        var vm = new UnlockViewModel(
            Substitute.For<ISender>(), _session, _navigation, _factory,
            Substitute.For<IVaultHeaderStore>(), keyService, _opener,
            new Localizer(), _platform, rotation);
        vm.Initialize(new VaultProfile { Name = "Personal", Path = VaultDir });
        vm.MasterPassword = "pw";

        await vm.UnlockVaultCommand.ExecuteAsync(null);

        seen.Should().Equal(Enumerable.Repeat((byte)3, 32));
        keyService.DidNotReceiveWithAnyArgs().UnwrapDataKey(default!, default!);
    }
}
