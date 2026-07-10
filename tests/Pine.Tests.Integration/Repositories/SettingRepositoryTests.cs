using FluentAssertions;
using Pine.Core.Settings;
using Pine.Core.Configuration;
using Pine.Infrastructure.Data;
using Pine.Tests.Integration.Fixtures;
using System.Threading.Tasks;
using Xunit;

namespace Pine.Tests.Integration.Repositories;

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
        var settingKey = "Test.Theme";
        var settingValue = "Dark";
        var setting = Setting.Create(settingKey, settingValue);

        // Act
        await repository.SaveAsync(setting);
        
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
        var settingKey = "Test.Language";
        var setting1 = Setting.Create(settingKey, "en-US");
        await repository.SaveAsync(setting1);

        // Act
        var setting2 = Setting.Create(settingKey, "es-ES");
        await repository.SaveAsync(setting2);

        // Assert
        var dbSetting = await repository.GetAsync(settingKey);
        dbSetting.Should().NotBeNull();
        dbSetting!.Value.Should().Be("es-ES");
    }
}
