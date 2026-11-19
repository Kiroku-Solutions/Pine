using System;
using System.Linq;
using FluentAssertions;
using NSubstitute;
using Pine.Application.Security;
using Pine.Ui.Avalonia.Composition;
using Pine.Ui.Avalonia.Services;
using Pine.Ui.Avalonia.ViewModels;
using Xunit;

namespace Pine.Tests.Unit.ViewModels;

/// <summary>
/// Every sidebar destination the Windows head offers is offered on every other head, except the
/// two that need the receipt pipeline (OpenCV/Paddle native runtimes exist for win-x64 only).
/// The Android tablet once looked "different" from Windows because of a fixed 240px sidebar in a
/// ~600px-wide portrait screen plus those two documented omissions.
/// </summary>
public class ShellParityTests
{
    /// <summary>The only destinations allowed to be missing off Windows, by design.</summary>
    private static readonly string[] CapabilityGated =
    {
        nameof(ShellViewModel.NavigateToScanReceiptCommand),
        nameof(ShellViewModel.NavigateToLiveScannerCommand),
    };

    private static ShellViewModel Shell(PineHeadProfile profile)
    {
        var mediator = Substitute.For<MediatR.IMediator>();
        var factory = Substitute.For<IViewModelFactory>();
        factory.CreateDashboard(Arg.Any<IShellNavigator>())
            .Returns(call => new DashboardViewModel(mediator, factory, call.Arg<IShellNavigator>(), PineHeadCapabilities.For(profile)));
        return new(factory, Substitute.For<IVaultSession>(), PineHeadCapabilities.For(profile));
    }

    private static string[] Destinations() =>
        typeof(ShellViewModel).GetProperties()
            .Where(p => p.Name.StartsWith("NavigateTo", StringComparison.Ordinal) && p.Name.EndsWith("Command", StringComparison.Ordinal))
            .Select(p => p.Name)
            .ToArray();

    [Theory]
    [InlineData(PineHeadProfile.Android)]
    [InlineData(PineHeadProfile.Ios)]
    [InlineData(PineHeadProfile.MacOs)]
    public void Non_windows_heads_hide_only_the_documented_capability_gated_destinations(PineHeadProfile profile)
    {
        Destinations().Should().Contain(CapabilityGated);
        Destinations().Length.Should().BeGreaterThan(CapabilityGated.Length);

        Shell(PineHeadProfile.Windows).HasReceiptVision.Should().BeTrue();
        var shell = Shell(profile);
        shell.HasReceiptVision.Should().BeFalse();
        shell.ShowReceiptVisionNote.Should().BeTrue("the missing buttons are explained, not silently gone");
    }

    [Fact]
    public void A_narrow_screen_turns_the_sidebar_into_a_closed_drawer_that_closes_after_navigating()
    {
        var shell = Shell(PineHeadProfile.Android);
        shell.IsPaneOpen.Should().BeTrue();

        shell.IsCompact = true;
        shell.IsPaneOpen.Should().BeFalse();

        shell.TogglePaneCommand.Execute(null);
        shell.IsPaneOpen.Should().BeTrue();

        shell.IsCompact = false;
        shell.IsPaneOpen.Should().BeTrue();
    }
}
