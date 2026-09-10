using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Pine.Core.Security;
using Pine.Core.Settings;
using Pine.Infrastructure.Data;
using Xunit;

namespace Pine.Tests.Integration.Data;

/// <summary>
/// Restore over a real, file-based, SQLCipher vault (TD-56, plan item 1.1).
/// </summary>
/// <remarks>
/// Every test here is about a way a restore can hurt someone, not about a way it can fail. The
/// happy path is one test; the other seven are the refusals, and each of them asserts the same two
/// things — that the operation stopped, and that the vault still holds what it held before.
/// </remarks>
public class VaultRestoreServiceTests : IDisposable
{
    private const string ProbeKey = "Restore.Probe";

    private readonly TempVault _vault = new();

    public void Dispose()
    {
        _vault.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task RestoreAsync_ShouldPutTheVaultBackToTheBackupsContents()
    {
        // Arrange: a vault with a known value, a backup of it, then a DIFFERENT known value.
        var context = _vault.CreateContext();
        await context.Database.MigrateAsync(CancellationToken.None);
        context.Settings.Add(Setting.Create(ProbeKey, "before-the-backup"));
        await context.SaveChangesAsync(CancellationToken.None);

        var backupService = new VaultBackupService(context);
        var backup = await backupService.CreateBackupAsync(
            _vault.VaultDirectory, VaultBackupReason.Manual, CancellationToken.None);

        var setting = await context.Settings.SingleAsync(s => s.Key == ProbeKey, CancellationToken.None);
        setting.UpdateValue("after-the-backup");
        await context.SaveChangesAsync(CancellationToken.None);

        _vault.CloseContexts();
        var restoreContext = _vault.CreateContext();
        var service = new VaultRestoreService(restoreContext, new VaultBackupService(restoreContext));

        // Act
        var result = await service.RestoreAsync(
            _vault.VaultDirectory, backup.Path, CancellationToken.None);

        // Assert: the vault now holds what the backup held.
        _vault.CloseContexts();
        _vault.ReadSetting(_vault.DatabasePath, _vault.Key, ProbeKey).Should().Be(
            "before-the-backup",
            "restoring a backup means the vault holds what the backup held, not what it held a moment ago");

        // Assert: and the replaced state is still on disk, so the restore is reversible.
        result.SafetyBackupPath.Should().NotBeNullOrWhiteSpace();
        File.Exists(result.SafetyBackupPath).Should().BeTrue();
        _vault.ReadSetting(result.SafetyBackupPath, _vault.Key, ProbeKey).Should().Be(
            "after-the-backup",
            "the safety copy is the undo: it must hold the state the restore replaced, not the state it restored");

        result.RestoredFrom.Should().Be(backup.Path);
    }

    /// <summary>
    /// A restore performed with stale write-ahead-log files on disk still yields the backup's data.
    /// </summary>
    /// <remarks>
    /// WHAT THIS TEST DOES NOT PROVE, STATED HERE BECAUSE IT WAS NEARLY CLAIMED. It was written as
    /// "the restore deletes the stale sidecars", and deleting the <c>DeleteSidecar</c> calls from the
    /// service left it GREEN — twice, under two different arrangements. The reason is that SQLite
    /// removes <c>-wal</c> and <c>-shm</c> itself when the last connection to a database closes
    /// cleanly, which is exactly what the restore does before it copies. So the deletion in the
    /// service is insurance against the NON-clean case — a crash or a kill between those two points —
    /// and this suite cannot construct that case. The assertion below is the one that is honestly
    /// observable: stale sidecars on disk do not corrupt the restored vault.
    /// </remarks>
    [Fact]
    public async Task RestoreAsync_ShouldYieldTheBackupsData_EvenWithStaleSidecarsOnDisk()
    {
        // Arrange: write enough through EF that a -wal file exists beside the database.
        var context = _vault.CreateContext();
        await context.Database.MigrateAsync(CancellationToken.None);
        context.Settings.Add(Setting.Create(ProbeKey, "one"));
        await context.SaveChangesAsync(CancellationToken.None);

        var backup = await new VaultBackupService(context).CreateBackupAsync(
            _vault.VaultDirectory, VaultBackupReason.Manual, CancellationToken.None);

        // THE SIDECARS ARE PLANTED, NOT ASSUMED. The first version of this test relied on EF having
        // left a -wal behind, and a clean close removes it -- so there was nothing to leave behind
        // and deleting the DeleteSidecar calls entirely left the test GREEN. These two files are
        // exactly what a crash, a kill, or a non-clean shutdown leaves on disk, which is the state
        // the restore has to survive. They are planted with SQLite's own handles released, because
        // otherwise the test cannot write them at all.
        _vault.CloseContexts();
        await File.WriteAllBytesAsync(_vault.DatabasePath + "-wal", new byte[512], CancellationToken.None);
        await File.WriteAllBytesAsync(_vault.DatabasePath + "-shm", new byte[512], CancellationToken.None);

        var restoreContext = _vault.CreateContext();
        var service = new VaultRestoreService(restoreContext, new VaultBackupService(restoreContext));

        // Act
        await service.RestoreAsync(_vault.VaultDirectory, backup.Path, CancellationToken.None);

        // Assert: the vault reads as the backup. A log belonging to the database that was replaced,
        // replayed into the one that replaced it, is the single way this operation could produce a
        // vault that is neither the old contents nor the new ones -- so the thing worth asserting is
        // that it did not happen, rather than which line of code prevented it.
        _vault.CloseContexts();
        _vault.ReadSetting(_vault.DatabasePath, _vault.Key, ProbeKey).Should().Be("one");
    }

    [Fact]
    public async Task RestoreAsync_ShouldLeaveTheVaultIntact_WhenTheFileCannotBePutInPlace()
    {
        // THE DEFECT THIS RECORDS. The replacement was `File.Copy(source, pine.db, overwrite: true)`,
        // which opens the user's live vault for writing and streams into it: a disk that fills or a
        // process that is killed partway leaves pine.db as neither the old vault nor the backup. It
        // is now a copy to a neighbour followed by a move, and the failure below -- a destination
        // that cannot be replaced -- is the observable stand-in for the interrupted write.
        var context = _vault.CreateContext();
        await context.Database.MigrateAsync(CancellationToken.None);
        await SeedAsync(context, "untouched");

        var backup = await new VaultBackupService(context).CreateBackupAsync(
            _vault.VaultDirectory, VaultBackupReason.Manual, CancellationToken.None);

        _vault.CloseContexts();
        var restoreContext = _vault.CreateContext();
        var service = new VaultRestoreService(restoreContext, new VaultBackupService(restoreContext));

        // A read-only destination cannot be replaced; it can still be read, which is what the
        // assertion needs.
        File.SetAttributes(_vault.DatabasePath, FileAttributes.ReadOnly);

        try
        {
            var restore = async () => await service.RestoreAsync(
                _vault.VaultDirectory, backup.Path, CancellationToken.None);

            var thrown = await restore.Should().ThrowAsync<VaultRestoreRefusedException>();
            thrown.Which.UserFacingMessage.Should().Contain(
                "nothing was changed",
                "the sentence the user reads has to be true, and before the staging file existed it "
                + "was not");
        }
        finally
        {
            File.SetAttributes(_vault.DatabasePath, FileAttributes.Normal);
        }

        await AssertVaultStillHoldsAsync("untouched");

        Directory.GetFiles(_vault.VaultDirectory, "*.restore-tmp").Should().BeEmpty(
            "a failed replacement cleans up after itself rather than leaving a mystery file beside "
            + "the vault");
    }

    [Fact]
    public async Task RestoreAsync_ShouldLeaveNoStagingFileBehind_OnSuccess()
    {
        var context = _vault.CreateContext();
        await context.Database.MigrateAsync(CancellationToken.None);
        await SeedAsync(context, "original");

        var backup = await new VaultBackupService(context).CreateBackupAsync(
            _vault.VaultDirectory, VaultBackupReason.Manual, CancellationToken.None);

        var service = new VaultRestoreService(context, new VaultBackupService(context));
        await service.RestoreAsync(_vault.VaultDirectory, backup.Path, CancellationToken.None);

        Directory.GetFiles(_vault.VaultDirectory, "*.restore-tmp").Should().BeEmpty(
            "the staging file is moved into place, not copied and left behind -- a stray half-vault "
            + "beside the real one is exactly the ambiguity this design removes");
    }

    [Fact]
    public async Task RestoreAsync_ShouldRefuse_WhenTheFileIsOutsideThisVaultsBackups()
    {
        var context = _vault.CreateContext();
        await context.Database.MigrateAsync(CancellationToken.None);
        await SeedAsync(context, "untouched");

        // THE STRANGER IS A PERFECTLY GOOD BACKUP OF THIS VERY VAULT, MOVED ONE DIRECTORY UP, and
        // that is the whole point of the test. The first version of it used a text file, which the
        // key/format probe rejected anyway -- so deleting the containment check entirely left the
        // test GREEN. A test that passes for a reason other than the one it is named after is not a
        // test. Only containment can refuse this file.
        var backup = await new VaultBackupService(context).CreateBackupAsync(
            _vault.VaultDirectory, VaultBackupReason.Manual, CancellationToken.None);

        var stranger = Path.Combine(_vault.VaultDirectory, "somebody-elses.db");
        File.Copy(backup.Path, stranger);

        var service = new VaultRestoreService(context, new VaultBackupService(context));

        var restore = async () => await service.RestoreAsync(
            _vault.VaultDirectory, stranger, CancellationToken.None);

        await restore.Should().ThrowAsync<VaultRestoreRefusedException>(
            "copying a file over someone's ledger because it happens to be readable is not a "
            + "feature; the restore path accepts this vault's own backups and nothing else");

        await AssertVaultStillHoldsAsync("untouched");
    }

    [Fact]
    public async Task RestoreAsync_ShouldRefuse_WhenTheBackupCannotBeReadWithTheVaultsKey()
    {
        var context = _vault.CreateContext();
        await context.Database.MigrateAsync(CancellationToken.None);
        await SeedAsync(context, "untouched");

        // A file in the right place, with the right name shape, that is not a Pine backup. This is
        // the case SQLCipher gives no open-time error for: it fails on the first READ.
        Directory.CreateDirectory(_vault.BackupsDirectory);
        var impostor = Path.Combine(_vault.BackupsDirectory, "pine-20260101T000000Z-Manual.db");
        await File.WriteAllBytesAsync(impostor, new byte[4096], CancellationToken.None);

        var service = new VaultRestoreService(context, new VaultBackupService(context));

        var restore = async () => await service.RestoreAsync(
            _vault.VaultDirectory, impostor, CancellationToken.None);

        var thrown = await restore.Should().ThrowAsync<VaultRestoreRefusedException>();
        thrown.Which.UserFacingMessage.Should().NotContain(
            impostor, "a user-facing sentence must name no file path (TD-24)");

        await AssertVaultStillHoldsAsync("untouched");
    }

    [Fact]
    public async Task RestoreAsync_ShouldRefuse_WhenTheBackupCarriesAMigrationThisBuildDoesNotKnow()
    {
        var context = _vault.CreateContext();
        await context.Database.MigrateAsync(CancellationToken.None);
        await SeedAsync(context, "untouched");

        var backup = await new VaultBackupService(context).CreateBackupAsync(
            _vault.VaultDirectory, VaultBackupReason.Manual, CancellationToken.None);

        // A backup written by a LATER Pine: it carries a migration this build has never heard of.
        // EF applies only PENDING migrations and is silent about unknown ones, so without this check
        // the file would open without complaint and then be written to by a model that does not
        // match it.
        _vault.CloseContexts();
        using (var connection = _vault.OpenRaw(backup.Path, _vault.Key))
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
                VALUES ('29990101000000_FromTheFuture', '99.0.0');
                """;
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }

        var restoreContext = _vault.CreateContext();
        var service = new VaultRestoreService(restoreContext, new VaultBackupService(restoreContext));

        var restore = async () => await service.RestoreAsync(
            _vault.VaultDirectory, backup.Path, CancellationToken.None);

        var thrown = await restore.Should().ThrowAsync<VaultRestoreRefusedException>();
        thrown.Which.UserFacingMessage.Should().Contain(
            "newer version",
            "the one fact that tells the user what to do next is that their Pine is out of date");
        thrown.Which.UserFacingMessage.Should().NotContain(
            "29990101000000_FromTheFuture", "a migration identifier is not a sentence for a person");

        await AssertVaultStillHoldsAsync("untouched");
    }

    [Fact]
    public async Task RestoreAsync_ShouldRefuse_WhenTheBackupHasVanished()
    {
        var context = _vault.CreateContext();
        await context.Database.MigrateAsync(CancellationToken.None);
        await SeedAsync(context, "untouched");

        var backup = await new VaultBackupService(context).CreateBackupAsync(
            _vault.VaultDirectory, VaultBackupReason.Manual, CancellationToken.None);

        _vault.CloseContexts();
        File.Delete(backup.Path);

        var restoreContext = _vault.CreateContext();
        var service = new VaultRestoreService(restoreContext, new VaultBackupService(restoreContext));

        var restore = async () => await service.RestoreAsync(
            _vault.VaultDirectory, backup.Path, CancellationToken.None);

        await restore.Should().ThrowAsync<VaultRestoreRefusedException>();

        await AssertVaultStillHoldsAsync("untouched");
    }

    [Fact]
    public async Task RestoreAsync_ShouldTakeItsSafetyCopyBeforeReplacingAnything()
    {
        // The ordering is the contract, not an implementation detail: a restore whose undo does not
        // exist must not proceed. Asserted by observing that the safety copy holds the PRE-restore
        // state -- if it were taken after the copy, it would hold the restored state instead, and
        // undoing a restore would restore the thing it was undoing.
        var context = _vault.CreateContext();
        await context.Database.MigrateAsync(CancellationToken.None);
        await SeedAsync(context, "original");

        var backup = await new VaultBackupService(context).CreateBackupAsync(
            _vault.VaultDirectory, VaultBackupReason.Manual, CancellationToken.None);

        var setting = await context.Settings.SingleAsync(s => s.Key == ProbeKey, CancellationToken.None);
        setting.UpdateValue("replaced-by-the-restore");
        await context.SaveChangesAsync(CancellationToken.None);

        _vault.CloseContexts();
        var restoreContext = _vault.CreateContext();
        var service = new VaultRestoreService(restoreContext, new VaultBackupService(restoreContext));

        var result = await service.RestoreAsync(
            _vault.VaultDirectory, backup.Path, CancellationToken.None);

        _vault.CloseContexts();
        _vault.ReadSetting(result.SafetyBackupPath, _vault.Key, ProbeKey)
            .Should().Be("replaced-by-the-restore");
    }

    [Fact]
    public async Task RestoreAsync_ShouldTagTheSafetyCopyAsPreRestore_SoTheListSaysWhatItIs()
    {
        var context = _vault.CreateContext();
        await context.Database.MigrateAsync(CancellationToken.None);
        await SeedAsync(context, "original");

        var backupService = new VaultBackupService(context);
        var backup = await backupService.CreateBackupAsync(
            _vault.VaultDirectory, VaultBackupReason.Manual, CancellationToken.None);

        var service = new VaultRestoreService(context, backupService);
        var result = await service.RestoreAsync(
            _vault.VaultDirectory, backup.Path, CancellationToken.None);

        _vault.CloseContexts();
        var listed = new VaultBackupService(_vault.CreateContext()).ListBackups(_vault.VaultDirectory);

        listed.Should().Contain(
            entry => entry.Path == result.SafetyBackupPath && entry.Reason == VaultBackupReason.PreRestore,
            "the copy a restore leaves behind must be distinguishable in the list from one the user "
            + "asked for, because the two answer different questions");
    }

    [Fact]
    public async Task RestoreAsync_ShouldRestoreARestore_SoTheOperationIsReversible()
    {
        // The whole claim of the safety copy, end to end: restore, decide it was a mistake, undo it.
        var context = _vault.CreateContext();
        await context.Database.MigrateAsync(CancellationToken.None);
        await SeedAsync(context, "the-state-the-user-wanted-back");

        var backup = await new VaultBackupService(context).CreateBackupAsync(
            _vault.VaultDirectory, VaultBackupReason.Manual, CancellationToken.None);

        var setting = await context.Settings.SingleAsync(s => s.Key == ProbeKey, CancellationToken.None);
        setting.UpdateValue("the-state-the-user-still-needed");
        await context.SaveChangesAsync(CancellationToken.None);

        _vault.CloseContexts();
        var first = _vault.CreateContext();
        var result = await new VaultRestoreService(first, new VaultBackupService(first))
            .RestoreAsync(_vault.VaultDirectory, backup.Path, CancellationToken.None);

        _vault.CloseContexts();
        var second = _vault.CreateContext();
        await new VaultRestoreService(second, new VaultBackupService(second))
            .RestoreAsync(_vault.VaultDirectory, result.SafetyBackupPath, CancellationToken.None);

        _vault.CloseContexts();
        _vault.ReadSetting(_vault.DatabasePath, _vault.Key, ProbeKey).Should().Be(
            "the-state-the-user-still-needed",
            "restoring the safety copy must put back exactly what the first restore replaced");
    }

    private static async Task SeedAsync(PineDbContext context, string value)
    {
        context.Settings.Add(Setting.Create(ProbeKey, value));
        await context.SaveChangesAsync(CancellationToken.None);
    }

    private async Task AssertVaultStillHoldsAsync(string expected)
    {
        _vault.CloseContexts();
        await Task.Yield();

        _vault.ReadSetting(_vault.DatabasePath, _vault.Key, ProbeKey).Should().Be(
            expected,
            "a refused restore must leave the vault exactly as it was -- that is what makes it a "
            + "refusal rather than a failure");
    }
}
