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

/// <summary>The device pairing page, rendered headlessly (post-audit plan item 3.1e).</summary>
[Collection("ProcessWideState")]
public class SyncPairingViewRenderingTests
{
    private const string Fingerprint = "ABCD-EFGH-JKMN-PQRS";

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
        // SelectableTextBlock is a TextBlock, so the code and the fingerprint are caught by this one query.
        return view.GetLogicalDescendants().OfType<TextBlock>()
            .Where(t => t.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(t.Text))
            .Select(t => t.Text!)
            .ToList();
    }

    private static IMediator MediatorWith(SyncPairingStatus status)
    {
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<GetSyncPairingStatusQuery>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(status));
        return mediator;
    }

    private static void Show(Control view)
    {
        var window = new Window { Content = view, Width = 900, Height = 1400 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        _ = window;
    }

    [Fact]
    public Task AVaultWithNoKey_SaysSo_AndStillOffersBothHalves() => InEnglish(localizer =>
    {
        var mediator = MediatorWith(new SyncPairingStatus(false, string.Empty, "/vault/pine.pairing-offer"));
        using var page = new SyncPairingViewModel(mediator, localizer);
        page.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();

        var view = new SyncPairingView { DataContext = page };
        Show(view);

        var text = VisibleText(view);
        text.Should().Contain(localizer["SyncPairing.Key.None"]);
        text.Should().Contain(localizer["SyncPairing.Offer.Title"]);
        text.Should().Contain(localizer["SyncPairing.Accept.Title"]);
        text.Should().NotContain(localizer["SyncPairing.Key.FingerprintHelp"], "there is no fingerprint to compare yet");
    });

    [Fact]
    public Task AVaultWithAKey_ShowsTheFingerprintAndWhatItIsFor() => InEnglish(localizer =>
    {
        var mediator = MediatorWith(new SyncPairingStatus(true, Fingerprint, "/vault/pine.pairing-offer"));
        using var page = new SyncPairingViewModel(mediator, localizer);
        page.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();

        var view = new SyncPairingView { DataContext = page };
        Show(view);

        var text = VisibleText(view);
        text.Should().Contain(Fingerprint);
        text.Should().Contain(localizer["SyncPairing.Key.FingerprintHelp"]);
        text.Should().NotContain(localizer["SyncPairing.Key.None"]);
    });

    [Fact]
    public Task ACodeAndItsWarning_AppearOnlyOnceOneIsMade() => InEnglish(localizer =>
    {
        var code = SyncPairingCode.Generate();
        var mediator = MediatorWith(new SyncPairingStatus(true, Fingerprint, "/vault/pine.pairing-offer"));
        mediator.Send(Arg.Any<CreateSyncPairingOfferCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new SyncPairingOfferIssued(code, Fingerprint, DateTimeOffset.UtcNow.AddMinutes(10), "/vault/offer")));

        using var page = new SyncPairingViewModel(mediator, localizer);
        page.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();

        var view = new SyncPairingView { DataContext = page };
        Show(view);

        VisibleText(view).Should().NotContain(localizer["SyncPairing.Offer.Warning"]);

        page.CreateOfferCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        var text = VisibleText(view);
        text.Should().Contain(code);
        text.Should().Contain(localizer["SyncPairing.Offer.Warning"],
            "a code on a screen without the sentence that says what it is worth is a trap");
    });

    [Fact]
    public Task EveryControlAPersonCanUse_HasAnAccessibleName() => InEnglish(localizer =>
    {
        var mediator = MediatorWith(new SyncPairingStatus(true, Fingerprint, "/vault/pine.pairing-offer"));
        using var page = new SyncPairingViewModel(mediator, localizer);
        page.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
        page.TypedCode = SyncPairingCode.Generate();

        var view = new SyncPairingView { DataContext = page };
        Show(view);
        Dispatcher.UIThread.RunJobs();

        var named = view.GetLogicalDescendants().OfType<Control>()
            .Where(c => c is Button or TextBox && c.IsEffectivelyVisible)
            .ToList();

        named.Should().HaveCountGreaterThanOrEqualTo(6);
        foreach (var control in named)
        {
            AutomationProperties.GetName(control).Should().NotBeNullOrWhiteSpace(
                $"{control.GetType().Name} is operable and a screen reader must be able to say what it is");
        }
    });

    [Fact]
    public Task ThePairButton_IsDisabledUntilACodeIsTyped() => InEnglish(localizer =>
    {
        var mediator = MediatorWith(new SyncPairingStatus(false, string.Empty, "/vault/pine.pairing-offer"));
        using var page = new SyncPairingViewModel(mediator, localizer);
        page.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();

        var view = new SyncPairingView { DataContext = page };
        Show(view);

        var pair = view.GetLogicalDescendants().OfType<Button>()
            .Single(b => Equals(b.Content, localizer["SyncPairing.Accept.Pair"]));

        pair.IsEffectivelyEnabled.Should().BeFalse();

        page.TypedCode = SyncPairingCode.Generate();
        Dispatcher.UIThread.RunJobs();

        pair.IsEffectivelyEnabled.Should().BeTrue();
    });

    [Fact]
    public Task ARefusal_IsOnScreen_AndPolite() => InEnglish(localizer =>
    {
        var mediator = MediatorWith(new SyncPairingStatus(false, string.Empty, "/vault/pine.pairing-offer"));
        mediator.Send(Arg.Any<AcceptSyncPairingOfferCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<SyncPairingCompleted>(new SyncPairingRefusedException(SyncPairingRefusal.DifferentKey)));

        using var page = new SyncPairingViewModel(mediator, localizer);
        page.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
        page.TypedCode = SyncPairingCode.Generate();

        // The refusal is produced before the view exists: this test is about what the page SHOWS, and driving a
        // command through a control adds a dispatcher round trip that says nothing about the markup.
        page.AcceptOfferCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        page.AcceptMessage.Should().NotBeEmpty();

        var view = new SyncPairingView { DataContext = page };
        Show(view);

        VisibleText(view).Should().Contain(localizer["Failure.SyncPairing.DifferentKey"]);
    });
}
