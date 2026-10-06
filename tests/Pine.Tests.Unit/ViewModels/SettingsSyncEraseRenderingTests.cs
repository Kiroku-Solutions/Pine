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

/// <summary>
/// The Settings page's "erase this copy's sync data" section, rendered headlessly (`14-Synchronization` §11, §13's
/// right to erasure; post-audit plan item 3.1h).
/// </summary>
[Collection("ProcessWideState")]
public class SettingsSyncEraseRenderingTests
{
    private const string Folder = @"E:\Pine";

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
        var window = new Window { Content = view, Width = 900, Height = 1800 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
    }

    private static SettingsViewModel NewSettings(ISender sender, Localizer localizer) =>
        new(sender, localizer, new LanguagePreferenceStore(path: null));

    [Fact]
    public Task TheConfirmation_IsHiddenUntilAsked() => InEnglish(localizer =>
    {
        var sender = Substitute.For<ISender>();
        var vm = NewSettings(sender, localizer);
        var view = new SettingsView { DataContext = vm };
        Show(view);

        VisibleText(view).Should().NotContain(localizer["Settings.SyncErase.Dialog.Title"], "hidden until asked for");
    });

    [Fact]
    public Task OpeningTheConfirmation_NamesTheFolderAndTheRealConsequence_AndErasesNothing() => InEnglish(localizer =>
    {
        var sender = Substitute.For<ISender>();
        var vm = NewSettings(sender, localizer);
        vm.EraseFolderPath = Folder;
        var view = new SettingsView { DataContext = vm };
        Show(view);

        vm.BeginEraseCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        var text = VisibleText(view);
        text.Should().Contain(localizer["Settings.SyncErase.Dialog.Title"]);
        text.Should().Contain(localizer["Settings.SyncErase.Dialog.Explanation"]);
        text.Should().Contain(Folder, "the confirmation names the exact folder about to be erased");
        sender.DidNotReceiveWithAnyArgs().Send(Arg.Any<EraseSyncDataCommand>(), Arg.Any<CancellationToken>());

        var confirm = view.GetLogicalDescendants().OfType<Button>()
            .Single(b => Equals(b.Content, localizer["Settings.SyncErase.Dialog.Confirm"]));
        ReferenceEquals(confirm.Command, vm.ConfirmEraseCommand).Should().BeTrue(
            "the confirm button reaches only the erasure path, never a plain delete with no confirmation");
    });

    [Fact]
    public Task TheEraseButton_IsDisabledUntilAFolderIsTyped() => InEnglish(localizer =>
    {
        var sender = Substitute.For<ISender>();
        var vm = NewSettings(sender, localizer);
        var view = new SettingsView { DataContext = vm };
        Show(view);

        var erase = view.GetLogicalDescendants().OfType<Button>()
            .Single(b => Equals(b.Content, localizer["Settings.SyncErase.Button"]));
        erase.IsEffectivelyEnabled.Should().BeFalse("there is no folder yet");

        vm.EraseFolderPath = Folder;
        Dispatcher.UIThread.RunJobs();
        erase.IsEffectivelyEnabled.Should().BeTrue();
    });

    [Fact]
    public Task ASuccessfulErasure_ReportsItOnScreen() => InEnglish(localizer =>
    {
        // The erasure is run BEFORE the view exists, as SyncRunViewRenderingTests.PageAfter does for a sync run:
        // toggling IsErasing while a TextBox bound to it is already in a live visual tree engages the FluentTheme's
        // default IsEnabled transition, which needs a render clock the headless dispatch of this one assertion does
        // not provide — a test-harness limitation, not something ConfirmEraseAsync itself does wrong.
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<EraseSyncDataCommand>(), Arg.Any<CancellationToken>()).Returns(new SyncEraseReport(2, true));
        var vm = NewSettings(sender, localizer);
        vm.EraseFolderPath = Folder;
        vm.BeginEraseCommand.Execute(null);
        vm.ConfirmEraseCommand.ExecuteAsync(null).GetAwaiter().GetResult();

        var view = new SettingsView { DataContext = vm };
        Show(view);

        var text = VisibleText(view);
        text.Should().Contain(line => line.Contains('2', StringComparison.Ordinal));
        text.Should().NotContain(localizer["Settings.SyncErase.Dialog.Title"], "the confirmation closes once the erasure runs");
    });

    [Fact]
    public Task EveryControlAPersonCanUse_HasAnAccessibleName() => InEnglish(localizer =>
    {
        var sender = Substitute.For<ISender>();
        var vm = NewSettings(sender, localizer);
        vm.EraseFolderPath = Folder;
        var view = new SettingsView { DataContext = vm };
        Show(view);

        var folderBox = view.GetLogicalDescendants().OfType<TextBox>()
            .Single(t => Equals(t.PlaceholderText, localizer["Settings.SyncErase.FolderLabel"]));
        AutomationProperties.GetName(folderBox).Should().NotBeNullOrWhiteSpace();

        var erase = view.GetLogicalDescendants().OfType<Button>()
            .Single(b => Equals(b.Content, localizer["Settings.SyncErase.Button"]));
        AutomationProperties.GetName(erase).Should().NotBeNullOrWhiteSpace();
    });
}
