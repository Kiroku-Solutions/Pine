using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using FluentAssertions;
using MediatR;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Pine.Application.Sync.Commands;
using Pine.Core.Sync;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.ViewModels;
using Pine.Ui.Avalonia.ViewModels.Messages;
using Xunit;

namespace Pine.Tests.Unit.ViewModels;

/// <summary>The "Sync now" page (post-audit plan item 3.1f).</summary>
[Collection("ProcessWideState")]
public class SyncRunPageTests
{
    private static readonly Guid Peer = Guid.Parse("c0ffee12-3456-4789-8abc-def012345678");

    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly Localizer _localizer = new(CultureInfo.GetCultureInfo("en-US"));

    private SyncRunViewModel Page() => new(_mediator, _localizer);

    private void Reports(SyncRunReport report) =>
        _mediator.Send(Arg.Any<RunSyncCommand>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(report));

    [Fact]
    public void TheButton_IsOffUntilAFolderIsTyped()
    {
        using var page = Page();

        page.CanRun.Should().BeFalse();
        page.FolderPath = "   ";
        page.CanRun.Should().BeFalse();
        page.FolderPath = @"E:\Pine";
        page.CanRun.Should().BeTrue();
    }

    [Fact]
    public async Task NothingIsSent_WithNoFolder()
    {
        using var page = Page();

        await page.RunCommand.ExecuteAsync(null);

        await _mediator.DidNotReceive().Send(Arg.Any<RunSyncCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ARun_SendsTheFolderTrimmed_AndSaysWhatItDid()
    {
        Reports(new SyncRunReport(2, 1, 2, 5, 0, []));
        using var page = Page();
        page.FolderPath = "  E:\\Pine  ";

        await page.RunCommand.ExecuteAsync(null);

        await _mediator.Received(1).Send(Arg.Is<RunSyncCommand>(c => c.FolderPath == "E:\\Pine"), Arg.Any<CancellationToken>());
        page.HasReport.Should().BeTrue();
        page.FoundNoPeers.Should().BeFalse();
        page.HasProblems.Should().BeFalse();
        page.IsRunning.Should().BeFalse();
        page.ErrorMessage.Should().BeNull();
        page.Summary.Should().Equal(
            _localizer.Format("SyncRun.Result.Peers", "2"),
            _localizer.Format("SyncRun.Result.Applied", "1"),
            _localizer.Format("SyncRun.Result.Rows", "5"),
            _localizer.Format("SyncRun.Result.Left", "2"));
    }

    [Fact]
    public async Task ConflictsAreMentioned_OnlyWhenThereAreSome()
    {
        Reports(new SyncRunReport(1, 1, 0, 1, 3, []));
        using var page = Page();
        page.FolderPath = @"E:\Pine";

        await page.RunCommand.ExecuteAsync(null);

        page.Summary.Should().Contain(_localizer.Format("SyncRun.Result.Conflicts", "3"));
    }

    [Fact]
    public async Task AFolderNobodyElseHasUsed_SaysSo()
    {
        Reports(new SyncRunReport(0, 0, 0, 0, 0, []));
        using var page = Page();
        page.FolderPath = @"E:\Pine";

        await page.RunCommand.ExecuteAsync(null);

        page.FoundNoPeers.Should().BeTrue();
        page.Summary.Should().NotContain(line => line.Contains("SyncRun.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EachProblem_NamesTheCopyAndWhatToDo()
    {
        Reports(new SyncRunReport(1, 0, 1, 0, 0,
            [new SyncRunProblem(Peer, SyncRunProblemKind.WrongKey), new SyncRunProblem(Peer, SyncRunProblemKind.TransferFailed)]));
        using var page = Page();
        page.FolderPath = @"E:\Pine";

        await page.RunCommand.ExecuteAsync(null);

        page.HasProblems.Should().BeTrue();
        page.Problems.Should().Equal(
            _localizer.Format("SyncRun.Problem.Line", "C0FFEE12", _localizer["SyncRun.Problem.WrongKey"]),
            _localizer.Format("SyncRun.Problem.Line", "C0FFEE12", _localizer["SyncRun.Problem.TransferFailed"]));
    }

    [Fact]
    public void EveryProblem_HasItsOwnSentence_InEveryLanguage()
    {
        var keys = Enum.GetValues<SyncRunProblemKind>().Select(SyncRunViewModel.ProblemKey).ToList();

        keys.Should().OnlyHaveUniqueItems("a person told the same thing about two different problems cannot act on either");
        foreach (var code in new[] { "en", "es" })
        {
            var table = Localizer.TableOf(code);
            keys.Should().OnlyContain(key => table.ContainsKey(key), $"the '{code}' table words every problem");
        }
    }

    [Fact]
    public void ACopy_IsNamedByTheStartOfItsVaultId()
    {
        SyncRunViewModel.CopyName(Peer).Should().Be("C0FFEE12");
    }

    [Fact]
    public async Task ARefusedRun_SaysWhy_AndLeavesNoOldReportOnScreen()
    {
        Reports(new SyncRunReport(1, 1, 1, 1, 0, []));
        using var page = Page();
        page.FolderPath = @"E:\Pine";
        await page.RunCommand.ExecuteAsync(null);
        page.HasReport.Should().BeTrue();

        _mediator.Send(Arg.Any<RunSyncCommand>(), Arg.Any<CancellationToken>())
            .Throws(new SyncRunRefusedException(SyncRunRefusal.FolderUnavailable));
        await page.RunCommand.ExecuteAsync(null);

        page.ErrorMessage.Should().Be(_localizer["Failure.SyncRun.FolderUnavailable"]);
        page.HasReport.Should().BeFalse("an old report beside a failure reads as this run's");
        page.Summary.Should().BeEmpty();
        page.Problems.Should().BeEmpty();
        page.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task AFailedRun_ShowsThePagesOwnSentence_NeverTheException()
    {
        _mediator.Send(Arg.Any<RunSyncCommand>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("SQLite Error 5: database is locked at E:\\secret"));
        using var page = Page();
        page.FolderPath = @"E:\Pine";

        await page.RunCommand.ExecuteAsync(null);

        page.ErrorMessage.Should().Be(_localizer["SyncRun.Failure.Run"]);
    }

    [Fact]
    public async Task ASuccessfulRunAfterAFailure_ClearsTheFailure()
    {
        _mediator.Send(Arg.Any<RunSyncCommand>(), Arg.Any<CancellationToken>())
            .Throws(new SyncRunRefusedException(SyncRunRefusal.NoKey));
        using var page = Page();
        page.FolderPath = @"E:\Pine";
        await page.RunCommand.ExecuteAsync(null);
        page.ErrorMessage.Should().NotBeNull();

        Reports(new SyncRunReport(0, 0, 0, 0, 0, []));
        await page.RunCommand.ExecuteAsync(null);

        page.ErrorMessage.Should().BeNull();
        page.HasReport.Should().BeTrue();
    }

    [Fact]
    public async Task TheReport_FollowsALanguageChange()
    {
        Reports(new SyncRunReport(2, 0, 0, 0, 0, [new SyncRunProblem(Peer, SyncRunProblemKind.Gap)]));
        using var page = Page();
        page.FolderPath = @"E:\Pine";
        await page.RunCommand.ExecuteAsync(null);

        _localizer.SetLanguage("es");

        page.Summary[0].Should().Be(Localizer.TableOf("es")["SyncRun.Result.Peers"].Replace("{0}", "2", StringComparison.Ordinal));
        page.Problems.Single().Should().Contain(Localizer.TableOf("es")["SyncRun.Problem.Gap"]);
    }

    [Fact]
    public async Task OtherPagesAreTold_OnlyWhenTheRunChangedSomethingTheyShow()
    {
        var told = 0;
        var recipient = new object();
        WeakReferenceMessenger.Default.Register<object, DataChangedMessage>(recipient, (_, _) => told++);
        try
        {
            using var page = Page();
            page.FolderPath = @"E:\Pine";

            Reports(new SyncRunReport(1, 0, 1, 0, 0, []));
            await page.RunCommand.ExecuteAsync(null);
            told.Should().Be(0, "leaving changes for another copy changes nothing here");

            Reports(new SyncRunReport(1, 1, 0, 2, 0, []));
            await page.RunCommand.ExecuteAsync(null);
            told.Should().Be(1);

            Reports(new SyncRunReport(1, 1, 0, 0, 1, []));
            await page.RunCommand.ExecuteAsync(null);
            told.Should().Be(2, "a recorded conflict is something the conflicts page shows");
        }
        finally
        {
            WeakReferenceMessenger.Default.UnregisterAll(recipient);
        }
    }
}
