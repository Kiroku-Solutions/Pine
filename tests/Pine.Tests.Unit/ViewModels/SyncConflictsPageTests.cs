using System;
using System.Collections.Generic;
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
using Pine.Application.Sync.Queries;
using Pine.Core.Sync;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.ViewModels;
using Pine.Ui.Avalonia.ViewModels.Messages;
using Xunit;

namespace Pine.Tests.Unit.ViewModels;

/// <summary>The sync conflicts page (post-audit plan item 3.1c).</summary>
public class SyncConflictsPageTests
{
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly Localizer _localizer = new(CultureInfo.GetCultureInfo("en-US"));
    private readonly List<ResolveSyncConflictCommand> _sent = new();
    private IReadOnlyList<SyncConflictSummary> _open = [];

    public SyncConflictsPageTests()
    {
        _mediator.Send(Arg.Any<GetSyncConflictsQuery>(), Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult(_open));
        _mediator.Send(Arg.Do<ResolveSyncConflictCommand>(_sent.Add), Arg.Any<CancellationToken>()).Returns(Task.FromResult(MediatR.Unit.Value));
    }

    private static SyncConflictSummary Conflict(
        SyncConflictKind kind = SyncConflictKind.ConcurrentEdit,
        SyncConflictBlock block = SyncConflictBlock.None,
        bool grantsConsent = false,
        bool localExists = true,
        bool remoteIsDeletion = false,
        string table = "Transactions",
        string? label = "Grocer",
        params SyncConflictField[] fields) =>
        new(Guid.NewGuid(), kind, table, Guid.NewGuid(), label, Guid.NewGuid(), Guid.Parse("22222222-2222-4222-8222-222222222222"), 7,
            new DateTime(2026, 9, 18, 10, 0, 0, DateTimeKind.Utc), localExists, remoteIsDeletion, fields, block, grantsConsent, null, null);

    private SyncConflictsViewModel Page() => new(_mediator, _localizer);

    [Fact]
    public async Task WithNothingRecorded_ThePageSaysWhatItIsFor()
    {
        using var page = Page();

        await page.InitializeAsync(CancellationToken.None);

        page.Conflicts.Should().BeEmpty();
        page.IsEmpty.Should().BeTrue();
        page.IsLoading.Should().BeFalse();
        _localizer["SyncConflicts.Empty"].Should().NotBe("SyncConflicts.Empty", "the empty sentence exists in the table");
    }

    [Fact]
    public async Task ARow_SaysWhatHappened_ToWhat_FromWhere_AndBothSidesInWords()
    {
        _open = [Conflict(fields:
        [
            new SyncConflictField("VoidReason", "left", "right"),
            new SyncConflictField("TotalAmount_Amount", -20m, -1234.5m),
            new SyncConflictField("IsEnabled", true, false),
            new SyncConflictField("Notes", null, string.Empty),
            new SyncConflictField("SomethingNew", 3L, 4L),
            new SyncConflictField("TransactionSplits", 1, 2),
        ])];
        using var page = Page();

        await page.InitializeAsync(CancellationToken.None);

        var row = page.Conflicts.Should().ContainSingle().Subject;
        page.IsEmpty.Should().BeFalse();
        row.Title.Should().Be("Changed on both devices");
        row.EntityText.Should().Be("Transaction · Grocer");
        row.MetaText.Should().StartWith("From device 22222222, recorded ");
        row.KeepLocalLabel.Should().Be("Keep this device's version");
        row.TakeRemoteLabel.Should().Be("Use the other device's version");
        row.HasFields.Should().BeTrue();
        row.Fields.Should().Equal(
            new SyncConflictFieldRow("Reason for voiding", "left", "right"),
            new SyncConflictFieldRow("Amount", "-20.00", "-1,234.50"),
            new SyncConflictFieldRow("Enabled", "Yes", "No"),
            new SyncConflictFieldRow("Notes", "Empty", "Empty"),
            new SyncConflictFieldRow("SomethingNew", "3", "4"),
            new SyncConflictFieldRow("Splits", "1", "2"));
        row.CanTakeRemote.Should().BeTrue();
        row.IsTakeRemoteBlocked.Should().BeFalse();
        row.TakeRemoteBlockedText.Should().BeEmpty();
    }

