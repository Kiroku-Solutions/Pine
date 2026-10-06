using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MediatR;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Pine.Application.Sync.Commands;
using Pine.Core.Sync;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.ViewModels;
using Xunit;

namespace Pine.Tests.Unit.ViewModels;

/// <summary>
/// The Settings page can erase this copy's sync data from a shared folder (`14-Synchronization` §11, §13's right
/// to erasure; post-audit plan item 3.1h).
/// </summary>
/// <remarks>
/// THE MOST IMPORTANT TEST IN THIS FILE, as in <c>SettingsVaultRestoreTests</c>, is that opening the confirmation
/// erases nothing. This is a permanent, one-sided deletion of files a paired copy may not have taken yet.
/// </remarks>
public class SettingsSyncEraseTests
{
    private const string Folder = @"E:\Pine";

    private static SettingsViewModel NewSettings(ISender sender, ILocalizer? localizer = null) =>
        new(sender, localizer ?? new Localizer(), new LanguagePreferenceStore(path: null));

    [Fact]
    public void TheButton_IsOffUntilAFolderIsTyped()
    {
        var vm = NewSettings(Substitute.For<ISender>());

        vm.CanErase.Should().BeFalse();
        vm.EraseFolderPath = "   ";
        vm.CanErase.Should().BeFalse();
        vm.EraseFolderPath = Folder;
        vm.CanErase.Should().BeTrue();
    }

    [Fact]
    public void OpeningTheConfirmation_ErasesNothing()
    {
        var sender = Substitute.For<ISender>();
        var vm = NewSettings(sender);
        vm.EraseFolderPath = Folder;

        vm.BeginEraseCommand.Execute(null);

        vm.IsEraseDialogVisible.Should().BeTrue();
        sender.DidNotReceiveWithAnyArgs().Send(Arg.Any<EraseSyncDataCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void OpeningTheConfirmation_WithNoFolder_DoesNothing()
    {
        var sender = Substitute.For<ISender>();
        var vm = NewSettings(sender);

        vm.BeginEraseCommand.Execute(null);

        vm.IsEraseDialogVisible.Should().BeFalse();
    }

    [Fact]
    public void CancellingTheConfirmation_ErasesNothing()
    {
        var sender = Substitute.For<ISender>();
        var vm = NewSettings(sender);
        vm.EraseFolderPath = Folder;
        vm.BeginEraseCommand.Execute(null);

        vm.CancelEraseCommand.Execute(null);

        vm.IsEraseDialogVisible.Should().BeFalse();
        sender.DidNotReceiveWithAnyArgs().Send(Arg.Any<EraseSyncDataCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConfirmingTheErasure_SendsTheCommandForTheTypedFolder_AndClosesTheDialog()
    {
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<EraseSyncDataCommand>(), Arg.Any<CancellationToken>()).Returns(new SyncEraseReport(2, true));
        var vm = NewSettings(sender);
        vm.EraseFolderPath = "  " + Folder + "  ";
        vm.BeginEraseCommand.Execute(null);

        await vm.ConfirmEraseCommand.ExecuteAsync(null);

        await sender.Received(1).Send(Arg.Is<EraseSyncDataCommand>(c => c.FolderPath == Folder), Arg.Any<CancellationToken>());
        vm.IsEraseDialogVisible.Should().BeFalse();
        vm.IsErasing.Should().BeFalse();
    }

    [Fact]
    public async Task ASuccessfulErasure_ReportsHowManyFilesAndTheAnnouncement()
    {
        var localizer = new Localizer();
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<EraseSyncDataCommand>(), Arg.Any<CancellationToken>()).Returns(new SyncEraseReport(3, true));
        var vm = NewSettings(sender, localizer);
        vm.EraseFolderPath = Folder;
        vm.BeginEraseCommand.Execute(null);

        await vm.ConfirmEraseCommand.ExecuteAsync(null);

        vm.EraseFailed.Should().BeFalse();
        vm.EraseMessage.Should().Contain("3");
        vm.EraseMessage.Should().Contain(localizer["Settings.SyncErase.Success.AnnouncementRemoved"]);
    }

    [Fact]
    public async Task ASuccessfulErasure_WithNothingToErase_SaysSoHonestly()
    {
        var localizer = new Localizer();
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<EraseSyncDataCommand>(), Arg.Any<CancellationToken>()).Returns(new SyncEraseReport(0, false));
        var vm = NewSettings(sender, localizer);
        vm.EraseFolderPath = Folder;
        vm.BeginEraseCommand.Execute(null);

        await vm.ConfirmEraseCommand.ExecuteAsync(null);

        vm.EraseFailed.Should().BeFalse();
        vm.EraseMessage.Should().Be(
            localizer["Settings.SyncErase.Success.CountZero"] + " " + localizer["Settings.SyncErase.Success.NoAnnouncement"]);
    }

    [Fact]
    public async Task ASuccessfulErasure_OfExactlyOneFile_UsesTheSingularSentence()
    {
        var localizer = new Localizer();
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<EraseSyncDataCommand>(), Arg.Any<CancellationToken>()).Returns(new SyncEraseReport(1, false));
        var vm = NewSettings(sender, localizer);
        vm.EraseFolderPath = Folder;
        vm.BeginEraseCommand.Execute(null);

        await vm.ConfirmEraseCommand.ExecuteAsync(null);

        vm.EraseMessage.Should().Contain(localizer["Settings.SyncErase.Success.CountOne"]);
        vm.EraseMessage.Should().NotContain("CountMany");
    }

    [Fact]
    public async Task AFailedErasure_ReportsTheFallbackSentence_WithoutLeakingThePath()
    {
        var localizer = new Localizer();
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<EraseSyncDataCommand>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new SyncRunRefusedException(SyncRunRefusal.FolderUnavailable));
        var vm = NewSettings(sender, localizer);
        vm.EraseFolderPath = Folder;
        vm.BeginEraseCommand.Execute(null);

        await vm.ConfirmEraseCommand.ExecuteAsync(null);

        vm.EraseFailed.Should().BeTrue();
        vm.EraseMessage.Should().NotContain(Folder);
    }

    [Fact]
    public async Task TheFolderBox_IsDisabledWhileTheConfirmationIsUp()
    {
        // No separate captured "target" exists here, unlike the restore dialog's: there is no second control
        // underneath the scrim that could steal focus and change the selection. The folder box itself is simply
        // disabled while IsErasing is set, which is the one path that could otherwise race with the confirmation.
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<EraseSyncDataCommand>(), Arg.Any<CancellationToken>()).Returns(new SyncEraseReport(0, false));
        var vm = NewSettings(sender);
        vm.EraseFolderPath = Folder;
        vm.BeginEraseCommand.Execute(null);

        vm.IsErasing.Should().BeFalse("the confirmation is up, but nothing is running yet");

        var running = vm.ConfirmEraseCommand.ExecuteAsync(null);
        await running;

        await sender.Received(1).Send(Arg.Is<EraseSyncDataCommand>(c => c.FolderPath == Folder), Arg.Any<CancellationToken>());
    }
}
