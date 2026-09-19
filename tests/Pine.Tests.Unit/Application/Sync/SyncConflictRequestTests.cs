using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MediatR;
using NSubstitute;
using Pine.Application.Sync.Commands;
using Pine.Application.Sync.Queries;
using Pine.Core.Sync;
using Xunit;

namespace Pine.Tests.Unit.Application.Sync;

/// <summary>The conflicts query, the resolve command and its validator (post-audit plan item 3.1c).</summary>
public class SyncConflictRequestTests
{
    private readonly ISyncConflictStore _store = Substitute.For<ISyncConflictStore>();

    private static SyncConflictSummary Conflict(
        SyncConflictBlock block = SyncConflictBlock.None,
        bool grantsConsent = false,
        DateTime? resolvedAt = null,
        SyncConflictKind kind = SyncConflictKind.ConcurrentEdit) =>
        new(Guid.NewGuid(), kind, "Transactions", Guid.NewGuid(), "Grocer", Guid.NewGuid(), Guid.NewGuid(), 7,
            new DateTime(2026, 9, 18, 10, 0, 0, DateTimeKind.Utc), true, false, [], block, grantsConsent, resolvedAt,
            resolvedAt is null ? null : "KeepLocal");

    private ResolveSyncConflictCommandValidator Validator() => new(_store);

    private void Stored(SyncConflictSummary conflict) =>
        _store.FindAsync(conflict.Id, Arg.Any<CancellationToken>()).Returns(conflict);

    [Fact]
    public async Task TheQuery_ReturnsWhatTheStoreHoldsOpen()
    {
        var open = new[] { Conflict() };
        _store.GetOpenAsync(Arg.Any<CancellationToken>()).Returns(open);

        var result = await new GetSyncConflictsQueryHandler(_store).Handle(new GetSyncConflictsQuery(), CancellationToken.None);

        result.Should().BeSameAs(open);
    }

