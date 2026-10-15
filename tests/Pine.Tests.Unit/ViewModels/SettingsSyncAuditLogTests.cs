using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MediatR;
using NSubstitute;
using Pine.Application.Security;
using Pine.Application.Sync.Queries;
using Pine.Core.Sync;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.ViewModels;
using Xunit;

namespace Pine.Tests.Unit.ViewModels;

/// <summary>
/// The Settings page's sync audit log section (`14-Synchronization` §12): a read-only list, newest first, of
/// every attempt <see cref="GetSyncAuditLogQuery"/> hands back.
/// </summary>
public class SettingsSyncAuditLogTests
{
    private static SettingsViewModel NewSettings(ISender sender, ILocalizer? localizer = null) =>
        new(sender, localizer ?? new Localizer(), new LanguagePreferenceStore(path: null));

    private static SyncRunAuditEntry Completed(DateTime whenUtc) => new(
        Guid.NewGuid(), whenUtc, "Folder", EnvelopesUp: 2, EnvelopesDown: 3, ConflictsDetected: 1, ConflictsResolved: 0,
        SyncRunAuditOutcome.Completed, FailureReason: null);

    private static SyncRunAuditEntry Refused(DateTime whenUtc) => new(
        Guid.NewGuid(), whenUtc, "Folder", 0, 0, 0, 0, SyncRunAuditOutcome.Refused, nameof(SyncRunRefusal.NoKey));

    [Fact]
    public async Task Loading_SendsTheQuery_AndFillsTheListInTheOrderItCameBack()
    {
        var sender = Substitute.For<ISender>();
        var newest = Completed(new DateTime(2026, 10, 5, 8, 0, 0, DateTimeKind.Utc));
        var oldest = Refused(new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc));
        sender.Send(Arg.Any<GetSyncAuditLogQuery>(), Arg.Any<CancellationToken>())
            .Returns(new List<SyncRunAuditEntry> { newest, oldest });
        var vm = NewSettings(sender);

        await vm.LoadSyncAuditLogCommand.ExecuteAsync(null);

        await sender.Received(1).Send(Arg.Any<GetSyncAuditLogQuery>(), Arg.Any<CancellationToken>());
        vm.SyncAuditLog.Should().HaveCount(2);
        vm.SyncAuditLog[0].Source.Should().Be(newest);
        vm.SyncAuditLog[1].Source.Should().Be(oldest);
        vm.HasSyncAuditLog.Should().BeTrue();
    }

    [Fact]
    public async Task ACompletedRun_ShowsCountsAndASuccessfulOutcome()
    {
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<GetSyncAuditLogQuery>(), Arg.Any<CancellationToken>())
            .Returns(new List<SyncRunAuditEntry> { Completed(new DateTime(2026, 10, 5, 8, 0, 0, DateTimeKind.Utc)) });
        var vm = NewSettings(sender, new Localizer());

        await vm.LoadSyncAuditLogCommand.ExecuteAsync(null);

        var row = vm.SyncAuditLog.Should().ContainSingle().Subject;
        row.Succeeded.Should().BeTrue();
        row.Outcome.Should().Be("Completed");
        row.Counts.Should().Contain("3").And.Contain("2").And.Contain("1");
    }

    [Fact]
    public async Task ARefusedRun_NamesTheReason_AndIsNotStyledAsSuccess()
    {
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<GetSyncAuditLogQuery>(), Arg.Any<CancellationToken>())
            .Returns(new List<SyncRunAuditEntry> { Refused(new DateTime(2026, 10, 5, 8, 0, 0, DateTimeKind.Utc)) });
        var vm = NewSettings(sender, new Localizer());

        await vm.LoadSyncAuditLogCommand.ExecuteAsync(null);

        var row = vm.SyncAuditLog.Should().ContainSingle().Subject;
        row.Succeeded.Should().BeFalse();
        row.Outcome.Should().StartWith("Refused:");
    }

    [Fact]
    public async Task WhenTheQueryFails_TheListIsEmptyRatherThanStale()
    {
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<GetSyncAuditLogQuery>(), Arg.Any<CancellationToken>())
            .Returns(new List<SyncRunAuditEntry> { Completed(DateTime.UtcNow) });
        var vm = NewSettings(sender);
        await vm.LoadSyncAuditLogCommand.ExecuteAsync(null);
        vm.SyncAuditLog.Should().NotBeEmpty();

        sender.Send(Arg.Any<GetSyncAuditLogQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<SyncRunAuditEntry>>(new InvalidOperationException("locked")));

        await vm.LoadSyncAuditLogCommand.ExecuteAsync(null);

        vm.SyncAuditLog.Should().BeEmpty();
        vm.HasSyncAuditLog.Should().BeFalse();
    }
}
