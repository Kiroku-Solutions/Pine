using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Pine.Core.Diagnostics;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Pine.Tests.Unit.ViewModels;

/// <summary>
/// Covers the property TD-15 is actually about: every value this panel shows was MEASURED, and the
/// panel never adds one of its own.
/// </summary>
/// <remarks>
/// <para>
/// WHY THE CONSTRUCTOR TEST IS THE IMPORTANT ONE. The old view model wrote seven log lines in its
/// constructor -- two of them asserting facts nothing had checked ("Migrations are up to date.",
/// "No active internet connection detected.") -- alongside a literal
/// <c>"Pine Vault v1.0.0-beta.1"</c> and a literal <c>"Connected (Healthy)"</c>. None of that was a
/// stub awaiting a backend; it was shipped content that read as the application's own output. A
/// test that only checked "after a refresh the values are right" would have passed against that
/// code too, because the fabrications sat happily alongside real data. So the first test asserts
/// the negative: before anything is asked of it, the panel knows nothing and says so.
/// </para>
/// <para>
/// WHAT THESE TESTS DELIBERATELY DO NOT PROVE. Both seams are NSubstitute doubles, so nothing here
/// touches a log directory or a database. That the real reader tolerates a missing log directory,
/// and that the real probe reports rather than throws, are properties of the infrastructure
/// implementations and are covered on that side.
/// </para>
/// </remarks>
public class DiagnosticsViewModelTests
{
    private readonly ILogTailReader _logTailReader = Substitute.For<ILogTailReader>();
    private readonly IDatabaseHealthProbe _databaseHealthProbe = Substitute.For<IDatabaseHealthProbe>();
    private readonly IRuntimeInfoProvider _runtimeInfoProvider = Substitute.For<IRuntimeInfoProvider>();

    // A real, private Localizer -- not Localizer.Instance, which another test class switches the
    // language of -- so EmptyLogMessage can be asserted by VALUE against the same table the view
    // model renders through (TD-150), the way BudgetsPageCreateTests and SyncConflictsPageTests
    // assert their own localized text.
    private readonly Localizer _localizer = new();

    private DiagnosticsViewModel CreateViewModel()
    {
        return new DiagnosticsViewModel(_logTailReader, _databaseHealthProbe, _runtimeInfoProvider, localizer: _localizer);
    }

    private static LogTailEntry Entry(string message, string level = "info")
    {
        return new LogTailEntry(
            new DateTimeOffset(2026, 3, 14, 9, 26, 53, TimeSpan.Zero),
            level,
            "Pine.Application.Vault",
            message);
    }

