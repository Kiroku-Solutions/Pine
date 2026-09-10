using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MediatR;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Pine.Application.Security;
using Pine.Application.Security.Commands;
using Pine.Core.Security;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.ViewModels;
using Xunit;

namespace Pine.Tests.Unit.ViewModels;

/// <summary>
/// The Settings page's two security buttons actually do something (TD-56).
/// </summary>
/// <remarks>
/// <para>
/// THE DEFECT THESE COVER. <c>ExportVault</c> and <c>LockVault</c> were empty method bodies with
/// "to be implemented" comments, bound to enabled, visible buttons on a shipped screen -- while
/// <c>ExportVaultBackupCommand</c> and <c>VaultBackupService</c> had been complete, tested and
/// unreachable from the UI. Pressing Export produced no file, no error and no feedback of any
/// kind; pressing Lock left the vault open with its data key in memory.
/// </para>
/// <para>
/// WHAT IS ASSERTED, AND WHAT IS NOT. These are view-model tests: they prove the button reaches
/// the command, that the path the handler returned is what the user is told, and that a failure
/// arrives as a sentence rather than being swallowed. That a backup file is genuinely produced and
/// genuinely encrypted is <c>VaultBackupServiceTests</c>'s job and is not re-proved here. Nothing
/// here has been rendered on a screen either.
/// </para>
/// </remarks>
public class SettingsVaultExportTests
{
    private const string BackupPath = @"C:\Users\camilo\Vaults\Personal\backups\pine-20260909T101500Z-Manual.db";
    private const string VaultPath = @"C:\Users\camilo\Vaults\Personal";

    private static VaultBackupInfo SomeBackup() =>
        new(BackupPath, DateTimeOffset.UtcNow, VaultBackupReason.Manual, 4096, "20260101_Initial");

    private static SettingsViewModel NewSettings(
        ISender sender,
        ILocalizer? localizer = null,
        IVaultSession? session = null) =>
        new(sender, localizer ?? new Localizer(), new LanguagePreferenceStore(path: null), null, session);

    /// <summary>The button reaches the backend that has existed all along.</summary>
    [Fact]
    public async Task ExportingTheVault_SendsTheBackupCommand()
    {
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<ExportVaultBackupCommand>(), Arg.Any<CancellationToken>())
            .Returns(SomeBackup());
        var vm = NewSettings(sender);

        await vm.ExportVaultCommand.ExecuteAsync(null);

        await sender.Received(1).Send(Arg.Any<ExportVaultBackupCommand>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The user is told WHERE the backup went, which is the only way they can find it.
    /// </summary>
    /// <remarks>
    /// The destination is chosen by the backup service -- the vault's own <c>backups</c> directory,
    /// because <c>VACUUM INTO</c> runs on the vault's keyed connection -- so there is no picker and
    /// therefore no other moment at which the user learns the path.
    /// </remarks>
    [Fact]
    public async Task ExportingTheVault_ReportsThePathItWroteTo()
    {
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<ExportVaultBackupCommand>(), Arg.Any<CancellationToken>())
            .Returns(SomeBackup());
        var vm = NewSettings(sender);

        await vm.ExportVaultCommand.ExecuteAsync(null);

        vm.VaultExportMessage.Should().Contain(BackupPath);
        vm.VaultExportFailed.Should().BeFalse();
        vm.IsExportingVault.Should().BeFalse();
    }

    /// <summary>
    /// An exception that opted in to being shown reaches the user in its own words (TD-24).
    /// </summary>
    /// <remarks>
    /// <c>VaultLockedException</c> is the realistic case: the handler throws it when the session is
    /// not unlocked, and its instruction -- unlock and try again -- is the one that resolves the
    /// situation. Under the old stub the user got nothing at all.
    /// </remarks>
    [Fact]
    public async Task ExportingALockedVault_TellsTheUserToUnlockIt()
    {
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<ExportVaultBackupCommand>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new VaultLockedException("Cannot export a vault backup while the vault is locked."));
        var vm = NewSettings(sender);

        await vm.ExportVaultCommand.ExecuteAsync(null);

        vm.VaultExportFailed.Should().BeTrue();
        vm.VaultExportMessage.Should().Contain("Unlock the vault and try again.");
    }

    /// <summary>
    /// A failure is reported rather than swallowed, and does not put the vault path on screen.
    /// </summary>
    /// <remarks>
    /// Same instrument <c>ErrorMessageTaxonomyTests</c> uses: a real Pine exception whose message
    /// names the user's vault directory. The fallback sentence is read from the shipped table
    /// rather than retyped here, so a wording change in <c>en.json</c> cannot leave this test
    /// asserting a sentence the application no longer says.
    /// </remarks>
    [Fact]
    public async Task ExportingTheVault_ReportsAFailureInWordsWithoutLeakingThePath()
    {
        var localizer = new Localizer();
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<ExportVaultBackupCommand>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new VaultHeaderCorruptException(VaultPath, $"The vault header at {VaultPath} is damaged."));
        var vm = NewSettings(sender, localizer);

        await vm.ExportVaultCommand.ExecuteAsync(null);

        vm.VaultExportFailed.Should().BeTrue();
        vm.VaultExportMessage.Should().NotBeNullOrWhiteSpace(
            "a button that fails silently is the defect this ticket is about");
        vm.VaultExportMessage.Should().NotContain(VaultPath);
        vm.VaultExportMessage.Should().Be(localizer["Settings.Security.ExportVault.Failed"]);
    }

    /// <summary>The success sentence is held as a key, so it follows a language change.</summary>
    /// <remarks>
    /// The reason <c>LocalizedMessageSlot</c> exists (D2): a sentence assigned as text would leave
    /// the page half translated after the picker above it is used.
    /// </remarks>
    [Fact]
    public async Task ExportSuccess_IsRerenderedWhenTheLanguageChanges()
    {
        var localizer = new Localizer();
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<ExportVaultBackupCommand>(), Arg.Any<CancellationToken>())
            .Returns(SomeBackup());
        var vm = NewSettings(sender, localizer);

        await vm.ExportVaultCommand.ExecuteAsync(null);
        var english = vm.VaultExportMessage;

        localizer.SetLanguage("es");

        vm.VaultExportMessage.Should().NotBe(english);
        vm.VaultExportMessage.Should().Contain(BackupPath);
    }

    /// <summary>
    /// The Settings page's Lock button asks the session to lock, exactly as the shell's does.
    /// </summary>
    /// <remarks>
    /// It was an empty method: the button was bound, visible and reachable from the sidebar, and it
    /// left the vault open. <c>MainViewModel</c> owns what happens next, so asking is the whole job.
    /// </remarks>
    [Fact]
    public void LockingTheVaultFromSettings_AsksTheSessionToLock()
    {
        var session = Substitute.For<IVaultSession>();
        var vm = NewSettings(Substitute.For<ISender>(), session: session);

        vm.LockVaultCommand.Execute(null);

        session.Received(1).Lock(VaultLockReason.UserRequested);
    }
}
