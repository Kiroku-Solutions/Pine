using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using Pine.Application.Common;
using Pine.Application.Sync.Commands;
using Pine.Application.Sync.Queries;
using Pine.Core.Sync;
using Xunit;

namespace Pine.Tests.Unit.Application.Sync;

/// <summary>The pairing status query, the two pairing commands and their validators (post-audit plan item 3.1e).</summary>
public class SyncPairingRequestTests
{
    private readonly ISyncPairing _pairing = Substitute.For<ISyncPairing>();

    private static string Code() => SyncPairingCode.Generate();

    [Fact]
    public async Task TheStatusQuery_ReturnsWhatTheVaultHolds()
    {
        var status = new SyncPairingStatus(true, "ABCD-EFGH-JKMN-PQRS", @"C:\vault\pine.pairing-offer");
        _pairing.GetStatusAsync(Arg.Any<CancellationToken>()).Returns(status);

        var result = await new GetSyncPairingStatusQueryHandler(_pairing).Handle(new GetSyncPairingStatusQuery(), CancellationToken.None);

        result.Should().BeSameAs(status);
    }

    [Fact]
    public async Task TheOfferCommand_PassesThePathAndTheLifetimeThrough()
    {
        var issued = new SyncPairingOfferIssued(Code(), "ABCD-EFGH-JKMN-PQRS", DateTimeOffset.UnixEpoch, "path");
        _pairing.CreateOfferAsync("path", TimeSpan.FromMinutes(3), Arg.Any<CancellationToken>()).Returns(issued);

        var result = await new CreateSyncPairingOfferCommandHandler(_pairing)
            .Handle(new CreateSyncPairingOfferCommand("path", TimeSpan.FromMinutes(3)), CancellationToken.None);

        result.Should().BeSameAs(issued);
    }

    [Fact]
    public async Task TheAcceptCommand_PassesThePathAndTheCodeThrough()
    {
        var code = Code();
        var completed = new SyncPairingCompleted("ABCD-EFGH-JKMN-PQRS");
        _pairing.AcceptOfferAsync("path", code, Arg.Any<CancellationToken>()).Returns(completed);

        var result = await new AcceptSyncPairingOfferCommandHandler(_pairing)
            .Handle(new AcceptSyncPairingOfferCommand("path", code), CancellationToken.None);

        result.Should().BeSameAs(completed);
    }

    [Fact]
    public void NeitherPairingCommand_IsTransactional()
    {
        // Both write files, not rows; a vault id is the only row either touches, and it manages its own
        // connection. Stated as a test so that adding a row write here has to change this line too.
        new CreateSyncPairingOfferCommand("p", TimeSpan.FromMinutes(1)).Should().NotBeAssignableTo<ITransactionalRequest>();
        new AcceptSyncPairingOfferCommand("p", Code()).Should().NotBeAssignableTo<ITransactionalRequest>();
    }

    [Fact]
    public async Task AnOffer_WithSomewhereToGoAndAnOrdinaryLife_IsAccepted()
    {
        var errors = await new CreateSyncPairingOfferCommandValidator()
            .ValidateAsync(new CreateSyncPairingOfferCommand("path", SyncPairingLimits.DefaultLifetime));

        errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task AnOffer_WithNowhereToGo_IsRefused(string? path)
    {
        var errors = await new CreateSyncPairingOfferCommandValidator()
            .ValidateAsync(new CreateSyncPairingOfferCommand(path!, SyncPairingLimits.DefaultLifetime));

        errors.Should().ContainSingle().Which.UserFacingMessage.Key.Should().Be("Validation.SyncPairing.OfferPathRequired");
    }

    [Fact]
    public async Task AnOffer_ThatWouldLiveTooLongOrNoTime_IsRefused()
    {
        var validator = new CreateSyncPairingOfferCommandValidator();

        foreach (var lifetime in new[] { TimeSpan.Zero, TimeSpan.FromMinutes(-1), SyncPairingLimits.MaximumLifetime + TimeSpan.FromSeconds(1) })
        {
            var errors = await validator.ValidateAsync(new CreateSyncPairingOfferCommand("path", lifetime));
            errors.Should().ContainSingle().Which.UserFacingMessage.Key.Should().Be("Validation.SyncPairing.LifetimeOutOfRange");
        }

        (await validator.ValidateAsync(new CreateSyncPairingOfferCommand("path", SyncPairingLimits.MaximumLifetime))).Should().BeEmpty();
    }

    [Fact]
    public async Task APairing_WithACodeOfTheRightShape_IsAccepted()
    {
        var code = Code();
        var errors = await new AcceptSyncPairingOfferCommandValidator()
            .ValidateAsync(new AcceptSyncPairingOfferCommand("path", code.ToLowerInvariant().Replace("-", " ", StringComparison.Ordinal)));

        errors.Should().BeEmpty("how a code was typed is not what decides whether it opens an offer");
    }

    [Theory]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("ABCDE-FGHJK-MNPQR-STVWX-YZ01U")]
    public async Task APairing_WithWhatIsNotACode_IsRefusedWithoutReadingAnything(string code)
    {
        var errors = await new AcceptSyncPairingOfferCommandValidator()
            .ValidateAsync(new AcceptSyncPairingOfferCommand("path", code));

        errors.Should().ContainSingle().Which.UserFacingMessage.Key.Should().Be("Validation.SyncPairing.CodeFormat");
    }

    [Fact]
    public async Task APairing_WithNoOfferAndNoCode_ReportsBoth()
    {
        var errors = await new AcceptSyncPairingOfferCommandValidator()
            .ValidateAsync(new AcceptSyncPairingOfferCommand(" ", "nope"));

        errors.Select(e => e.UserFacingMessage.Key).Should().BeEquivalentTo(
            ["Validation.SyncPairing.OfferPathRequired", "Validation.SyncPairing.CodeFormat"]);
    }

    [Fact]
    public async Task NoValidationSentence_CarriesThePathOrTheCode()
    {
        var code = Code();
        var accept = await new AcceptSyncPairingOfferCommandValidator()
            .ValidateAsync(new AcceptSyncPairingOfferCommand(" ", code + "X"));
        var offer = await new CreateSyncPairingOfferCommandValidator()
            .ValidateAsync(new CreateSyncPairingOfferCommand("/very/secret/path", TimeSpan.FromDays(1)));

        foreach (var error in accept.Concat(offer))
        {
            error.UserFacingMessage.Arguments.Should().BeEmpty("a sentence with no placeholders can leak nothing");
            error.UserFacingMessage.Key.Should().StartWith("Validation.SyncPairing.");
        }

        accept.Should().NotContain(e => e.Message.Contains(code, StringComparison.Ordinal));
        offer.Should().NotContain(e => e.Message.Contains("secret", StringComparison.Ordinal));
    }
}
