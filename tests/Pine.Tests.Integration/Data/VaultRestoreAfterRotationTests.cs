using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Pine.Application.Security;
using Pine.Application.Security.Commands;
using Pine.Core.Security;
using Pine.Core.Settings;
using Pine.Infrastructure.Data;
using Pine.Infrastructure.Security;
using Xunit;

namespace Pine.Tests.Integration.Data;

/// <summary>
/// Restoring a backup taken BEFORE a data-key rotation (TD-86), on a real SQLCipher vault: the backup is encrypted
/// under the old data key, which only the password from back then (with the header saved beside the backup) can reach.
/// </summary>
public class VaultRestoreAfterRotationTests : IDisposable
{
    private const string Probe = "Rotation.Probe";
    private const string OldPassword = "the password used back then 1";
    private const string NewPassword = "the password used from now on 2";

    private static readonly KdfParameters CheapKdf =
        new("argon2id", memoryKib: 8, iterations: 1, parallelism: 1, keyLengthBytes: 32, saltLengthBytes: 16);

    private readonly TempVault _vault = new();
    private readonly AesGcmVaultKeyService _keys = new(new Argon2idKeyDerivationService());
    private readonly FileVaultHeaderStore _headers = new();
    private readonly SqlCipherVaultRekeyer _rekeyer = new();

