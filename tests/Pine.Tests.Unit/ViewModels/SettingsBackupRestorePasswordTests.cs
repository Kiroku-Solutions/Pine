using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MediatR;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Pine.Application.Security.Commands;
using Pine.Application.Security.Queries;
using Pine.Core.Security;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.ViewModels;
using Xunit;

namespace Pine.Tests.Unit.ViewModels;

/// <summary>
/// The older-backup password prompt (TD-86). View-model behaviour against fakes; nothing here has been rendered.
/// </summary>
public class SettingsBackupRestorePasswordTests
{
    private const string Backup = @"C:\Vaults\Personal\backups\pine-20260909T101500Z-Manual.db";
    private const string Safety = @"C:\Vaults\Personal\backups\pine-20260910T120000Z-PreRestore.db";

    private static async Task<(SettingsViewModel Vm, ISender Sender, Localizer Localizer)> PromptedAsync()
    {
        var localizer = new Localizer();
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<ListVaultBackupsQuery>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<VaultBackupInfo>)new[]
            {
                new VaultBackupInfo(Backup, new DateTimeOffset(2026, 9, 9, 10, 15, 0, TimeSpan.Zero), VaultBackupReason.Manual, 1, null),
            });
        sender.Send(Arg.Any<RestoreVaultBackupCommand>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new VaultBackupKeyMismatchException(Backup));
        sender.Send(Arg.Any<RestoreVaultBackupWithPasswordCommand>(), Arg.Any<CancellationToken>())
            .Returns(new VaultRestoreResult(Backup, DateTimeOffset.UtcNow, Safety, DateTimeOffset.UtcNow));

        var vm = new SettingsViewModel(sender, localizer, new LanguagePreferenceStore(path: null), null, null);
        await vm.LoadBackupsCommand.ExecuteAsync(null);
        vm.SelectedBackup = vm.Backups[0];
        vm.BeginRestoreCommand.Execute(null);
        await vm.ConfirmRestoreCommand.ExecuteAsync(null);
        return (vm, sender, localizer);
    }

    [Fact]
    public async Task TheTypedKeyMismatch_OpensThePrompt_AndIsNotReportedAsAFailure()
    {
        var (vm, _, localizer) = await PromptedAsync();

        vm.IsBackupPasswordPromptVisible.Should().BeTrue();
        vm.VaultRestoreFailed.Should().BeFalse();
        vm.VaultRestoreMessage.Should().BeNull();
        localizer["Settings.Security.RestoreVault.BackupPassword.Prompt"]
            .Should().Be("Enter the password used when this backup was created");
        localizer.SetLanguage("es");
        localizer["Settings.Security.RestoreVault.BackupPassword.Prompt"]
            .Should().Be("Introduzca la contraseña utilizada cuando se creó este respaldo");
    }

    [Fact]
    public async Task SubmittingThePassword_SendsItWithTheBackup_ClosesThePrompt_AndTellsWhereTheUndoIs()
    {
        var (vm, sender, _) = await PromptedAsync();
        vm.BackupRestorePassword = "old password";

        await vm.SubmitBackupPasswordCommand.ExecuteAsync(null);

        await sender.Received(1).Send(
            Arg.Is<RestoreVaultBackupWithPasswordCommand>(c => c.BackupPath == Backup && c.BackupPassword == "old password"),
            Arg.Any<CancellationToken>());
        vm.IsBackupPasswordPromptVisible.Should().BeFalse();
        vm.BackupRestorePassword.Should().BeEmpty();
        vm.VaultRestoreFailed.Should().BeFalse();
        vm.VaultRestoreMessage.Should().Contain(Safety);
    }

    [Fact]
    public async Task AWrongPassword_KeepsThePromptOpen_ShowsAClearError_ClearsTheField_AndAllowsAnotherTry()
    {
        var (vm, sender, localizer) = await PromptedAsync();
        sender.Send(Arg.Any<RestoreVaultBackupWithPasswordCommand>(), Arg.Any<CancellationToken>())
            .Returns<VaultRestoreResult>(_ => throw new VaultBackupPasswordIncorrectException());
        vm.BackupRestorePassword = "wrong";

        await vm.SubmitBackupPasswordCommand.ExecuteAsync(null);

        vm.IsBackupPasswordPromptVisible.Should().BeTrue();
        vm.BackupPasswordError.Should().Be(localizer["Failure.Restore.BackupPasswordWrong"]);
        vm.BackupRestorePassword.Should().BeEmpty();
        vm.IsRestoringVault.Should().BeFalse();

        sender.Send(Arg.Any<RestoreVaultBackupWithPasswordCommand>(), Arg.Any<CancellationToken>())
            .Returns(new VaultRestoreResult(Backup, DateTimeOffset.UtcNow, Safety, DateTimeOffset.UtcNow));
        vm.BackupRestorePassword = "right";
        await vm.SubmitBackupPasswordCommand.ExecuteAsync(null);

        vm.IsBackupPasswordPromptVisible.Should().BeFalse();
        vm.VaultRestoreMessage.Should().Contain(Safety);
    }

    [Fact]
    public async Task CancellingThePrompt_SendsNothing_AndForgetsThePassword()
    {
        var (vm, sender, _) = await PromptedAsync();
        vm.BackupRestorePassword = "typed";

        vm.CancelBackupPasswordCommand.Execute(null);

        vm.IsBackupPasswordPromptVisible.Should().BeFalse();
        vm.BackupRestorePassword.Should().BeEmpty();
        await sender.DidNotReceive().Send(Arg.Any<RestoreVaultBackupWithPasswordCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnEmptyPassword_SendsNothing()
    {
        var (vm, sender, _) = await PromptedAsync();

        await vm.SubmitBackupPasswordCommand.ExecuteAsync(null);

        await sender.DidNotReceive().Send(Arg.Any<RestoreVaultBackupWithPasswordCommand>(), Arg.Any<CancellationToken>());
        vm.IsBackupPasswordPromptVisible.Should().BeTrue();
    }

    [Fact]
    public async Task AnyOtherFailure_ClosesThePrompt_AndReportsTheGenericSentence()
    {
        var (vm, sender, localizer) = await PromptedAsync();
        sender.Send(Arg.Any<RestoreVaultBackupWithPasswordCommand>(), Arg.Any<CancellationToken>())
            .Returns<VaultRestoreResult>(_ => throw new InvalidOperationException("boom " + Backup));
        vm.BackupRestorePassword = "old";

        await vm.SubmitBackupPasswordCommand.ExecuteAsync(null);

        vm.IsBackupPasswordPromptVisible.Should().BeFalse();
        vm.VaultRestoreFailed.Should().BeTrue();
        vm.VaultRestoreMessage.Should().Be(localizer["Settings.Security.RestoreVault.Failed"]);
    }
}
