using NetArchTest.Rules;
using Xunit;

namespace Pine.Tests.Unit;

public class ArchitectureTests
{
    [Fact]
    public void DomainLayerShouldNotDependOnOtherLayers()
    {
        // Arrange
        var domainAssembly = typeof(Pine.Core.Accounts.Account).Assembly;

        // Act
        var result = Types
            .InAssembly(domainAssembly)
            .ShouldNot()
            .HaveDependencyOn("Pine.Application")
            .And()
            .HaveDependencyOn("Pine.Infrastructure")
            .And()
            .HaveDependencyOn("Pine.Cli")
            .GetResult();

        // Assert
        Assert.True(result.IsSuccessful, "Domain layer (Pine.Core) should not depend on other layers.");
    }
}