    public void Dispose()
    {
        _vault.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Vault created under the old password, "v1" backed up, "v2" written, key then rotated.</summary>
    private async Task<(string BackupPath, byte[] NewKey)> RotatedVaultWithOldBackupAsync()
    {
        var material = _keys.CreateVault(OldPassword, CheapKdf);
        _headers.Write(_vault.VaultDirectory, material.Header);
        material.DataKey.CopyTo(_vault.Key, 0);

        var context = _vault.CreateContext();
        await context.Database.MigrateAsync(CancellationToken.None);
        context.Settings.Add(Setting.Create(Probe, "v1"));
        await context.SaveChangesAsync(CancellationToken.None);

        var backup = await new VaultBackupService(context).CreateBackupAsync(
            _vault.VaultDirectory, VaultBackupReason.Manual, CancellationToken.None);

        (await context.Settings.SingleAsync(s => s.Key == Probe, CancellationToken.None)).UpdateValue("v2");
        await context.SaveChangesAsync(CancellationToken.None);
        _vault.CloseContexts();

        var rotation = new VaultKeyRotationService(
            _headers, _keys, _rekeyer, new FileVaultRekeyJournal(), new VaultSealedFileResealer());
        var newKey = rotation.ChangePassword(_vault.VaultDirectory, OldPassword, NewPassword);
        newKey.CopyTo(_vault.Key, 0);

        return (backup.Path, newKey);
    }

    private VaultRestoreService NewService()
    {
        var context = _vault.CreateContext();
        return new VaultRestoreService(context, new VaultBackupService(context), null, _keys, _rekeyer);
    }

    private void AssertVaultUnchanged(byte[] newKey)
    {
        _vault.CloseContexts();
        _vault.ReadSetting(_vault.DatabasePath, newKey, Probe).Should().Be("v2");
        _vault.CloseContexts(); // releases the pooled handle the read left, so a later restore can replace the file
        Directory.GetDirectories(_vault.VaultDirectory, ".restore-staging-*").Should().BeEmpty();
    }

    [Fact]
    public async Task ABackup_CarriesTheKeyRecordOfItsEra()
    {
        var (backupPath, _) = await RotatedVaultWithOldBackupAsync();

        File.Exists(VaultBackupService.HeaderSidecarPath(backupPath)).Should().BeTrue();
        new VaultBackupService(_vault.CreateContext()).ListBackups(_vault.VaultDirectory)
            .Should().ContainSingle("the key record beside a backup is not itself listed as a backup");
    }

    [Fact]
    public async Task PlainRestore_OfABackupFromBeforeTheRotation_FailsWithTheTypedException_AndChangesNothing()
    {
        var (backupPath, newKey) = await RotatedVaultWithOldBackupAsync();

        var act = async () => await NewService().RestoreAsync(
            _vault.VaultDirectory, backupPath, CancellationToken.None);

        (await act.Should().ThrowAsync<VaultBackupKeyMismatchException>())
            .Which.BackupPath.Should().Be(backupPath);
        AssertVaultUnchanged(newKey);
    }

    [Fact]
    public async Task RestoreWithTheOldPassword_RestoresTheData_AndTheVaultOpensWithTheNewPassword()
    {
        var (backupPath, newKey) = await RotatedVaultWithOldBackupAsync();

        await NewService().RestoreWithBackupPasswordAsync(
            _vault.VaultDirectory, backupPath, OldPassword, CancellationToken.None);

        _vault.CloseContexts();
        _vault.ReadSetting(_vault.DatabasePath, newKey, Probe).Should().Be(
            "v1", "the backup's data is back, and it is readable under the CURRENT data key");
        _rekeyer.CanOpen(_vault.VaultDirectory, newKey).Should().BeTrue();

        var header = _headers.Read(_vault.VaultDirectory);
        var viaNewPassword = _keys.UnwrapDataKey(NewPassword, header);
        viaNewPassword.Should().Equal(newKey, "the vault still opens with the current password");
        Directory.GetDirectories(_vault.VaultDirectory, ".restore-staging-*").Should().BeEmpty();
        File.Exists(backupPath).Should().BeTrue("the backup itself is never consumed");
    }

    [Fact]
    public async Task RestoreWithAWrongOldPassword_ThrowsTheTypedException_ChangesNothing_AndAnotherTryWorks()
    {
        var (backupPath, newKey) = await RotatedVaultWithOldBackupAsync();
        var service = NewService();

        var act = async () => await service.RestoreWithBackupPasswordAsync(
            _vault.VaultDirectory, backupPath, "not the password at all", CancellationToken.None);
        await act.Should().ThrowAsync<VaultBackupPasswordIncorrectException>();
        AssertVaultUnchanged(newKey);

        await NewService().RestoreWithBackupPasswordAsync(
            _vault.VaultDirectory, backupPath, OldPassword, CancellationToken.None);
        _vault.CloseContexts();
        _vault.ReadSetting(_vault.DatabasePath, newKey, Probe).Should().Be("v1");
    }

    [Fact]
    public async Task RestoreWhenTheKeyRecordDoesNotBelongToTheBackup_IsRefusedAsWrongPassword_BeforeAnythingIsRekeyed()
    {
        // The password unwraps the record fine, but the key it yields does not open THIS backup. Only the explicit
        // verification of the derived key catches this; a missing check would surface as a rekey refusal instead.
        var (backupPath, newKey) = await RotatedVaultWithOldBackupAsync();
        var stranger = _keys.CreateVault(OldPassword, CheapKdf);
        var strangerDir = Path.Combine(_vault.VaultDirectory, "stranger");
        _headers.Write(strangerDir, stranger.Header);
        File.Copy(
            Path.Combine(strangerDir, FileVaultHeaderStore.HeaderFileName),
            VaultBackupService.HeaderSidecarPath(backupPath),
            overwrite: true);

        var act = async () => await NewService().RestoreWithBackupPasswordAsync(
            _vault.VaultDirectory, backupPath, OldPassword, CancellationToken.None);

        await act.Should().ThrowAsync<VaultBackupPasswordIncorrectException>();
        AssertVaultUnchanged(newKey);
    }

    [Fact]
    public async Task RestoreWithPassword_OfABackupWithoutAKeyRecord_IsRefusedWithAPreciseReason()
    {
        var (backupPath, newKey) = await RotatedVaultWithOldBackupAsync();
        File.Delete(VaultBackupService.HeaderSidecarPath(backupPath));

        var act = async () => await NewService().RestoreWithBackupPasswordAsync(
            _vault.VaultDirectory, backupPath, OldPassword, CancellationToken.None);

        (await act.Should().ThrowAsync<VaultRestoreRefusedException>())
            .Which.UserFacingMessages.Should().ContainSingle().Which.Key.Should().Be("Failure.Restore.NoKeyRecord");
        AssertVaultUnchanged(newKey);
    }

    [Fact]
    public async Task Handler_InvalidatesThePlatformWrap_ThenRestores_ThenLocks()
    {
        var (backupPath, _) = await RotatedVaultWithOldBackupAsync();
        var session = new RestoreVaultBackupCommandHandlerTests.SpyVaultSession(_vault.VaultDirectory);
        var unlock = new NoopPlatformUnlock();
        var handler = new RestoreVaultBackupWithPasswordCommandHandler(NewService(), session, unlock);

        await handler.Handle(
            new RestoreVaultBackupWithPasswordCommand(backupPath, OldPassword), CancellationToken.None);

        unlock.Invalidated.Should().ContainSingle().Which.Should().Be(_vault.VaultDirectory);
        session.IsUnlocked.Should().BeFalse();
    }

    [Fact]
    public void TheCommandNeverPrintsThePassword()
    {
        new RestoreVaultBackupWithPasswordCommand("p.db", "hunter2-secret").ToString()
            .Should().NotContain("hunter2-secret");
    }
}
