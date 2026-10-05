using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MediatR;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Pine.Application.Sync.Commands;
using Pine.Application.Sync.Queries;
using Pine.Core.Sync;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.ViewModels;
using Xunit;

namespace Pine.Tests.Unit.ViewModels;

/// <summary>The device pairing page (post-audit plan item 3.1e).</summary>
public class SyncPairingPageTests
{
    private const string Fingerprint = "ABCD-EFGH-JKMN-PQRS";

    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly Localizer _localizer = new(CultureInfo.GetCultureInfo("en-US"));

    private static string Code() => SyncPairingCode.Generate();

    private void Status(bool hasKey, string fingerprint = "", string suggested = "/vault/pine.pairing-offer") =>
        _mediator.Send(Arg.Any<GetSyncPairingStatusQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new SyncPairingStatus(hasKey, fingerprint, suggested)));

    private SyncPairingViewModel Page() => new(_mediator, _localizer);

    [Fact]
    public async Task AVaultWithNoKey_SaysSo_AndOffersAPlaceToWriteAnOffer()
    {
        Status(hasKey: false);
        using var page = Page();

        await page.InitializeAsync(CancellationToken.None);

        page.HasKey.Should().BeFalse();
        page.HasNoKey.Should().BeTrue();
        page.Fingerprint.Should().BeEmpty();
        page.OfferFilePath.Should().Be("/vault/pine.pairing-offer");
        page.ErrorMessage.Should().BeNull();
        page.HasCode.Should().BeFalse();
    }

    [Fact]
    public async Task AVaultWithAKey_ShowsItsFingerprint()
    {
        Status(hasKey: true, Fingerprint);
        using var page = Page();

        await page.InitializeAsync(CancellationToken.None);

        page.HasKey.Should().BeTrue();
        page.Fingerprint.Should().Be(Fingerprint);
    }

    [Fact]
    public async Task APathTheUserEdited_SurvivesAReload()
    {
        Status(hasKey: false);
        using var page = Page();
        await page.InitializeAsync(CancellationToken.None);

        page.OfferFilePath = "/somewhere/else";
        await page.LoadCommand.ExecuteAsync(null);

        page.OfferFilePath.Should().Be("/somewhere/else");
    }

    [Fact]
    public async Task ShowingACode_SendsTheCommand_AndPutsTheCodeAndTheFingerprintOnScreen()
    {
        Status(hasKey: false);
        var code = Code();
        var expires = DateTimeOffset.UtcNow.AddMinutes(10);
        _mediator.Send(Arg.Any<CreateSyncPairingOfferCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new SyncPairingOfferIssued(code, Fingerprint, expires, "/vault/offer")));

        using var page = Page();
        await page.InitializeAsync(CancellationToken.None);
        await page.CreateOfferCommand.ExecuteAsync(null);

        page.Code.Should().Be(code);
        page.HasCode.Should().BeTrue();
        page.Fingerprint.Should().Be(Fingerprint);
        page.HasKey.Should().BeTrue("a vault that has just made an offer has a key");
        page.OfferFilePath.Should().Be("/vault/offer");
        page.OfferExpiresText.Should().NotBeEmpty();
        page.OfferMessage.Should().BeEmpty();

        await _mediator.Received(1).Send(
            Arg.Is<CreateSyncPairingOfferCommand>(c => c.Lifetime == SyncPairingLimits.DefaultLifetime),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AFailedOffer_LeavesNoCodeOnScreen()
    {
        Status(hasKey: true, Fingerprint);
        var code = Code();
        _mediator.Send(Arg.Any<CreateSyncPairingOfferCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new SyncPairingOfferIssued(code, Fingerprint, DateTimeOffset.UtcNow.AddMinutes(1), "/vault/offer")));

        using var page = Page();
        await page.InitializeAsync(CancellationToken.None);
        await page.CreateOfferCommand.ExecuteAsync(null);
        page.HasCode.Should().BeTrue();

        _mediator.Send(Arg.Any<CreateSyncPairingOfferCommand>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("disk full"));
        await page.CreateOfferCommand.ExecuteAsync(null);

        page.Code.Should().BeEmpty("a stale code beside a failure reads as success");
        page.HasCode.Should().BeFalse();
        page.OfferExpiresText.Should().BeEmpty();
        page.OfferMessage.Should().NotBeEmpty().And.NotContain("disk full");
    }

    [Fact]
    public async Task ThePairButton_IsOffUntilACodeOfTheRightShapeIsTyped()
    {
        Status(hasKey: false);
        using var page = Page();
        await page.InitializeAsync(CancellationToken.None);

        page.CanPressAccept.Should().BeFalse();
        page.TypedCode = "ABC";
        page.CanPressAccept.Should().BeFalse();
        page.TypedCode = Code();
        page.CanPressAccept.Should().BeTrue();
    }

    [Fact]
    public async Task PairingSucceeds_ClearsTheTypedCode_AndSaysWhichFingerprintToCompare()
    {
        Status(hasKey: false);
        _mediator.Send(Arg.Any<AcceptSyncPairingOfferCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new SyncPairingCompleted(Fingerprint)));

        using var page = Page();
        await page.InitializeAsync(CancellationToken.None);
        page.AcceptOfferFilePath = "/somewhere/offer";
        var code = Code();
        page.TypedCode = code;

        Status(hasKey: true, Fingerprint);
        await page.AcceptOfferCommand.ExecuteAsync(null);

        page.TypedCode.Should().BeEmpty("a used code is a credential nobody should leave on a screen");
        page.HasPaired.Should().BeTrue();
        page.PairedMessage.Should().Contain(Fingerprint);
        page.AcceptMessage.Should().BeEmpty();
        page.HasKey.Should().BeTrue();
        page.Fingerprint.Should().Be(Fingerprint);

        await _mediator.Received(1).Send(
            Arg.Is<AcceptSyncPairingOfferCommand>(c => c.OfferFilePath == "/somewhere/offer" && c.Code == code),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ARefusedPairing_SaysWhy_WithoutQuotingTheInternals()
    {
        Status(hasKey: false);
        _mediator.Send(Arg.Any<AcceptSyncPairingOfferCommand>(), Arg.Any<CancellationToken>())
            .Throws(new SyncPairingRefusedException(SyncPairingRefusal.Expired));

        using var page = Page();
        await page.InitializeAsync(CancellationToken.None);
        var code = Code();
        page.TypedCode = code;
        await page.AcceptOfferCommand.ExecuteAsync(null);

        page.AcceptMessage.Should().NotBeEmpty().And.NotContain(code);
        page.HasPaired.Should().BeFalse();
        page.TypedCode.Should().Be(code, "a refusal leaves the code so it can be corrected rather than retyped");
    }

    [Fact]
    public async Task APairingIsNotAttempted_WithACodeThatCannotOpenAnything()
    {
        Status(hasKey: false);
        using var page = Page();
        await page.InitializeAsync(CancellationToken.None);
        page.TypedCode = "nope";

        await page.AcceptOfferCommand.ExecuteAsync(null);

        await _mediator.DidNotReceive().Send(Arg.Any<AcceptSyncPairingOfferCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AFailureToReadTheVaultsState_IsReportedByThePolicy()
    {
        _mediator.Send(Arg.Any<GetSyncPairingStatusQuery>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("locked"));

        using var page = Page();
        var policy = page.InitializationFailurePolicy;

        await FluentActions.Awaiting(() => page.InitializeAsync(CancellationToken.None)).Should().ThrowAsync<InvalidOperationException>();
        page.IsLoading.Should().BeFalse("a failed load must not leave the spinner running");

        // The shell's initialiser calls this; the page's own sentence is what it reports, never the exception's.
        policy.Operation.Should().Be("sync-pairing-load");
        policy.Report.Should().NotBeNull();
        policy.Report!([policy.Fallback]);
        page.ErrorMessage.Should().NotBeNullOrEmpty().And.NotContain("locked");
    }
}
