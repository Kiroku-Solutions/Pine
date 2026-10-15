using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MediatR;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Pine.Application.Security;
using Pine.Application.Security.Commands;
using Pine.Application.Security.Queries;
using Pine.Core.Common;
using Pine.Core.Security;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.ViewModels;
using Xunit;

namespace Pine.Tests.Unit.ViewModels;

/// <summary>
/// The Settings page can restore a backup (TD-56's remaining half, plan item 1.1).
/// </summary>
/// <remarks>
/// <para>
/// WHAT THESE COVER AND WHAT THEY DO NOT. These are view-model tests: that the list is what the
/// query returned, that nothing is restored without a confirmation, that the confirmed command
/// carries the backup the user selected, and that the sentence afterwards names the copy that makes
/// the operation undoable. That the file on disk is genuinely replaced, and genuinely refused when
/// it should be, is <c>VaultRestoreServiceTests</c>'s job against a real vault. Nothing here has
/// been rendered on a screen.
/// </para>
/// <para>
/// THE MOST IMPORTANT TEST IN THIS FILE is the one asserting that opening the dialog restores
/// nothing. A confirmation that has already acted by the time it appears is not a confirmation, and
/// this is the only destructive operation in the application that a user asks for on purpose.
/// </para>
/// </remarks>
public class SettingsVaultRestoreTests
{
    private const string Vault = @"C:\Users\camilo\Vaults\Personal";
    private const string ManualBackup = Vault + @"\backups\pine-20260909T101500Z-Manual.db";
    private const string OlderBackup = Vault + @"\backups\pine-20260901T080000Z-PreMigration.db";
    private const string SafetyCopy = Vault + @"\backups\pine-20260910T120000Z-PreRestore.db";

    private static SettingsViewModel NewSettings(ISender sender, ILocalizer? localizer = null) =>
        new(sender, localizer ?? new Localizer(), new LanguagePreferenceStore(path: null), null, null);

