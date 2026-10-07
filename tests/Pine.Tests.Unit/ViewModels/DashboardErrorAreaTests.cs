using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MediatR;
using NSubstitute;
using Pine.Ui.Avalonia.Composition;
using Pine.Ui.Avalonia.Diagnostics;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.Services;
using Pine.Ui.Avalonia.ViewModels;
using Xunit;

namespace Pine.Tests.Unit.ViewModels;

/// <summary>
/// TD-105: a failed load's sentence has its own slot now, separate from <c>Greeting</c>.
/// </summary>
/// <remarks>
/// Before this, <c>DashboardViewModel.InitializationFailurePolicy.Report</c> pointed at the same
/// <c>LocalizedMessageSlot</c> that renders the greeting, so a failed load replaced "Welcome to
/// Pine" with an error sentence -- and there was nowhere for the two to coexist, because the view
/// had no error surface of its own. <c>ErrorMessage</c> and the <c>TextBlock</c> bound to it in
/// <c>DashboardView.axaml</c> are that surface, mirroring <c>AddTransactionViewModel.ErrorMessage</c>.
/// </remarks>
[Collection("ProcessWideState")]
public class DashboardErrorAreaTests
{
    private static readonly PineHeadCapabilities Windows = PineHeadCapabilities.For(PineHeadProfile.Windows);

    private static DashboardViewModel Dashboard(IMediator mediator, ILocalizer localizer) => new(
        mediator,
        Substitute.For<IViewModelFactory>(),
        Substitute.For<IShellNavigator>(),
        Windows,
        localizer: localizer);

    /// <summary>
    /// Drives the failure path exactly the way <c>PageActivation.Activate</c> does, and checks the
    /// sentence lands on <c>ErrorMessage</c> while <c>Greeting</c> is left untouched.
    /// </summary>
    [Fact]
    public async Task WhenInitialisationFails_TheFailureLandsOnErrorMessage_NotGreeting()
    {
        var mediator = Substitute.For<IMediator>();
        var localizer = new Localizer();
        localizer.SetLanguage("en");
        var dashboard = Dashboard(mediator, localizer);
        var policy = dashboard.InitializationFailurePolicy;

        policy.Report.Should().NotBeNull(
            "the dashboard must have somewhere to put a failed load's sentence now (TD-105)");

        var greetingBefore = dashboard.Greeting;
        greetingBefore.Should().Be(localizer["Dashboard.Header.Greeting"]);
        dashboard.ErrorMessage.Should().BeEmpty("nothing has failed yet");

        ObservedTask.Run(
            () => throw new InvalidOperationException("unreadable"),
            policy.Logger,
            policy.Operation,
            policy.Fallback,
            policy.Report);

        await WaitForAsync(() => !string.IsNullOrEmpty(dashboard.ErrorMessage));

        dashboard.ErrorMessage.Should().Be(localizer["Dashboard.Tile.LoadFailed"],
            "the fallback this policy was given is the same one every tile uses for an unspecific failure");
        dashboard.Greeting.Should().Be(
            greetingBefore,
            "the failure must not overwrite the greeting -- that was exactly TD-105's defect");
        dashboard.Greeting.Should().Be(localizer["Dashboard.Header.Greeting"]);

        dashboard.Dispose();
    }

    /// <summary>
    /// A leftover failure from an earlier, failed visit must not keep showing once the page is
    /// (re-)initialised and nothing fails this time -- there is no success path that calls
    /// <c>Set</c> to overwrite it, so <c>InitializeAsync</c> clears the slot itself up front.
    /// </summary>
    [Fact]
    public async Task AStaleErrorMessage_IsClearedWhenInitializeAsyncRunsAgain()
    {
        var mediator = Substitute.For<IMediator>();
        var localizer = new Localizer();
        localizer.SetLanguage("en");
        var dashboard = Dashboard(mediator, localizer);

        dashboard.ErrorMessage = "a stale failure from an earlier visit";

        await dashboard.InitializeAsync(CancellationToken.None);

        dashboard.ErrorMessage.Should().BeEmpty(
            "a fresh (re-)activation must not leave a previous visit's failure on screen (TD-105)");

        dashboard.Dispose();
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100 && !condition(); attempt++)
        {
            await Task.Delay(20);
        }
    }
}
