using FluentAssertions;
using NetArchTest.Rules;
using Xunit;
using MediatR;

namespace Pine.Tests.Unit.Architecture;

public class ArchitectureTests
{
    [Fact]
    public void DomainLayer_ShouldNot_DependOnOtherLayers()
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
            .HaveDependencyOn("Pine.Ui.Avalonia")
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue("Domain layer (Pine.Core) must remain pure and not depend on external layers.");
    }

    [Fact]
    public void ApplicationLayer_ShouldNot_DependOnInfrastructureOrUI()
    {
        // Arrange
        var appAssembly = typeof(Pine.Application.Accounts.Commands.CreateAccountCommand).Assembly;

        // Act
        var result = Types
            .InAssembly(appAssembly)
            .ShouldNot()
            .HaveDependencyOn("Pine.Infrastructure")
            .And()
            .HaveDependencyOn("Pine.Ui.Avalonia")
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue("Application layer (Pine.Application) should not depend on concrete infrastructure or UI.");
    }

    [Fact]
    public void Handlers_ShouldHave_DependencyOnMediatR()
    {
        // Arrange
        var appAssembly = typeof(Pine.Application.Accounts.Commands.CreateAccountCommand).Assembly;

        // Act
        var result = Types
            .InAssembly(appAssembly)
            .That()
            .HaveNameEndingWith("Handler")
            .Should()
            .ImplementInterface(typeof(IRequestHandler<>))
            .Or()
            .ImplementInterface(typeof(IRequestHandler<,>))
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue("All CQRS Handlers should implement MediatR IRequestHandler.");
    }
}
