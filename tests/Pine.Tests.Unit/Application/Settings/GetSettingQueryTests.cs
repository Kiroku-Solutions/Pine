using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using Pine.Application.Common;
using Pine.Application.Settings.Queries;
using Pine.Core.Configuration;
using Pine.Core.Settings;
using Xunit;

namespace Pine.Tests.Unit.Application.Settings;

public class GetSettingQueryTests
{
    [Fact]
    public async Task Handle_ShouldReturnTheStoredValue_WhenTheKeyExists()
    {
        // Arrange
        var repository = Substitute.For<ISettingRepository>();
        repository.GetAsync("Appearance.Palette", Arg.Any<CancellationToken>())
            .Returns(Setting.Create("Appearance.Palette", "forest"));
        var handler = new GetSettingQueryHandler(repository);

        // Act
        var result = await handler.Handle(new GetSettingQuery("Appearance.Palette"), CancellationToken.None);

        // Assert
        result.Should().Be("forest");
    }

    [Fact]
    public async Task Handle_ShouldReturnNull_WhenTheKeyIsAbsent()
    {
        // Arrange - the UI treats null as "keep the default", so this must not throw.
        var repository = Substitute.For<ISettingRepository>();
        repository.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((Setting?)null);
        var handler = new GetSettingQueryHandler(repository);

        // Act
        var result = await handler.Handle(new GetSettingQuery("Appearance.Theme"), CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void Query_MustNotBeTransactional()
    {
        // A read has nothing to commit; opening a transaction for it is pure overhead.
        new GetSettingQuery("k").Should().NotBeAssignableTo<ITransactionalRequest>();
    }
}
