using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using FluentAssertions;
using MediatR;
using NSubstitute;
using Pine.Application.Sync.Commands;
using Pine.Core.Sync;
using Pine.Tests.Unit.Localization;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.ViewModels;
using Pine.Ui.Avalonia.Views;
using Xunit;

namespace Pine.Tests.Unit.ViewModels;

/// <summary>The "Sync now" page, rendered headlessly (post-audit plan item 3.1f).</summary>
[Collection("ProcessWideState")]
public class SyncRunViewRenderingTests
{
    private static readonly Guid Peer = Guid.Parse("c0ffee12-3456-4789-8abc-def012345678");

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

    private static List<string> VisibleText(Control view)
    {
        Dispatcher.UIThread.RunJobs();
        return view.GetLogicalDescendants().OfType<TextBlock>()
            .Where(t => t.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(t.Text))
            .Select(t => t.Text!)
            .ToList();
    }

    private static void Show(Control view)
    {
        var window = new Window { Content = view, Width = 900, Height = 1400 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
    }

    private static SyncRunViewModel PageAfter(Localizer localizer, SyncRunReport report)
    {
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<RunSyncCommand>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(report));
        var page = new SyncRunViewModel(mediator, localizer) { FolderPath = @"E:\Pine" };
        page.RunCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        return page;
    }

    [Fact]
    public Task BeforeARun_OnlyTheFolderAndTheButtonAreThere() => InEnglish(localizer =>
    {
        using var page = new SyncRunViewModel(Substitute.For<IMediator>(), localizer);
        var view = new SyncRunView { DataContext = page };
        Show(view);

        var text = VisibleText(view);
        text.Should().Contain(localizer["SyncRun.Folder.Help"]);
        text.Should().NotContain(localizer["SyncRun.Result.Title"]);

        var run = view.GetLogicalDescendants().OfType<Button>().Single(b => Equals(b.Content, localizer["SyncRun.Run"]));
        run.IsEffectivelyEnabled.Should().BeFalse("there is no folder yet");
        page.FolderPath = @"E:\Pine";
        Dispatcher.UIThread.RunJobs();
        run.IsEffectivelyEnabled.Should().BeTrue();
    });

    [Fact]
    public Task AReport_AndItsProblems_AreOnScreen() => InEnglish(localizer =>
    {
        using var page = PageAfter(localizer, new SyncRunReport(1, 1, 1, 4, 0, [new SyncRunProblem(Peer, SyncRunProblemKind.NotAuthentic)]));
        var view = new SyncRunView { DataContext = page };
        Show(view);

        var text = VisibleText(view);
        text.Should().Contain(localizer.Format("SyncRun.Result.Rows", "4"));
        text.Should().Contain(localizer["SyncRun.Problems.Title"]);
        text.Should().Contain(line => line.Contains(localizer["SyncRun.Problem.NotAuthentic"], StringComparison.Ordinal));
        text.Should().NotContain(localizer["SyncRun.Result.NoPeers"]);
    });

    [Fact]
    public Task AnEmptyFolder_SaysWhatToDoNext() => InEnglish(localizer =>
    {
        using var page = PageAfter(localizer, new SyncRunReport(0, 0, 0, 0, 0, []));
        var view = new SyncRunView { DataContext = page };
        Show(view);

        var text = VisibleText(view);
        text.Should().Contain(localizer["SyncRun.Result.NoPeers"]);
        text.Should().NotContain(localizer["SyncRun.Problems.Title"]);
    });

    [Fact]
    public Task EveryControlAPersonCanUse_HasAnAccessibleName() => InEnglish(localizer =>
    {
        using var page = new SyncRunViewModel(Substitute.For<IMediator>(), localizer) { FolderPath = @"E:\Pine" };
        var view = new SyncRunView { DataContext = page };
        Show(view);

        var named = view.GetLogicalDescendants().OfType<Control>()
            .Where(c => c is Button or TextBox && c.IsEffectivelyVisible)
            .ToList();

        named.Should().HaveCount(2);
        foreach (var control in named)
        {
            AutomationProperties.GetName(control).Should().NotBeNullOrWhiteSpace();
        }
    });
}