    private static ISender SenderWithBackups(params VaultBackupInfo[] backups)
    {
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<ListVaultBackupsQuery>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<VaultBackupInfo>)backups);
        sender.Send(Arg.Any<RestoreVaultBackupCommand>(), Arg.Any<CancellationToken>())
            .Returns(new VaultRestoreResult(
                ManualBackup,
                new DateTimeOffset(2026, 9, 9, 10, 15, 0, TimeSpan.Zero),
                SafetyCopy,
                DateTimeOffset.UtcNow));
        return sender;
    }

    private static VaultBackupInfo Backup(string path, VaultBackupReason reason) =>
        new(path, new DateTimeOffset(2026, 9, 9, 10, 15, 0, TimeSpan.Zero), reason, 262144, null);

    [Fact]
    public async Task LoadingBackups_ShowsWhatTheQueryReturned()
    {
        var vm = NewSettings(SenderWithBackups(
            Backup(ManualBackup, VaultBackupReason.Manual),
            Backup(OlderBackup, VaultBackupReason.PreMigration)));

        await vm.LoadBackupsCommand.ExecuteAsync(null);

        vm.Backups.Should().HaveCount(2);
        vm.HasBackups.Should().BeTrue();
        vm.Backups[0].Path.Should().Be(ManualBackup);
    }

    [Fact]
    public async Task LoadingBackups_OnALockedVault_ShowsAnEmptyListAndNoError()
    {
        // The page can be reached with the vault locked, and the query refuses that by design. The
        // user has asked for nothing yet, so a red sentence on arrival would report a failure that
        // did not happen.
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<ListVaultBackupsQuery>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new VaultLockedException());
        var vm = NewSettings(sender);

        await vm.LoadBackupsCommand.ExecuteAsync(null);

        vm.Backups.Should().BeEmpty();
        vm.HasBackups.Should().BeFalse();
        vm.VaultRestoreMessage.Should().BeNull();
        vm.VaultRestoreFailed.Should().BeFalse();
    }

    [Fact]
    public async Task OpeningTheConfirmation_RestoresNothing()
    {
        // A confirmation that has already acted by the time it appears is not a confirmation.
        var sender = SenderWithBackups(Backup(ManualBackup, VaultBackupReason.Manual));
        var vm = NewSettings(sender);
        await vm.LoadBackupsCommand.ExecuteAsync(null);
        vm.SelectedBackup = vm.Backups[0];

        vm.BeginRestoreCommand.Execute(null);

        vm.IsRestoreDialogVisible.Should().BeTrue();
        await sender.DidNotReceive().Send(Arg.Any<RestoreVaultBackupCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OpeningTheConfirmation_WithNothingSelected_DoesNothingAtAll()
    {
        var sender = SenderWithBackups(Backup(ManualBackup, VaultBackupReason.Manual));
        var vm = NewSettings(sender);
        await vm.LoadBackupsCommand.ExecuteAsync(null);

        vm.BeginRestoreCommand.Execute(null);

        vm.IsRestoreDialogVisible.Should().BeFalse();
    }

    [Fact]
    public async Task CancellingTheConfirmation_RestoresNothing()
    {
        var sender = SenderWithBackups(Backup(ManualBackup, VaultBackupReason.Manual));
        var vm = NewSettings(sender);
        await vm.LoadBackupsCommand.ExecuteAsync(null);
        vm.SelectedBackup = vm.Backups[0];
        vm.BeginRestoreCommand.Execute(null);

        vm.CancelRestoreCommand.Execute(null);

        vm.IsRestoreDialogVisible.Should().BeFalse();
        await sender.DidNotReceive().Send(Arg.Any<RestoreVaultBackupCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConfirmingTheRestore_SendsTheCommandForTheSelectedBackup()
    {
        var sender = SenderWithBackups(
            Backup(ManualBackup, VaultBackupReason.Manual),
            Backup(OlderBackup, VaultBackupReason.PreMigration));
        var vm = NewSettings(sender);
        await vm.LoadBackupsCommand.ExecuteAsync(null);
        vm.SelectedBackup = vm.Backups[1];
        vm.BeginRestoreCommand.Execute(null);

        await vm.ConfirmRestoreCommand.ExecuteAsync(null);

        await sender.Received(1).Send(
            Arg.Is<RestoreVaultBackupCommand>(command => command.BackupPath == OlderBackup),
            Arg.Any<CancellationToken>());
        vm.IsRestoreDialogVisible.Should().BeFalse();
        vm.IsRestoringVault.Should().BeFalse();
    }

    [Fact]
    public async Task ChangingTheSelectionWhileTheDialogIsUp_DoesNotChangeWhatIsRestored()
    {
        // The list keeps its keyboard focus under the scrim, so an arrow key can move the selection
        // while the confirmation is up. A confirmation must act on what the user was shown when they
        // asked for it. This is the same reason the void dialog captures its target.
        var sender = SenderWithBackups(
            Backup(ManualBackup, VaultBackupReason.Manual),
            Backup(OlderBackup, VaultBackupReason.PreMigration));
        var vm = NewSettings(sender);
        await vm.LoadBackupsCommand.ExecuteAsync(null);
        vm.SelectedBackup = vm.Backups[0];
        vm.BeginRestoreCommand.Execute(null);

        vm.SelectedBackup = vm.Backups[1];

        await vm.ConfirmRestoreCommand.ExecuteAsync(null);

        await sender.Received(1).Send(
            Arg.Is<RestoreVaultBackupCommand>(command => command.BackupPath == ManualBackup),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConfirmingTheRestore_TellsTheUserWhereTheUndoIs()
    {
        // The one thing the user needs from this screen afterwards: the vault is about to lock, and
        // the copy of what they just replaced is the only way back.
        var sender = SenderWithBackups(Backup(ManualBackup, VaultBackupReason.Manual));
        var vm = NewSettings(sender);
        await vm.LoadBackupsCommand.ExecuteAsync(null);
        vm.SelectedBackup = vm.Backups[0];
        vm.BeginRestoreCommand.Execute(null);

        await vm.ConfirmRestoreCommand.ExecuteAsync(null);

        vm.VaultRestoreFailed.Should().BeFalse();
        vm.VaultRestoreMessage.Should().Contain(SafetyCopy);
    }

    [Fact]
    public async Task ARefusedRestore_ReachesTheUserInItsOwnWords()
    {
        // VaultRestoreRefusedException implements IUserFacingFailure precisely so the reason -- and
        // the fact that nothing changed -- is what the user reads (TD-24).
        var sender = SenderWithBackups(Backup(ManualBackup, VaultBackupReason.Manual));
        sender.Send(Arg.Any<RestoreVaultBackupCommand>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new VaultRestoreRefusedException(
                $"Refused to restore '{ManualBackup}': it carries a migration this build does not know.",
                UserFacingText.RestoreFromNewerVersion()));
        var vm = NewSettings(sender);
        await vm.LoadBackupsCommand.ExecuteAsync(null);
        vm.SelectedBackup = vm.Backups[0];
        vm.BeginRestoreCommand.Execute(null);

        await vm.ConfirmRestoreCommand.ExecuteAsync(null);

        vm.VaultRestoreFailed.Should().BeTrue();
        vm.VaultRestoreMessage.Should().Contain("newer version of Pine");
        vm.VaultRestoreMessage.Should().NotContain(ManualBackup, "a user-facing sentence names no path");
    }

    [Fact]
    public async Task AFailedRestore_ReportsTheFallbackSentenceWithoutLeakingThePath()
    {
        var localizer = new Localizer();
        var sender = SenderWithBackups(Backup(ManualBackup, VaultBackupReason.Manual));
        sender.Send(Arg.Any<RestoreVaultBackupCommand>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new VaultHeaderCorruptException(Vault, $"The vault header at {Vault} is damaged."));
        var vm = NewSettings(sender, localizer);
        await vm.LoadBackupsCommand.ExecuteAsync(null);
        vm.SelectedBackup = vm.Backups[0];
        vm.BeginRestoreCommand.Execute(null);

        await vm.ConfirmRestoreCommand.ExecuteAsync(null);

        vm.VaultRestoreFailed.Should().BeTrue();
        vm.VaultRestoreMessage.Should().Be(localizer["Settings.Security.RestoreVault.Failed"]);
        vm.VaultRestoreMessage.Should().NotContain(Vault);
    }

    [Fact]
    public async Task TheSuccessSentence_FollowsALanguageChange()
    {
        var localizer = new Localizer();
        var sender = SenderWithBackups(Backup(ManualBackup, VaultBackupReason.Manual));
        var vm = NewSettings(sender, localizer);
        await vm.LoadBackupsCommand.ExecuteAsync(null);
        vm.SelectedBackup = vm.Backups[0];
        vm.BeginRestoreCommand.Execute(null);
        await vm.ConfirmRestoreCommand.ExecuteAsync(null);
        var english = vm.VaultRestoreMessage;

        localizer.SetLanguage("es");

        vm.VaultRestoreMessage.Should().NotBe(english);
        vm.VaultRestoreMessage.Should().Contain(SafetyCopy);
    }

    [Fact]
    public async Task EachBackupsReasonIsShownInWords_NotAsAnEnumName()
    {
        var localizer = new Localizer();
        var vm = NewSettings(
            SenderWithBackups(
                Backup(ManualBackup, VaultBackupReason.Manual),
                Backup(OlderBackup, VaultBackupReason.PreMigration),
                Backup(SafetyCopy, VaultBackupReason.PreRestore)),
            localizer);

        await vm.LoadBackupsCommand.ExecuteAsync(null);

        vm.Backups[0].Reason.Should().Be(localizer["Settings.Security.RestoreVault.Reason.Manual"]);
        vm.Backups[1].Reason.Should().Be(localizer["Settings.Security.RestoreVault.Reason.PreMigration"]);
        vm.Backups[2].Reason.Should().Be(localizer["Settings.Security.RestoreVault.Reason.PreRestore"]);
        vm.Backups.Should().NotContain(option => option.Reason.Contains("PreMigration", StringComparison.Ordinal));
    }
}