    private void GivenTail(params LogTailEntry[] entries)
    {
        _logTailReader
            .ReadTailAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<LogTailEntry>>(entries));
    }

    private void GivenHealth(DatabaseHealth health)
    {
        _databaseHealthProbe
            .CheckAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(health));
    }

    [Fact]
    public void Constructor_ShouldReadNothing_AndDisplayNothingItHasNotMeasured()
    {
        // Arrange: every seam is configured to fail the test if it is touched at all. The point is
        // not "the constructor produced the wrong values" but "the constructor must not have an
        // opinion yet", which is only checkable by watching the seams.
        _logTailReader
            .ReadTailAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("The constructor must not read the log."));
        _databaseHealthProbe
            .CheckAsync(Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("The constructor must not probe the database."));
        _runtimeInfoProvider.AppVersion
            .Returns(_ => throw new InvalidOperationException("The constructor must not read runtime info."));

        // Act
        var vm = CreateViewModel();

        // Assert
        vm.LogEntries.Should().BeEmpty("the seven fabricated constructor log lines are the whole of TD-15");
        vm.AppVersion.Should().Be(_localizer["Diagnostics.NotMeasured"]);
        vm.OsDescription.Should().Be(_localizer["Diagnostics.NotMeasured"]);
        vm.DatabaseStatus.Should().Be(_localizer["Diagnostics.NotMeasured"]);
        vm.ManagedMemory.Should().Be(_localizer["Diagnostics.NotMeasured"]);

        // Not "no records yet" either: the panel has not looked, and saying the log is empty would
        // be an invention of exactly the same kind as saying it is full.
        vm.HasNoLogRecords.Should().BeFalse();

        // No DidNotReceive assertion is needed here and none is written: the substitutes above
        // throw on contact, so a constructor that touched either seam would have failed this test
        // before reaching a single assertion.
    }

    [Fact]
    public void Constructor_ShouldRejectMissingSeams()
    {
        // A diagnostics panel silently constructed without its sources would fall back to showing
        // nothing, which is indistinguishable on screen from a quiet system.
        var act1 = () => new DiagnosticsViewModel(null!, _databaseHealthProbe, _runtimeInfoProvider);
        var act2 = () => new DiagnosticsViewModel(_logTailReader, null!, _runtimeInfoProvider);
        var act3 = () => new DiagnosticsViewModel(_logTailReader, _databaseHealthProbe, null!);

        act1.Should().Throw<ArgumentNullException>();
        act2.Should().Throw<ArgumentNullException>();
        act3.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task RefreshAsync_ShouldShowExactlyWhatTheReaderReturned_InOrder()
    {
        // Arrange
        GivenHealth(DatabaseHealth.Healthy);
        GivenTail(
            Entry("Vault opened."),
            Entry("Import completed.", "warn"),
            Entry("Vault locked."));
        var vm = CreateViewModel();

        // Act
        await vm.RefreshAsync();

        // Assert: the COUNT is the assertion that matters. A panel that appends a "read N records"
        // line of its own would still satisfy every content check below.
        vm.LogEntries.Should().HaveCount(3);
        vm.LogEntries.Select(e => e.Message).Should().Equal(
            "Vault opened.",
            "Import completed.",
            "Vault locked.");
        vm.LogEntries.Select(e => e.Level).Should().Equal("info", "warn", "info");
        vm.LogEntries[0].Timestamp.Should().Be(new DateTimeOffset(2026, 3, 14, 9, 26, 53, TimeSpan.Zero));
        vm.HasNoLogRecords.Should().BeFalse();
        vm.IsLoading.Should().BeFalse();
    }

    [Fact]
    public async Task RefreshAsync_ShouldReadABoundedTail()
    {
        // The panel must never ask for "everything": a Debug-level day's worth of records is not a
        // thing to pull into a list control.
        GivenHealth(DatabaseHealth.Healthy);
        GivenTail();
        var vm = CreateViewModel();

        await vm.RefreshAsync();

        await _logTailReader.Received(1).ReadTailAsync(
            DiagnosticsViewModel.MaxLogEntries,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefreshAsync_ShouldRenderAnEmptyLogAsAnEmptyLog()
    {
        // Arrange: no log file yet is a real, displayable state.
        GivenHealth(DatabaseHealth.Healthy);
        GivenTail();
        var vm = CreateViewModel();

        // Act
        await vm.RefreshAsync();

        // Assert: no placeholder row. Padding an empty tail with a synthetic "initialised" record
        // is precisely the behaviour TD-15 removed.
        vm.LogEntries.Should().BeEmpty();
        vm.HasNoLogRecords.Should().BeTrue();
        vm.EmptyLogMessage.Should().Be(_localizer["Diagnostics.Log.Empty"]);
    }

    [Fact]
    public async Task RefreshAsync_ShouldReportRuntimeFactsVerbatim()
    {
        // Arrange: values chosen so that a surviving literal cannot possibly coincide with them.
        // This is the regression test for AppVersion = "Pine Vault v1.0.0-beta.1", a string frozen
        // at whatever the build had been on the day someone typed it.
        GivenHealth(DatabaseHealth.Healthy);
        GivenTail();
        _runtimeInfoProvider.AppVersion.Returns("9.9.9-td15-probe+abc123");
        _runtimeInfoProvider.OsDescription.Returns("Contoso OS 42 (x64-probe)");
        _runtimeInfoProvider.ManagedMemoryBytes.Returns(42L * 1024 * 1024);
        var vm = CreateViewModel();

        // Act
        await vm.RefreshAsync();

        // Assert
        vm.AppVersion.Should().Be("9.9.9-td15-probe+abc123");
        vm.OsDescription.Should().Be("Contoso OS 42 (x64-probe)");
        vm.AppVersion.Should().NotContain("1.0.0-beta.1");

        // The number is the provider's; only the unit and the rounding are the view model's.
        vm.ManagedMemory.Should().Contain("42");
        vm.ManagedMemory.Should().Contain("MB");
    }

    [Theory]
    [InlineData(DatabaseHealthStatus.Locked)]
    [InlineData(DatabaseHealthStatus.Healthy)]
    [InlineData(DatabaseHealthStatus.PendingMigrations)]
    [InlineData(DatabaseHealthStatus.Unreachable)]
    [InlineData(DatabaseHealthStatus.Faulted)]
    public async Task RefreshAsync_ShouldDescribeEveryHealthStatus(DatabaseHealthStatus status)
    {
        // Arrange
        GivenTail();
        GivenHealth(new DatabaseHealth(status, status == DatabaseHealthStatus.PendingMigrations ? 3 : 0));
        var vm = CreateViewModel();

        // Act
        await vm.RefreshAsync();

        // Assert: every member of the closed set must produce real wording. A status that fell
        // through to an empty string would render as a blank card, which a user reads as "fine" --
        // the same failure as the literal "Connected (Healthy)" it replaced.
        vm.DatabaseStatus.Should().NotBeNullOrWhiteSpace();
        vm.DatabaseStatus.Should().NotBe(_localizer["Diagnostics.NotMeasured"]);
    }

    [Fact]
    public async Task RefreshAsync_ShouldGiveDistinctWordingToEachHealthStatus()
    {
        // Two statuses sharing a phrase would make the card unable to distinguish "the vault is
        // locked, which is normal" from "the database will not answer, which is not".
        GivenTail();
        var descriptions = new List<string>();

        foreach (var status in Enum.GetValues<DatabaseHealthStatus>())
        {
            GivenHealth(new DatabaseHealth(status, 3));
            var vm = CreateViewModel();
            await vm.RefreshAsync();
            descriptions.Add(vm.DatabaseStatus);
        }

        descriptions.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task RefreshAsync_ShouldIncludeThePendingMigrationCount()
    {
        // Arrange: "pending migrations" without the number does not tell a user whether they are
        // one release behind or holding a vault written by a future build.
        GivenTail();
        GivenHealth(new DatabaseHealth(DatabaseHealthStatus.PendingMigrations, 7));
        var vm = CreateViewModel();

        // Act
        await vm.RefreshAsync();

        // Assert
        vm.DatabaseStatus.Should().Contain("7");
        vm.DatabaseStatus.Should().Contain("migration");
    }

    [Fact]
    public async Task RefreshAsync_ShouldNotSurfaceExceptionDetail_WhenTheProbeFaulted()
    {
        // Arrange: the probe never throws by contract -- it reports Faulted and puts the reason in
        // the log. The view model therefore has no exception in reach, and this test pins that the
        // wording stays a fixed neutral phrase rather than growing an ex.Message channel (TD-23);
        // a SQLite or EF message routinely carries the vault file path.
        GivenTail();
        GivenHealth(DatabaseHealth.Faulted);
        var vm = CreateViewModel();

        // Act
        await vm.RefreshAsync();

        // Assert
        vm.DatabaseStatus.Should().Be(_localizer["Diagnostics.Database.Unavailable"]);
        vm.DatabaseStatus.Should().NotContain("Exception");
        vm.DatabaseStatus.Should().NotContain("SQLite");
        vm.DatabaseStatus.Should().NotContain("\\");
        vm.DatabaseStatus.Should().NotContain("/");
        vm.DatabaseStatus.Should().NotContain(".db");
    }

    [Fact]
    public async Task RefreshAsync_ShouldReplaceTheTail_NotAppendIt()
    {
        // Arrange
        GivenHealth(DatabaseHealth.Healthy);
        GivenTail(Entry("Vault opened."), Entry("Vault locked."));
        var vm = CreateViewModel();

        // Act
        await vm.RefreshAsync();
        await vm.RefreshAsync();

        // Assert: an append-only refresh manufactures duplicate records, which is fabrication by a
        // slower route -- the user would be reading two "Vault opened." lines for one open.
        vm.LogEntries.Should().HaveCount(2);
        vm.LogEntries.Select(e => e.Message).Should().Equal("Vault opened.", "Vault locked.");
    }

    [Fact]
    public async Task RefreshAsync_ShouldClearTheEmptyState_WhenRecordsAppear()
    {
        // Arrange: the log was empty when the panel opened and has since been written to.
        GivenHealth(DatabaseHealth.Healthy);
        GivenTail();
        var vm = CreateViewModel();
        await vm.RefreshAsync();
        vm.HasNoLogRecords.Should().BeTrue();

        // Act
        GivenTail(Entry("Import completed."));
        await vm.RefreshAsync();

        // Assert
        vm.HasNoLogRecords.Should().BeFalse();
        vm.LogEntries.Should().ContainSingle();
    }

    [Fact]
    public async Task ClearViewCommand_ShouldEmptyTheViewOnly()
    {
        // Arrange
        GivenHealth(DatabaseHealth.Healthy);
        GivenTail(Entry("Vault opened."), Entry("Vault locked."));
        var vm = CreateViewModel();
        await vm.RefreshAsync();
        _logTailReader.ClearReceivedCalls();

        // Act
        vm.ClearViewCommand.Execute(null);

        // Assert: it clears a screen buffer and nothing else. The old command was labelled "Clear"
        // and appended "Logs cleared by user." -- wording that claimed Pine had deleted the log
        // while it had in fact deleted nothing and then fabricated one more record to say so.
        vm.LogEntries.Should().BeEmpty();
        vm.HasNoLogRecords.Should().BeTrue();
        vm.EmptyLogMessage.Should().Be(_localizer["Diagnostics.Log.ViewCleared"]);
        vm.EmptyLogMessage.Should().NotBe(_localizer["Diagnostics.Log.Empty"],
            "a cleared view is not evidence that the log is empty");
        _ = _logTailReader.DidNotReceiveWithAnyArgs().ReadTailAsync(default, default);
    }

    /// <summary>
    /// The empty-state line is a key, not a frozen sentence (TD-150): a language switch re-words
    /// whichever empty state is showing, the same guarantee <c>LocalizedMessageSlot</c> already gives
    /// <see cref="DiagnosticsViewModel.ErrorMessage"/>.
    /// </summary>
    [Fact]
    public async Task EmptyLogMessage_IsWordedAgain_WhenTheLanguageChanges()
    {
        GivenHealth(DatabaseHealth.Healthy);
        GivenTail();
        var vm = CreateViewModel();
        await vm.RefreshAsync();

        vm.EmptyLogMessage.Should().Be(_localizer["Diagnostics.Log.Empty"]);

        _localizer.SetLanguage("es");

        vm.EmptyLogMessage.Should().Be(_localizer["Diagnostics.Log.Empty"]);
        vm.EmptyLogMessage.Should().NotBe("No log records yet.");

        vm.ClearViewCommand.Execute(null);
        vm.EmptyLogMessage.Should().Be(_localizer["Diagnostics.Log.ViewCleared"]);

        _localizer.SetLanguage("en");
        vm.EmptyLogMessage.Should().Be(_localizer["Diagnostics.Log.ViewCleared"]);

        vm.Dispose();
    }

    /// <summary>A disposed panel stops reacting to the process-lifetime localizer (TD-150, mirroring TD-34).</summary>
    [Fact]
    public void ADisposedDiagnosticsViewModel_StopsListeningToTheLanguage()
    {
        var vm = CreateViewModel();
        vm.Dispose();

        var notified = false;
        vm.PropertyChanged += (_, e) => notified |= e.PropertyName == nameof(DiagnosticsViewModel.EmptyLogMessage);
        _localizer.SetLanguage("es");

        notified.Should().BeFalse("the localizer lives for the whole process and would keep a disposed view model alive");
        _localizer.SetLanguage("en");
    }

    [Fact]
    public async Task ClearView_ThenRefresh_ShouldBringTheRecordsBack()
    {
        // The clearest proof that the file was untouched: the same records read again.
        GivenHealth(DatabaseHealth.Healthy);
        GivenTail(Entry("Vault opened."));
        var vm = CreateViewModel();
        await vm.RefreshAsync();

        vm.ClearViewCommand.Execute(null);
        await vm.RefreshAsync();

        vm.LogEntries.Should().ContainSingle();
        vm.HasNoLogRecords.Should().BeFalse();
        vm.EmptyLogMessage.Should().Be(_localizer["Diagnostics.Log.Empty"]);
    }

    [Fact]
    public async Task LogEntry_ShouldCarryTheCategoryAndUtcInstantIntoItsCopyText()
    {
        // Copy output is read away from this screen, so it carries the two fields the terminal
        // columns drop. It must still contain only what the reader supplied.
        GivenHealth(DatabaseHealth.Healthy);
        GivenTail(Entry("Import completed.", "warn"));
        var vm = CreateViewModel();

        await vm.RefreshAsync();

        var text = vm.LogEntries.Single().Text;
        text.Should().Contain("2026-03-14 09:26:53");
        text.Should().Contain("WARN");
        text.Should().Contain("Pine.Application.Vault");
        text.Should().Contain("Import completed.");
    }
}
