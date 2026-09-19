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
using Pine.Application.Sync.Queries;
using Pine.Core.Sync;
using Pine.Tests.Unit.Localization;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.ViewModels;
using Pine.Ui.Avalonia.Views;
using Xunit;

namespace Pine.Tests.Unit.ViewModels;

/// <summary>The sync conflicts page, rendered headlessly (post-audit plan item 3.1c).</summary>
[Collection("ProcessWideState")]
public class SyncConflictsViewRenderingTests
{
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
        return view.GetLogicalDescendants()
            .OfType<TextBlock>()
            .Where(text => text.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(text.Text))
            .Select(text => text.Text!)
            .ToList();
    }

    private static Button? VisibleButton(Control view, string label) =>
        view.GetLogicalDescendants().OfType<Button>().FirstOrDefault(b => b.IsEffectivelyVisible && Equals(b.Content, label));

    private static SyncConflictSummary Conflict(SyncConflictKind kind, SyncConflictBlock block = SyncConflictBlock.None, bool grantsConsent = false) =>
        new(Guid.NewGuid(), kind, "Transactions", Guid.NewGuid(), "Grocer", Guid.NewGuid(), Guid.NewGuid(), 7,
            DateTime.UtcNow, true, false, [new SyncConflictField("Payee", "Grocer", "Grocery store")], block, grantsConsent, null, null);

    [Fact]
    public Task TheEmptyPage_SaysWhatItIsFor() => InEnglish(localizer =>
    {
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<GetSyncConflictsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<SyncConflictSummary>>([]));
        using var page = new SyncConflictsViewModel(mediator, localizer);
        page.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();

        var view = new SyncConflictsView { DataContext = page };
        var window = new Window { Content = view, Width = 900, Height = 900 };
        window.Show();
        try
        {
            VisibleText(view).Should().Contain([localizer["SyncConflicts.Header.Title"], localizer["SyncConflicts.Empty"]]);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public Task ARow_ShowsBothSides_OffersOnlyTheSafeChoice_AndItsButtonsResolve() => InEnglish(localizer =>
    {
        var mediator = Substitute.For<IMediator>();
        var concurrent = Conflict(SyncConflictKind.ConcurrentEdit);
        var orphan = Conflict(SyncConflictKind.MissingParent, SyncConflictBlock.ParentMissing);
        mediator.Send(Arg.Any<GetSyncConflictsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<SyncConflictSummary>>([concurrent, orphan]));
        var sent = new List<ResolveSyncConflictCommand>();
        mediator.Send(Arg.Do<ResolveSyncConflictCommand>(sent.Add), Arg.Any<CancellationToken>()).Returns(Task.FromResult(MediatR.Unit.Value));
        using var page = new SyncConflictsViewModel(mediator, localizer);
        page.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();

        var view = new SyncConflictsView { DataContext = page };
        var window = new Window { Content = view, Width = 900, Height = 1400 };
        window.Show();
        try
        {
            var text = VisibleText(view);
            text.Should().Contain(["Changed on both devices", "Grocery store", localizer["SyncConflicts.Column.OtherDevice"], localizer["Failure.SyncConflict.ParentMissing"]]);
            text.Should().NotContain(localizer["SyncConflicts.Empty"]);

            var take = VisibleButton(view, "Use the other device's version");
            take.Should().NotBeNull();
            AutomationProperties.GetName(take!).Should().Be("Use the other device's version · Transaction · Grocer",
                "every button has an accessible name, and it names the item: every row's buttons share their labels (independent review)");
            AutomationProperties.GetName(VisibleButton(view, "Keep this device's version")!).Should().Be("Keep this device's version · Transaction · Grocer");
            view.GetLogicalDescendants().OfType<Button>().Count(b => b.IsEffectivelyVisible && Equals(b.Content, "Use the other device's version"))
                .Should().Be(1, "the orphan's unsafe choice is not a button at all");

            take.Command!.Execute(take.CommandParameter);
            Dispatcher.UIThread.RunJobs();
            sent.Should().ContainSingle().Which.ConflictId.Should().Be(concurrent.Id, "the row's own button resolves that row");
        }
        finally
        {
            window.Close();
        }
    });
}
