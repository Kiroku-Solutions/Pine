using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using Pine.Application.Sync.Commands;
using Pine.Core.Sync;
using Xunit;

namespace Pine.Tests.Unit.Application.Sync;

/// <summary>
/// <see cref="RunSyncCommandHandler"/> logs every attempt through <see cref="ISyncAuditLog"/>, whatever the
/// runner does (`14-Synchronization` §12).
/// </summary>
public class RunSyncCommandAuditLoggingTests
{
    private static readonly string Folder = Path.Combine(Path.GetTempPath(), "shared");

    [Fact]
    public async Task ASuccessfulRun_IsLoggedAsCompleted_WithTheReportsCounts()
    {
        var runner = Substitute.For<ISyncRunner>();
        var report = new SyncRunReport(PeersFound: 1, EnvelopesApplied: 2, EnvelopesLeft: 3, RowsChanged: 9, ConflictsRecorded: 4, Problems: []);
        runner.RunThroughFolderAsync(Folder, Arg.Any<CancellationToken>()).Returns(report);
        var auditLog = Substitute.For<ISyncAuditLog>();

        await new RunSyncCommandHandler(runner, auditLog).Handle(new RunSyncCommand(Folder), CancellationToken.None);

        await auditLog.Received(1).RecordAsync(
            "Folder",
            envelopesUp: 3,    // EnvelopesLeft
            envelopesDown: 2,  // EnvelopesApplied
            conflictsDetected: 4,
            conflictsResolved: 0,
            SyncRunAuditOutcome.Completed,
            null,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ARefusedRun_IsLoggedAsRefused_WithTheRefusalReason_AndNothingElseWritten()
    {
        var runner = Substitute.For<ISyncRunner>();
        runner.RunThroughFolderAsync(Folder, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<SyncRunReport>(new SyncRunRefusedException(SyncRunRefusal.NoKey)));
        var auditLog = Substitute.For<ISyncAuditLog>();

        await FluentActions
            .Awaiting(() => new RunSyncCommandHandler(runner, auditLog).Handle(new RunSyncCommand(Folder), CancellationToken.None))
            .Should().ThrowAsync<SyncRunRefusedException>();

        await auditLog.Received(1).RecordAsync(
            "Folder", 0, 0, 0, 0, SyncRunAuditOutcome.Refused, nameof(SyncRunRefusal.NoKey), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnUnexpectedFailure_IsLoggedAsFailed_WithTheExceptionTypeOnly_NeverItsMessage()
    {
        var runner = Substitute.For<ISyncRunner>();
        runner.RunThroughFolderAsync(Folder, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<SyncRunReport>(new InvalidOperationException("contains a secret path: C:\\Users\\me\\vault")));
        var auditLog = Substitute.For<ISyncAuditLog>();

        await FluentActions
            .Awaiting(() => new RunSyncCommandHandler(runner, auditLog).Handle(new RunSyncCommand(Folder), CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();

        await auditLog.Received(1).RecordAsync(
            "Folder", 0, 0, 0, 0, SyncRunAuditOutcome.Failed, nameof(InvalidOperationException), Arg.Any<CancellationToken>());

        // TD-24: the failure reason is the exception's TYPE name, never its Message.
        await auditLog.DidNotReceive().RecordAsync(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(),
            Arg.Any<SyncRunAuditOutcome>(),
            Arg.Is<string?>(reason => reason != null && reason.Contains("secret", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ACancelledRun_IsNotLogged()
    {
        var runner = Substitute.For<ISyncRunner>();
        runner.RunThroughFolderAsync(Folder, Arg.Any<CancellationToken>())
            .Returns(Task.FromCanceled<SyncRunReport>(new CancellationToken(canceled: true)));
        var auditLog = Substitute.For<ISyncAuditLog>();

        await FluentActions
            .Awaiting(() => new RunSyncCommandHandler(runner, auditLog).Handle(new RunSyncCommand(Folder), CancellationToken.None))
            .Should().ThrowAsync<OperationCanceledException>();

        await auditLog.DidNotReceiveWithAnyArgs().RecordAsync(
            default!, default, default, default, default, default, default, default);
    }
}