    [Theory]
    [InlineData(SyncConflictResolution.KeepLocal, false)]
    [InlineData(SyncConflictResolution.TakeRemote, true)]
    public async Task TheCommand_PassesTheDecisionAndTheConfirmationThrough(SyncConflictResolution resolution, bool confirm)
    {
        var id = Guid.NewGuid();

        var result = await new ResolveSyncConflictCommandHandler(_store).Handle(new ResolveSyncConflictCommand(id, resolution, confirm), CancellationToken.None);

        result.Should().Be(MediatR.Unit.Value);
        await _store.Received(1).ResolveAsync(id, resolution, confirm, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void TheCommand_IsTransactional()
    {
        new ResolveSyncConflictCommand(Guid.NewGuid(), SyncConflictResolution.KeepLocal)
            .Should().BeAssignableTo<Pine.Application.Common.ITransactionalRequest>("a resolution's writes commit together or not at all");
    }

    [Fact]
    public async Task AnOpenConflict_KeptLocally_IsAccepted()
    {
        var conflict = Conflict(block: SyncConflictBlock.AmbiguousNaturalKey, grantsConsent: true);
        Stored(conflict);

        (await Validator().ValidateAsync(new ResolveSyncConflictCommand(conflict.Id, SyncConflictResolution.KeepLocal)))
            .Should().BeEmpty("keeping this device's side is always safe, whatever blocks the other");
    }

    [Fact]
    public async Task AnOpenConflict_TakenRemotely_IsAcceptedWhenNothingBlocksIt()
    {
        var conflict = Conflict();
        Stored(conflict);

        (await Validator().ValidateAsync(new ResolveSyncConflictCommand(conflict.Id, SyncConflictResolution.TakeRemote))).Should().BeEmpty();
    }

    [Fact]
    public async Task AnUnknownOrEmptyId_IsNotFound()
    {
        (await Validator().ValidateAsync(new ResolveSyncConflictCommand(Guid.NewGuid(), SyncConflictResolution.KeepLocal)))
            .Should().ContainSingle().Which.UserFacingMessage.Key.Should().Be("Validation.SyncConflict.NotFound");

        (await Validator().ValidateAsync(new ResolveSyncConflictCommand(Guid.Empty, SyncConflictResolution.KeepLocal)))
            .Should().ContainSingle().Which.UserFacingMessage.Key.Should().Be("Validation.SyncConflict.NotFound");
        await _store.DidNotReceive().FindAsync(Guid.Empty, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AResolvedConflict_IsRefused_WhicheverWayItIsAsked()
    {
        var conflict = Conflict(resolvedAt: DateTime.UtcNow);
        Stored(conflict);

        foreach (var resolution in new[] { SyncConflictResolution.KeepLocal, SyncConflictResolution.TakeRemote })
        {
            (await Validator().ValidateAsync(new ResolveSyncConflictCommand(conflict.Id, resolution)))
                .Should().ContainSingle().Which.UserFacingMessage.Key.Should().Be("Validation.SyncConflict.AlreadyResolved");
        }
    }

    [Theory]
    [InlineData(SyncConflictBlock.ParentMissing, "Failure.SyncConflict.ParentMissing")]
    [InlineData(SyncConflictBlock.StillPointedAt, "Failure.SyncConflict.StillPointedAt")]
    [InlineData(SyncConflictBlock.AmbiguousNaturalKey, "Failure.SyncConflict.Ambiguous")]
    [InlineData(SyncConflictBlock.AlreadyExistsHere, "Failure.SyncConflict.AlreadyExistsHere")]
    public async Task TakingABlockedSide_IsRefused_WithTheReason(SyncConflictBlock block, string key)
    {
        var conflict = Conflict(block: block);
        Stored(conflict);

        (await Validator().ValidateAsync(new ResolveSyncConflictCommand(conflict.Id, SyncConflictResolution.TakeRemote, ConfirmConsentGrant: true)))
            .Should().ContainSingle().Which.UserFacingMessage.Key.Should().Be(key);
    }

    [Fact]
    public async Task GrantingAConsent_NeedsTheConfirmation()
    {
        var conflict = Conflict(grantsConsent: true, kind: SyncConflictKind.ConsentNotImplicitlyGranted);
        Stored(conflict);

        (await Validator().ValidateAsync(new ResolveSyncConflictCommand(conflict.Id, SyncConflictResolution.TakeRemote)))
            .Should().ContainSingle().Which.UserFacingMessage.Key.Should().Be("Validation.SyncConflict.ConsentNotConfirmed");
        (await Validator().ValidateAsync(new ResolveSyncConflictCommand(conflict.Id, SyncConflictResolution.TakeRemote, ConfirmConsentGrant: true)))
            .Should().BeEmpty();
    }

    [Fact]
    public async Task AnUndefinedResolution_IsRefused_WithoutReadingTheStore()
    {
        (await Validator().ValidateAsync(new ResolveSyncConflictCommand(Guid.NewGuid(), (SyncConflictResolution)42)))
            .Should().ContainSingle().Which.UserFacingMessage.Key.Should().Be("Validation.RequestRejected");
        await _store.DidNotReceiveWithAnyArgs().FindAsync(default, default);
    }

    [Theory]
    [InlineData(SyncConflictRefusal.NotFound, SyncConflictBlock.None, "Failure.SyncConflict.NotFound")]
    [InlineData(SyncConflictRefusal.AlreadyResolved, SyncConflictBlock.None, "Failure.SyncConflict.AlreadyResolved")]
    [InlineData(SyncConflictRefusal.ConsentNotConfirmed, SyncConflictBlock.None, "Failure.SyncConflict.ConsentNotConfirmed")]
    [InlineData(SyncConflictRefusal.TakeRemoteBlocked, SyncConflictBlock.ParentMissing, "Failure.SyncConflict.ParentMissing")]
    public void TheStoresRefusal_CarriesASentenceForTheScreen_AndNoData(SyncConflictRefusal refusal, SyncConflictBlock block, string key)
    {
        var refused = new SyncConflictRefusedException("for the log " + Guid.NewGuid(), refusal, block);

        refused.UserFacingMessages.Should().ContainSingle().Which.Key.Should().Be(key);
        refused.UserFacingMessages[0].Arguments.Should().BeEmpty();
        refused.Refusal.Should().Be(refusal);
        refused.Block.Should().Be(block);
    }

    [Fact]
    public void NothingBlocking_HasNoSentence()
    {
        var act = () => SyncConflictRefusedException.DescribeBlock(SyncConflictBlock.None);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ASummary_IsResolvedOnlyWithAResolutionTime_AndOffersTheRemoteSideOnlyWhenUnblocked()
    {
        Conflict().IsResolved.Should().BeFalse();
        Conflict(resolvedAt: DateTime.UtcNow).IsResolved.Should().BeTrue();
        Conflict().CanTakeRemote.Should().BeTrue();
        Conflict(block: SyncConflictBlock.StillPointedAt).CanTakeRemote.Should().BeFalse();
    }
}