    [Fact]
    public async Task AMissingOrDeletedSide_IsSaidSo_NotLeftBlank()
    {
        _open =
        [
            Conflict(SyncConflictKind.RemoteEditOfLocalDelete, localExists: false, fields: [new SyncConflictField("Payee", null, "Cafe")]),
            Conflict(SyncConflictKind.RemoteDeleteOfLocalEdit, remoteIsDeletion: true, table: "Budgets", label: null, fields: [new SyncConflictField("Category", "Food", null)]),
        ];
        using var page = Page();

        await page.InitializeAsync(CancellationToken.None);

        page.Conflicts[0].Fields.Single().Should().Be(new SyncConflictFieldRow("Payee", "Not on this device", "Cafe"));
        page.Conflicts[0].KeepLocalLabel.Should().Be("Keep it deleted");
        page.Conflicts[0].TakeRemoteLabel.Should().Be("Restore it");
        page.Conflicts[1].Fields.Single().Should().Be(new SyncConflictFieldRow("Category", "Food", "Deleted"));
        page.Conflicts[1].EntityText.Should().Be("Budget");
        page.Conflicts[1].TakeRemoteLabel.Should().Be("Delete it here too");
    }

    [Fact]
    public async Task KeepingThisDevicesSide_SendsTheDecision_ReloadsTheList_AndTellsOtherPages()
    {
        var conflict = Conflict();
        _open = [conflict];
        using var page = Page();
        await page.InitializeAsync(CancellationToken.None);
        var recipient = new object();
        var broadcasts = 0;
        WeakReferenceMessenger.Default.Register<object, DataChangedMessage>(recipient, (_, _) => broadcasts++);

        try
        {
            _open = [];
            await page.KeepLocalCommand.ExecuteAsync(page.Conflicts[0]);
        }
        finally
        {
            WeakReferenceMessenger.Default.UnregisterAll(recipient);
        }

        _sent.Should().ContainSingle().Which.Should().Be(new ResolveSyncConflictCommand(conflict.Id, SyncConflictResolution.KeepLocal, false));
        page.Conflicts.Should().BeEmpty();
        page.IsEmpty.Should().BeTrue();
        page.IsResolving.Should().BeFalse();
        broadcasts.Should().Be(1);
    }

    [Fact]
    public async Task ABlockedSide_IsNotAButton_AndSaysWhy_AndTheCommandSendsNothing()
    {
        _open = [Conflict(SyncConflictKind.MissingParent, SyncConflictBlock.ParentMissing)];
        using var page = Page();
        await page.InitializeAsync(CancellationToken.None);
        var row = page.Conflicts.Single();

        row.CanTakeRemote.Should().BeFalse();
        row.CanPressTakeRemote.Should().BeFalse();
        row.IsTakeRemoteBlocked.Should().BeTrue();
        row.TakeRemoteBlockedText.Should().Be(_localizer["Failure.SyncConflict.ParentMissing"]);

        await page.TakeRemoteCommand.ExecuteAsync(row);

        _sent.Should().BeEmpty();
    }

    [Fact]
    public async Task AConsent_NeedsTheBoxTicked_AndTheTickTravelsWithTheCommand()
    {
        var conflict = Conflict(SyncConflictKind.ConsentNotImplicitlyGranted, grantsConsent: true, table: "Settings", label: "TelemetryConsent");
        _open = [conflict];
        using var page = Page();
        await page.InitializeAsync(CancellationToken.None);
        var row = page.Conflicts.Single();

        row.RequiresConsentConfirmation.Should().BeTrue();
        row.CanPressTakeRemote.Should().BeFalse("one click never grants a consent");
        await page.TakeRemoteCommand.ExecuteAsync(row);
        _sent.Should().BeEmpty();

        row.ConsentConfirmed = true;
        row.CanPressTakeRemote.Should().BeTrue();
        await page.TakeRemoteCommand.ExecuteAsync(row);

        _sent.Should().ContainSingle().Which.Should().Be(new ResolveSyncConflictCommand(conflict.Id, SyncConflictResolution.TakeRemote, true));
    }

    [Fact]
    public async Task AKeptConsent_NeverSendsTheConfirmation_EvenIfTheBoxWasTicked()
    {
        var conflict = Conflict(SyncConflictKind.ConsentNotImplicitlyGranted, grantsConsent: true, table: "Settings");
        _open = [conflict];
        using var page = Page();
        await page.InitializeAsync(CancellationToken.None);
        page.Conflicts[0].ConsentConfirmed = true;

        await page.KeepLocalCommand.ExecuteAsync(page.Conflicts[0]);

        _sent.Single().ConfirmConsentGrant.Should().BeFalse();
    }

