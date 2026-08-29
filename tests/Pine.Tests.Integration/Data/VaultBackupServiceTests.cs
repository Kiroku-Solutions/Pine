using System.Text;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pine.Core.Security;
using Pine.Core.Settings;
using Pine.Infrastructure.Data;
using Xunit;

namespace Pine.Tests.Integration.Data;

public class VaultBackupServiceTests : IDisposable
{
    private const string ProbeKey = "Backup.Probe";
    private const string ProbeValue = "the-quick-brown-fox";

    private readonly TempVault _vault = new();

    public void Dispose()
    {
        _vault.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task CreateBackupAsync_ShouldProduceBackupEncryptedWithSameKey()
    {
        // Arrange
        var context = _vault.CreateContext();
        await context.Database.MigrateAsync(CancellationToken.None);
        context.Settings.Add(Setting.Create(ProbeKey, ProbeValue));
        await context.SaveChangesAsync(CancellationToken.None);
        var service = new VaultBackupService(context);

        // Act
        var backup = await service.CreateBackupAsync(
            _vault.VaultDirectory, VaultBackupReason.Manual, CancellationToken.None);

        // Assert: the file exists and is NOT a plaintext SQLite database.
        File.Exists(backup.Path).Should().BeTrue();
        backup.SizeBytes.Should().BeGreaterThan(0);
        var header = new byte[16];
        await using (var stream = File.OpenRead(backup.Path))
        {
            var read = await stream.ReadAsync(header, CancellationToken.None);
            read.Should().Be(16);
        }

        Encoding.ASCII.GetString(header).Should().NotStartWith("SQLite format 3",
            "a backup taken through the keyed SQLCipher connection must not land on disk in plaintext");

        // Assert: it opens with the CORRECT key and carries the data.
        _vault.ReadSetting(backup.Path, _vault.Key, ProbeKey).Should().Be(ProbeValue);

        // Assert: it does NOT open with a WRONG key.
        var wrongKey = new byte[32];
        Array.Fill(wrongKey, (byte)0xAB);
        var openWithWrongKey = () => _vault.ReadSetting(backup.Path, wrongKey, ProbeKey);
        openWithWrongKey.Should()
            .Throw<SqliteException>("the backup must be encrypted with the vault key, not readable by anyone else")
            .Which.SqliteErrorCode.Should().Be(26, "SQLITE_NOTADB is what a failed SQLCipher decryption looks like");
    }

    [Fact]
    public async Task CreateBackupAsync_ShouldRecordSchemaVersionAndReason()
    {
        // Arrange
        var context = _vault.CreateContext();
        await context.Database.MigrateAsync(CancellationToken.None);
        var service = new VaultBackupService(context);

        // Act
        var backup = await service.CreateBackupAsync(
            _vault.VaultDirectory, VaultBackupReason.PreMigration, CancellationToken.None);

        // Assert
        backup.Reason.Should().Be(VaultBackupReason.PreMigration);
        // Deliberate tripwire: this is the HEAD migration id, spelled out so that adding a migration
        // without deciding what the backup record should say turns this test red instead of letting
        // the recorded schema version drift silently away from the schema that was backed up.
        // Retargeted in iteration 6 (TD-02) from 20260827141109_AddTransactionImportKey, and again
        // in iteration 9 (TD-19), which added the nullable line item Kind column.
        backup.SchemaVersionBefore.Should().Be("20260828201103_AddTransactionLineItemKind");
        Path.GetFileName(backup.Path).Should().EndWith("-PreMigration.db");
        Path.GetDirectoryName(backup.Path).Should().Be(_vault.BackupsDirectory);
    }

    [Fact]
    public async Task CreateBackupAsync_ShouldNotOverwrite_WhenTwoBackupsLandInTheSameSecond()
    {
        // Arrange
        var context = _vault.CreateContext();
        await context.Database.MigrateAsync(CancellationToken.None);
        var service = new VaultBackupService(context);

        // Act
        var first = await service.CreateBackupAsync(
            _vault.VaultDirectory, VaultBackupReason.Manual, CancellationToken.None);
        var second = await service.CreateBackupAsync(
            _vault.VaultDirectory, VaultBackupReason.Manual, CancellationToken.None);

        // Assert
        second.Path.Should().NotBe(first.Path);
        File.Exists(first.Path).Should().BeTrue("an existing backup must never be overwritten");
        File.Exists(second.Path).Should().BeTrue();
        Directory.GetFiles(_vault.BackupsDirectory).Should().HaveCount(2);
    }

    [Fact]
    public async Task CreateBackupAsync_ShouldThrowArgumentException_WhenDatabaseIsOutsideVaultDirectory()
    {
        // Arrange
        var context = _vault.CreateContext();
        await context.Database.MigrateAsync(CancellationToken.None);
        var service = new VaultBackupService(context);
        var unrelatedDirectory = Path.Combine(Path.GetTempPath(), "pine-tests", Guid.NewGuid().ToString("N"));

        // Act
        var act = async () => await service.CreateBackupAsync(
            unrelatedDirectory, VaultBackupReason.Manual, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
        Directory.Exists(unrelatedDirectory).Should().BeFalse("the guard must run before anything is created on disk");
    }

    [Fact]
    public async Task CreateBackupAsync_ShouldThrowAndLeaveNoFile_WhenCancelled()
    {
        // Arrange
        var context = _vault.CreateContext();
        await context.Database.MigrateAsync(CancellationToken.None);
        var service = new VaultBackupService(context);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        var act = async () => await service.CreateBackupAsync(
            _vault.VaultDirectory, VaultBackupReason.Manual, cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        var leftovers = Directory.Exists(_vault.BackupsDirectory)
            ? Directory.GetFiles(_vault.BackupsDirectory)
            : [];
        leftovers.Should().BeEmpty("a truncated backup is worse than no backup");
    }

    [Fact]
    public async Task ListBackups_ShouldReturnBackupsNewestFirst_WithMetadata()
    {
        // Arrange
        var context = _vault.CreateContext();
        await context.Database.MigrateAsync(CancellationToken.None);
        var service = new VaultBackupService(context);
        var created = new List<VaultBackupInfo>();
        for (var i = 0; i < 3; i++)
        {
            created.Add(await service.CreateBackupAsync(
                _vault.VaultDirectory, VaultBackupReason.Manual, CancellationToken.None));
        }

        // Act
        var listed = service.ListBackups(_vault.VaultDirectory);

        // Assert
        listed.Should().HaveCount(3);
        listed.Select(b => b.Path).Should().Equal(created.AsEnumerable().Reverse().Select(b => b.Path));
        listed.Should().AllSatisfy(b =>
        {
            b.Reason.Should().Be(VaultBackupReason.Manual);
            b.SizeBytes.Should().Be(new FileInfo(b.Path).Length);
            b.CreatedAtUtc.Offset.Should().Be(TimeSpan.Zero);
        });
    }

    [Fact]
    public void ListBackups_ShouldReturnEmpty_WhenNoBackupsWereEverTaken()
    {
        // Arrange
        var service = new VaultBackupService(_vault.CreateContext());

        // Act
        var listed = service.ListBackups(_vault.VaultDirectory);

        // Assert
        listed.Should().BeEmpty();
    }

    [Fact]
    public async Task PruneAsync_ShouldKeepExactlyTheMostRecentBackups()
    {
        // Arrange
        var context = _vault.CreateContext();
        await context.Database.MigrateAsync(CancellationToken.None);
        var service = new VaultBackupService(context);
        for (var i = 0; i < 7; i++)
        {
            await service.CreateBackupAsync(
                _vault.VaultDirectory, VaultBackupReason.Manual, CancellationToken.None);
        }

        var before = service.ListBackups(_vault.VaultDirectory);

        // Act
        await service.PruneAsync(_vault.VaultDirectory, 3, CancellationToken.None);

        // Assert
        var after = service.ListBackups(_vault.VaultDirectory);
        after.Should().HaveCount(3);
        after.Select(b => b.Path).Should().Equal(before.Take(3).Select(b => b.Path));
        before.Skip(3).Should().AllSatisfy(b => File.Exists(b.Path).Should().BeFalse());
    }

    [Fact]
    public async Task PruneAsync_ShouldDoNothing_WhenFewerBackupsThanRequested()
    {
        // Arrange
        var context = _vault.CreateContext();
        await context.Database.MigrateAsync(CancellationToken.None);
        var service = new VaultBackupService(context);
        await service.CreateBackupAsync(
            _vault.VaultDirectory, VaultBackupReason.Manual, CancellationToken.None);
        await service.CreateBackupAsync(
            _vault.VaultDirectory, VaultBackupReason.Manual, CancellationToken.None);

        // Act
        await service.PruneAsync(
            _vault.VaultDirectory, VaultBackupService.DefaultBackupsToKeep, CancellationToken.None);

        // Assert
        service.ListBackups(_vault.VaultDirectory).Should().HaveCount(2);
    }

    [Fact]
    public async Task PruneAsync_ShouldReject_WhenKeepMostRecentIsBelowOne()
    {
        // Arrange
        var service = new VaultBackupService(_vault.CreateContext());

        // Act
        var act = async () => await service.PruneAsync(
            _vault.VaultDirectory, 0, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>(
            "pruning to zero would delete every backup the user has");
    }
}
