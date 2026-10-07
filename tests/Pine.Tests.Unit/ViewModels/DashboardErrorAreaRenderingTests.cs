using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using FluentAssertions;
using MediatR;
using NSubstitute;
using Pine.Tests.Unit.Localization;
using Pine.Ui.Avalonia.Composition;
using Pine.Ui.Avalonia.Diagnostics;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.Services;
using Pine.Ui.Avalonia.ViewModels;
using Pine.Ui.Avalonia.Views;
using Xunit;

namespace Pine.Tests.Unit.ViewModels;

/// <summary>
/// TD-105, rendered: the dashboard's heading and its error area are two different
/// <c>TextBlock</c>s, so a failed load's sentence can be on screen at the same time as "Welcome
/// to Pine" rather than in place of it.
/// </summary>
/// <remarks>
/// Mirrors <c>DashboardTileRenderingTests</c>: a view-model test can prove the right string lands
/// in the right property, but only a render proves the view actually shows it, keeps the other
/// <c>TextBlock</c> visible beside it, and does not show the error area at all while nothing has
/// failed (an <c>IsVisible</c> bound backwards would still leave the view-model tests green).
/// </remarks>
[Collection("ProcessWideState")]
public class DashboardErrorAreaRenderingTests
{
    private static readonly PineHeadCapabilities Windows = PineHeadCapabilities.For(PineHeadProfile.Windows);

    private static Task InEnglish(Action<Localizer> body) => AvaloniaUiThread.On(() =>
    {
        var localizer = Localizer.Instance;
        var previous = localizer.CurrentLanguage;
        localizer.SetLanguage("en");

        try
        {
            body(localizer);
        }
        finally
        {
            localizer.SetLanguage(previous);
        }
    });

    [Fact]
    public Task BeforeAnyFailure_TheGreetingShows_AndTheErrorAreaIsHidden() => InEnglish(localizer =>
    {
        var mediator = Substitute.For<IMediator>();
        var dashboard = new DashboardViewModel(
            mediator, Substitute.For<IViewModelFactory>(), Substitute.For<IShellNavigator>(), Windows, localizer: localizer);

        RenderAndRun(dashboard, view =>
        {
            VisibleText(view).Should().Contain(localizer["Dashboard.Header.Greeting"]);
            VisibleText(view).Should().NotContain(localizer["Dashboard.Tile.LoadFailed"]);
        });

        dashboard.Dispose();
    });

    [Fact]
    public Task AFailedLoad_ShowsTheErrorArea_WhileTheGreetingStaysNormal() => InEnglish(localizer =>
    {
        var mediator = Substitute.For<IMediator>();
        var dashboard = new DashboardViewModel(
            mediator, Substitute.For<IViewModelFactory>(), Substitute.For<IShellNavigator>(), Windows, localizer: localizer);

        RenderAndRun(dashboard, view =>
        {
            var policy = dashboard.InitializationFailurePolicy;

            // Drives the same path PageActivation does -- InitializeAsync itself cannot throw
            // (each tile observes its own failure), so this is how a page-level failure is
            // exercised, exactly as AsyncInitialisationTests does for the other pages.
            ObservedTask.Run(
                () => throw new InvalidOperationException("disk"),
                policy.Logger,
                policy.Operation,
                policy.Fallback,
                policy.Report);

            PumpUntil(() => !string.IsNullOrEmpty(dashboard.ErrorMessage));

            var text = VisibleText(view);
            text.Should().Contain(localizer["Dashboard.Header.Greeting"],
                "the heading must keep saying its greeting while an error shows beside it (TD-105)");
            text.Should().Contain(localizer["Dashboard.Tile.LoadFailed"],
                "the error area must show the failure sentence");
        });

        dashboard.Dispose();
    });

    private static void RenderAndRun(DashboardViewModel dashboard, Action<Control> body)
    {
        var view = new DashboardView { DataContext = dashboard, Width = 900, Height = 700 };
        var window = new Window { Content = view, Width = 900, Height = 700, SizeToContent = SizeToContent.Manual };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            body(view);
        }
        finally
        {
            // Hidden, not closed: this page's controls have theme transitions, and a transition
            // subscribes to a render clock owned by a thread other than the headless session's, so
            // closing throws "a different thread owns it" (same harness limitation noted in
            // BudgetsViewRenderingTests). Hidden windows live until the process ends.
            window.Hide();
        }
    }

    private static List<string> VisibleText(Control view) =>
        view.GetLogicalDescendants()
            .OfType<TextBlock>()
            .Where(text => text.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(text.Text))
            .Select(text => text.Text!)
            .ToList();

    private static void PumpUntil(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200 && !condition(); attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
    }
}