    [Fact]
    public async Task ARefusedDecision_IsSaidInTheUsersLanguage_AndTheListStays()
    {
        _open = [Conflict()];
        using var page = Page();
        await page.InitializeAsync(CancellationToken.None);
        _mediator.Send(Arg.Any<ResolveSyncConflictCommand>(), Arg.Any<CancellationToken>())
            .Throws(new SyncConflictRefusedException("log", SyncConflictRefusal.AlreadyResolved));

        await page.KeepLocalCommand.ExecuteAsync(page.Conflicts[0]);

        page.ResolveMessage.Should().Be(_localizer["Failure.SyncConflict.AlreadyResolved"]);
        page.Conflicts.Should().ContainSingle();
        page.IsResolving.Should().BeFalse();
    }

    [Fact]
    public async Task AnUnexpectedFailure_SaysNothingWasChanged_NotTheExceptionsText()
    {
        _open = [Conflict()];
        using var page = Page();
        await page.InitializeAsync(CancellationToken.None);
        _mediator.Send(Arg.Any<ResolveSyncConflictCommand>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("SQLite Error 19: C:\\secret\\pine.db"));

        await page.KeepLocalCommand.ExecuteAsync(page.Conflicts[0]);

        page.ResolveMessage.Should().Be(_localizer["SyncConflicts.Failure.Resolve"]);
    }

    [Fact]
    public async Task ANullRow_SendsNothing()
    {
        using var page = Page();
        await page.KeepLocalCommand.ExecuteAsync(null);
        await page.TakeRemoteCommand.ExecuteAsync(null);
        _sent.Should().BeEmpty();
    }

    [Fact]
    public async Task ALoadFailure_IsNotTheEmptyState()
    {
        _mediator.Send(Arg.Any<GetSyncConflictsQuery>(), Arg.Any<CancellationToken>()).Throws(new InvalidOperationException("boom"));
        using var page = Page();

        var act = () => page.InitializeAsync(CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>("the shell's initialisation policy reports it");
        page.InitializationFailurePolicy.Report!([new Pine.Core.Common.UserFacingMessage("SyncConflicts.Failure.Load")]);

        page.ErrorMessage.Should().Be(_localizer["SyncConflicts.Failure.Load"]);
        page.IsEmpty.Should().BeFalse("a list that could not be read is not a list with nothing in it");
        page.InitializationFailurePolicy.Fallback.Key.Should().Be("SyncConflicts.Failure.Load");
    }

    [Fact]
    public async Task SwitchingLanguage_RewordsTheRows_AndDisposingStopsListening()
    {
        _open = [Conflict()];
        var page = Page();
        await page.InitializeAsync(CancellationToken.None);

        _localizer.SetLanguage("es");
        page.Conflicts.Single().Title.Should().Be("Cambiado en ambos dispositivos");

        page.Dispose();
        _localizer.SetLanguage("en");
        page.Conflicts.Single().Title.Should().Be("Cambiado en ambos dispositivos", "a disposed page no longer follows the language");
    }

    [Fact]
    public void EveryKind_HasItsWords_AndEveryTable_ItsName()
    {
        foreach (var kind in Enum.GetValues<SyncConflictKind>())
        {
            var (title, explanation, keep, take) = SyncConflictRowViewModel.KindKeys(kind);
            foreach (var key in new[] { title, explanation, keep, take })
            {
                _localizer[key].Should().NotBe(key, $"'{key}' ({kind}) is in the English table");
            }
        }

        foreach (var table in new[] { "Accounts", "Transactions", "Budgets", "Settings", "Rules", "Attachments", "ImportProfiles", "Unknown" })
        {
            var key = SyncConflictRowViewModel.EntityKey(table);
            _localizer[key].Should().NotBe(key);
        }

        SyncConflictRowViewModel.EntityKey("Unknown").Should().Be("SyncConflicts.Entity.Other");
        SyncConflictRowViewModel.FieldName("Payee", _localizer).Should().Be("Payee");
        SyncConflictRowViewModel.FieldName("NoSuchColumn", _localizer).Should().Be("NoSuchColumn", "an unnamed column shows as itself, not as nothing");
        var undefined = () => SyncConflictRowViewModel.KindKeys((SyncConflictKind)99);
        undefined.Should().Throw<ArgumentOutOfRangeException>();
    }
}
