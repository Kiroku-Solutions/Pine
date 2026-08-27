using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Pine.Core.Settings;
using Pine.Infrastructure.Data;
using Pine.Tests.Integration.Fixtures;
using System.Threading.Tasks;
using Xunit;

namespace Pine.Tests.Integration.Repositories;

/// <summary>
/// Repositories are pure change-trackers since TD-01: <c>SaveAsync</c> stages an entity on the
/// scoped <see cref="PineDbContext"/> and nothing else. Persistence is the unit of work's job, so
/// these tests flush through <see cref="EfCoreUnitOfWork"/> exactly as <c>TransactionBehavior</c>
/// does in production instead of relying on the repository to commit for them.
/// </summary>
public class SettingRepositoryTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public SettingRepositoryTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SaveAsync_ShouldPersistSetting_AndGetAsyncShouldRetrieveIt()
    {
        // Arrange
        var repository = new SqliteSettingRepository(_fixture.DbContext);
        var unitOfWork = new EfCoreUnitOfWork(_fixture.DbContext);
        var settingKey = "Test.Theme";
        var settingValue = "Dark";
        var setting = Setting.Create(settingKey, settingValue);

        // Act
        await repository.SaveAsync(setting);
        await unitOfWork.SaveChangesAsync();

        // Assert: Read directly from DbContext to ensure it was saved
        var dbSetting = await repository.GetAsync(settingKey);
        dbSetting.Should().NotBeNull();
        dbSetting!.Value.Should().Be(settingValue);
    }

    [Fact]
    public async Task SaveAsync_ShouldUpdateExistingSetting_WhenKeyAlreadyExists()
    {
        // Arrange
        var repository = new SqliteSettingRepository(_fixture.DbContext);
        var unitOfWork = new EfCoreUnitOfWork(_fixture.DbContext);
        var settingKey = "Test.Language";
        var setting1 = Setting.Create(settingKey, "en-US");
        await repository.SaveAsync(setting1);
        await unitOfWork.SaveChangesAsync();

        // Act
        var setting2 = Setting.Create(settingKey, "es-ES");
        await repository.SaveAsync(setting2);
        await unitOfWork.SaveChangesAsync();

        // Assert
        var dbSetting = await repository.GetAsync(settingKey);
        dbSetting.Should().NotBeNull();
        dbSetting!.Value.Should().Be("es-ES");
    }

    [Fact]
    public async Task SaveAsync_ShouldNotCommitByItself()
    {
        // This is the contract TD-01 depends on: any write path that does not run inside a
        // transactional MediatR request stages its change and loses it. If this test ever fails,
        // repositories have started committing again and the transactional boundary is a lie.
        var repository = new SqliteSettingRepository(_fixture.DbContext);
        var setting = Setting.Create("Test.NeverCommitted", "staged");

        await repository.SaveAsync(setting);

        // Query the database itself, bypassing the change tracker.
        var rowCount = await _fixture.DbContext.Settings
            .AsNoTracking()
            .CountAsync(s => s.Key == "Test.NeverCommitted");
        rowCount.Should().Be(0);

        // Drop the staged entity so it cannot leak into another test through the shared context.
        _fixture.DbContext.Entry(setting).State = EntityState.Detached;
    }
}
