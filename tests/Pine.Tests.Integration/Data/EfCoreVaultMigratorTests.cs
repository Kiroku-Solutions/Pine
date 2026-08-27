using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Pine.Core.Security;
using Pine.Infrastructure.Data;
using Xunit;

namespace Pine.Tests.Integration.Data;

public class EfCoreVaultMigratorTests : IDisposable
{
    /// <summary>
    /// Stands in for a migration applied by an earlier Pine build. Its presence is what makes EF
    /// report a non-empty applied set alongside a non-empty pending set, i.e. a real schema upgrade
    /// of a vault that already holds the user's data.
    /// </summary>
    private const string PreviousSchemaVersion = "20250101000000_InitialSchema";

    private const string CurrentSchemaVersion = "20260825151122_AddTransactionLineItems";

    private readonly TempVault _vault = new();

    public void Dispose()
    {
        _vault.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task MigrateAsync_ShouldMigrateWithoutBackup_OnFirstRun()
    {
        // Arrange: a brand-new vault; pine.db does not exist yet and there is no data to lose.
        var context = _vault.CreateContext();
        var migrator = new EfCoreVaultMigrator(context, new VaultBackupService(context));

        // Act
        var result = await migrator.MigrateAsync(CancellationToken.None);

        // Assert
        result.MigrationsApplied.Should().BeTrue();
        result.AppliedMigrationIds.Should().Contain(CurrentSchemaVersion);
        result.BackupPath.Should().BeNull("backing up an empty, brand-new vault protects nothing");
        Directory.Exists(_vault.BackupsDirectory).Should().BeFalse();

        // Assert: and the schema really is usable afterwards.
        var applied = await context.Database.GetAppliedMigrationsAsync(CancellationToken.None);
        applied.Should().Contain(CurrentSchemaVersion);
    }

    [Fact]
    public async Task MigrateAsync_ShouldDoNothing_WhenSchemaIsAlreadyUpToDate()
    {
        // Arrange: a vault that has already been migrated once.
        var firstContext = _vault.CreateContext();
        await new EfCoreVaultMigrator(firstContext, new VaultBackupService(firstContext))
            .MigrateAsync(CancellationToken.None);

        var context = _vault.CreateContext();
        var backupService = new VaultBackupService(context);
        var migrator = new EfCoreVaultMigrator(context, backupService);

        // Act
        var result = await migrator.MigrateAsync(CancellationToken.None);

        // Assert
        result.MigrationsApplied.Should().BeFalse();
        result.AppliedMigrationIds.Should().BeEmpty();
        result.BackupPath.Should().BeNull();
        backupService.ListBackups(_vault.VaultDirectory).Should()
            .BeEmpty("unlock happens constantly and must not burn disk on every open");
    }

    [Fact]
    public async Task MigrateAsync_ShouldCreateBackup_WhenMigrationsArePendingOnAnExistingVault()
    {
        // Arrange
        _vault.SeedAppliedMigration(PreviousSchemaVersion);
        var context = _vault.CreateContext();
        var backupService = new VaultBackupService(context);
        var migrator = new EfCoreVaultMigrator(context, backupService);

        // Act
        var result = await migrator.MigrateAsync(CancellationToken.None);

        // Assert
        result.MigrationsApplied.Should().BeTrue();
        result.AppliedMigrationIds.Should().Contain(CurrentSchemaVersion);
        result.BackupPath.Should().NotBeNull();
        File.Exists(result.BackupPath!).Should().BeTrue();

        var backups = backupService.ListBackups(_vault.VaultDirectory);
        backups.Should().ContainSingle();
        backups[0].Reason.Should().Be(VaultBackupReason.PreMigration);
    }

    [Fact]
    public async Task MigrateAsync_ShouldThrowVaultMigrationExceptionWithRecoverableBackup_WhenMigrationFails()
    {
        // Arrange: an existing vault whose schema upgrade cannot succeed, because an object the
        // migration creates is already there (the classic half-upgraded / hand-edited vault).
        _vault.SeedAppliedMigration(PreviousSchemaVersion);
        _vault.ExecuteSql(@"CREATE TABLE ""Accounts"" (""Id"" TEXT NOT NULL PRIMARY KEY);");

        var context = _vault.CreateContext();
        var migrator = new EfCoreVaultMigrator(context, new VaultBackupService(context));

        // Act
        var act = async () => await migrator.MigrateAsync(CancellationToken.None);

        // Assert
        var thrown = await act.Should().ThrowAsync<VaultMigrationException>();
        var exception = thrown.Which;
        exception.PendingMigrations.Should().Contain(CurrentSchemaVersion);
        exception.InnerException.Should().NotBeNull();
        exception.BackupPath.Should().NotBeNull();
        File.Exists(exception.BackupPath!).Should()
            .BeTrue("the user must be able to actually restore the backup we point them at");
    }
}
