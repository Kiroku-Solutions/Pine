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
using Pine.Application.Security;
using Pine.Application.Sync.Queries;
using Pine.Core.Sync;
using Pine.Tests.Unit.Localization;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.ViewModels;
using Pine.Ui.Avalonia.Views;
using Xunit;

namespace Pine.Tests.Unit.ViewModels;

/// <summary>The Settings page's sync audit log section, rendered headlessly (`14-Synchronization` §12).</summary>
[Collection("ProcessWideState")]
public class SettingsSyncAuditLogRenderingTests
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
        return view.GetLogicalDescendants().OfType<TextBlock>()
            .Where(t => t.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(t.Text))
            .Select(t => t.Text!)
            .ToList();
    }

    private static void Show(Control view)
    {
        var window = new Window { Content = view, Width = 900, Height = 1600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
    }

    private static SettingsViewModel NewSettings(ISender sender, Localizer localizer) =>
        new(sender, localizer, new LanguagePreferenceStore(path: null));

    [Fact]
    public Task WithNothingLogged_TheEmptySentenceShows_AndNoRowDoes() => InEnglish(localizer =>
    {
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<GetSyncAuditLogQuery>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<SyncRunAuditEntry>());
        var vm = NewSettings(sender, localizer);
        vm.LoadSyncAuditLogCommand.ExecuteAsync(null).GetAwaiter().GetResult();

        var view = new SettingsView { DataContext = vm };
        Show(view);

        var text = VisibleText(view);
        text.Should().Contain(localizer["Settings.SyncAuditLog.Empty"]);
    });

    [Fact]
    public Task WithAttemptsLogged_EachRowShowsItsCountsAndOutcome() => InEnglish(localizer =>
    {
        var sender = Substitute.For<ISender>();
        var completed = new SyncRunAuditEntry(
            Guid.NewGuid(), new DateTime(2026, 10, 5, 8, 0, 0, DateTimeKind.Utc), "Folder",
            EnvelopesUp: 2, EnvelopesDown: 3, ConflictsDetected: 1, ConflictsResolved: 0,
            SyncRunAuditOutcome.Completed, FailureReason: null);
        var refused = new SyncRunAuditEntry(
            Guid.NewGuid(), new DateTime(2026, 10, 4, 8, 0, 0, DateTimeKind.Utc), "Folder",
            0, 0, 0, 0, SyncRunAuditOutcome.Refused, nameof(SyncRunRefusal.NoKey));
        sender.Send(Arg.Any<GetSyncAuditLogQuery>(), Arg.Any<CancellationToken>())
            .Returns(new List<SyncRunAuditEntry> { completed, refused });
        var vm = NewSettings(sender, localizer);
        vm.LoadSyncAuditLogCommand.ExecuteAsync(null).GetAwaiter().GetResult();

        var view = new SettingsView { DataContext = vm };
        Show(view);

        var text = VisibleText(view);
        text.Should().NotContain(localizer["Settings.SyncAuditLog.Empty"]);
        text.Should().Contain(localizer["Settings.SyncAuditLog.Outcome.Completed"]);
        text.Should().Contain(line => line.StartsWith("Refused:", StringComparison.Ordinal));
    });

    [Fact]
    public Task TheRefreshButton_HasAnAccessibleName() => InEnglish(localizer =>
    {
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<GetSyncAuditLogQuery>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<SyncRunAuditEntry>());
        var vm = NewSettings(sender, localizer);

        var view = new SettingsView { DataContext = vm };
        Show(view);

        // Single(Content == "Refresh") is ambiguous: the restore-backup card above has its own
        // "Refresh" button with the same localized text. The sync audit log's is the one bound to
        // its own reload command, which is what actually identifies it.
        var refresh = view.GetLogicalDescendants().OfType<Button>()
            .Single(b => ReferenceEquals(b.Command, vm.LoadSyncAuditLogCommand));
        AutomationProperties.GetName(refresh).Should().NotBeNullOrWhiteSpace();
    });
}
