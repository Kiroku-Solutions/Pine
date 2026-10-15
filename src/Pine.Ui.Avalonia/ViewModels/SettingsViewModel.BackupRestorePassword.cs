using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pine.Application.Security.Commands;
using Pine.Core.Common;
using Pine.Core.Security;
using Pine.Ui.Avalonia.Diagnostics;
using Pine.Ui.Avalonia.Localization;

namespace Pine.Ui.Avalonia.ViewModels;

// Restoring a backup taken before the vault's data key was rotated (TD-86). The plain restore signals this with the
// typed VaultBackupKeyMismatchException; this half asks for the password from when the backup was created, and
// retries with it. A wrong password keeps the prompt open and changes nothing.
public partial class SettingsViewModel
{
    private LocalizedMessageSlot? _backupPasswordError;
    private string? _backupPasswordTargetPath;

    /// <summary>Whether the "password from when this backup was created" prompt is up.</summary>
    [ObservableProperty]
    public partial bool IsBackupPasswordPromptVisible { get; set; }

    /// <summary>What the user typed. Cleared after every attempt and when the prompt closes.</summary>
    [ObservableProperty]
    public partial string BackupRestorePassword { get; set; } = string.Empty;

    /// <summary>Why the last attempt did not restore, in the active language; null when there is nothing to say.</summary>
    [ObservableProperty]
    public partial string? BackupPasswordError { get; set; }

    private LocalizedMessageSlot BackupPasswordErrorSlot =>
        _backupPasswordError ??= new LocalizedMessageSlot(_localizer, text => BackupPasswordError = text);

    /// <summary>Opens the prompt for the backup the plain restore could not open. Nothing has changed at this point.</summary>
    private void ShowBackupPasswordPrompt(string backupPath)
    {
        _backupPasswordTargetPath = backupPath;
        BackupRestorePassword = string.Empty;
        BackupPasswordErrorSlot.Clear();
        IsBackupPasswordPromptVisible = true;
    }

    [RelayCommand]
    private void CancelBackupPassword() => CloseBackupPasswordPrompt();

    private void CloseBackupPasswordPrompt()
    {
        IsBackupPasswordPromptVisible = false;
        BackupRestorePassword = string.Empty;
        _backupPasswordTargetPath = null;
        BackupPasswordErrorSlot.Clear();
    }

    [RelayCommand]
    private async Task SubmitBackupPasswordAsync()
    {
        var path = _backupPasswordTargetPath;
        var password = BackupRestorePassword;

        if (path is null || IsRestoringVault || string.IsNullOrEmpty(password))
        {
            return;
        }

        IsRestoringVault = true;
        BackupPasswordErrorSlot.Clear();

        try
        {
            var result = await _sender.Send(new RestoreVaultBackupWithPasswordCommand(path, password));
            LoadPlatformUnlockState();

            CloseBackupPasswordPrompt();
            VaultRestoreFailed = false;
            _restoreMessage.Set("Settings.Security.RestoreVault.Success", result.SafetyBackupPath);
        }
        catch (VaultBackupPasswordIncorrectException)
        {
            // Nothing changed. Keep the prompt, empty the field, allow another attempt.
            BackupRestorePassword = string.Empty;
            BackupPasswordErrorSlot.Set(UserFacingText.RestoreBackupPasswordWrong());
        }
        catch (Exception ex)
        {
            CloseBackupPasswordPrompt();
            VaultRestoreFailed = true;
            _restoreMessage.Set(UserFacingError.Report(
                _logger,
                ex,
                "restore-vault-backup-with-password",
                new UserFacingMessage("Settings.Security.RestoreVault.Failed")));
        }
        finally
        {
            password = string.Empty;
            IsRestoringVault = false;
        }
    }
}
