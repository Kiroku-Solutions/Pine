using FluentAssertions;
using NSubstitute;
using Pine.Application.Security;
using Pine.Ui.Avalonia.Services;
using Pine.Ui.Avalonia.ViewModels;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Pine.Tests.Unit.ViewModels;

public class SetupViewModelTests
{
    private readonly IServiceProvider _serviceProvider = Substitute.For<IServiceProvider>();
    private readonly IVaultSession _vaultSession = Substitute.For<IVaultSession>();
    private readonly INavigationService _navigationService = Substitute.For<INavigationService>();

    private SetupViewModel CreateViewModel()
    {
        return new SetupViewModel(_serviceProvider, _vaultSession, _navigationService);
    }

    [Fact]
    public void Constructor_ShouldInitializeAtStep1()
    {
        // Act
        var vm = CreateViewModel();

        // Assert
        vm.CurrentStep.Should().Be(1);
        vm.IsStep1.Should().BeTrue();
        vm.IsStep2.Should().BeFalse();
    }

    [Fact]
    public void NextStepCommand_ShouldAdvanceToStep2()
    {
        // Arrange
        var vm = CreateViewModel();

        // Act
        vm.NextStepCommand.Execute(null);

        // Assert
        vm.CurrentStep.Should().Be(2);
        vm.IsStep1.Should().BeFalse();
        vm.IsStep2.Should().BeTrue();
    }

    [Fact]
    public void BackStepCommand_ShouldReturnToStep1()
    {
        // Arrange
        var vm = CreateViewModel();
        vm.CurrentStep = 2;

        // Act
        vm.BackStepCommand.Execute(null);

        // Assert
        vm.CurrentStep.Should().Be(1);
        vm.IsStep1.Should().BeTrue();
        vm.IsStep2.Should().BeFalse();
    }

    [Fact]
    public async Task InitializeVaultCommand_ShouldSetErrorMessage_WhenPasswordIsEmpty()
    {
        // Arrange
        var vm = CreateViewModel();
        vm.MasterPassword = "";

        // Act
        await vm.InitializeVaultCommand.ExecuteAsync(null);

        // Assert
        vm.StatusMessage.Should().Contain("cannot be empty");
        _navigationService.DidNotReceive().NavigateTo<ShellViewModel>();
    }
}
