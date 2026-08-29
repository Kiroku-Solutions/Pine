using System;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Pine.Application.Security;
using Xunit;

namespace Pine.Tests.Unit.Security;

/// <summary>
/// Ports the idle-lock portion of the 25-check console harness described in
/// <c>60-Production-Readiness-Audit.md</c> §12.16 into the committed suite, per §12.17's first item.
/// </summary>
/// <remarks>
/// <para>
/// NO <c>Task.Delay</c> ANYWHERE. Both <see cref="VaultSession"/> and <see cref="VaultAutoLockService"/>
/// take their clock from an injected <see cref="TimeProvider"/> -- the same seam
/// <c>VoidTransactionCommandHandler</c> already uses -- so the whole nine-minutes-then-nine-minutes-
/// then-two-minutes sequence from the audit's table runs in microseconds against a
/// <see cref="FakeTimeProvider"/>. <see cref="VaultAutoLockService"/> arms its poll via
/// <c>TimeProvider.CreateTimer</c>, and <see cref="FakeTimeProvider.Advance"/> fires any due timer
/// callbacks synchronously as part of advancing, so no background thread or wait is involved.
/// </para>
/// <para>
/// This reproduces the audit's row verbatim: "unlocked -&gt; idle 9 min (still open, 1 min remaining)
/// -&gt; ReportActivity() -&gt; idle 9 min again (still open, so activity really does reset the
/// countdown) -&gt; idle 2 min more" -&gt; locked, <see cref="VaultLockReason.IdleTimeout"/>, key null.
/// </para>
/// </remarks>
public class VaultAutoLockTests
{
    private static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(15);

    private static (FakeTimeProvider Clock, VaultSession Session, VaultAutoLockService Service) CreateHarness(
        TimeSpan? idleTimeout = null, TimeSpan? pollInterval = null)
    {
        var clock = new FakeTimeProvider();
        var session = new VaultSession(clock);
        var service = new VaultAutoLockService(session, clock, new VaultAutoLockOptions
        {
            IdleTimeout = idleTimeout ?? DefaultIdleTimeout,
            PollInterval = pollInterval ?? DefaultPollInterval,
        });

        return (clock, session, service);
    }

    /// <summary>
    /// The exact sequence transcribed from the audit's table: two nine-minute idle stretches
    /// separated by a reported activity, each individually under the ten-minute timeout, followed by
    /// a further two minutes that pushes the session over it. Asserted step by step so a failure
    /// points at exactly which step diverged from the audit's observed behaviour.
    /// </summary>
    [Fact]
    public void AutoLock_ShouldReproduceAuditSequence_ActivityResetsTheCountdownThenIdleLocks()
    {
        var (clock, session, _) = CreateHarness();

        session.Unlock(dbPath: "C:\\fake\\vault", key: [1, 2, 3, 4]);
        session.IsUnlocked.Should().BeTrue("the session just unlocked");

        clock.Advance(TimeSpan.FromMinutes(9));
        session.IsUnlocked.Should().BeTrue("nine minutes is still under the ten-minute timeout");

        session.ReportActivity();

        clock.Advance(TimeSpan.FromMinutes(9));
        session.IsUnlocked.Should().BeTrue(
            "ReportActivity should have reset the countdown, so this is nine minutes since the reset, not eighteen since unlock");

        VaultLockedEventArgs? capturedArgs = null;
        session.Locked += (_, args) => capturedArgs = args;

        clock.Advance(TimeSpan.FromMinutes(2));

        session.IsUnlocked.Should().BeFalse("eleven minutes have now elapsed since the last reported activity");
        session.Key.Should().BeNull();
        capturedArgs.Should().NotBeNull("the Locked event must fire when the idle lock trips");
        capturedArgs!.Reason.Should().Be(VaultLockReason.IdleTimeout);
    }

    [Fact]
    public void AutoLock_ShouldNotLock_WhileWellWithinTheIdleTimeout()
    {
        var (clock, session, _) = CreateHarness();

        session.Unlock(dbPath: "C:\\fake\\vault", key: [1, 2, 3, 4]);
        clock.Advance(TimeSpan.FromMinutes(5));

        session.IsUnlocked.Should().BeTrue();
    }

    [Fact]
    public void AutoLock_ShouldLock_AssoonAsThePollAfterTheIdleTimeoutRuns()
    {
        var (clock, session, _) = CreateHarness();

        session.Unlock(dbPath: "C:\\fake\\vault", key: [1, 2, 3, 4]);

        // One tick past the timeout, plus the poll interval so a poll actually lands after the
        // deadline -- the lock fires on the next poll, not the instant the deadline is crossed.
        clock.Advance(DefaultIdleTimeout + DefaultPollInterval);

        session.IsUnlocked.Should().BeFalse();
    }

    [Fact]
    public void ReportActivity_ShouldBeANoOp_WhenTheSessionIsAlreadyLocked()
    {
        var (clock, session, _) = CreateHarness();

        // Never unlocked, so there is nothing to keep alive -- ReportActivity must not resurrect a
        // countdown against a session that was never open.
        session.ReportActivity();

        session.LastActivityUtc.Should().BeNull();
        session.IsUnlocked.Should().BeFalse();
    }

    [Fact]
    public void CheckNow_ShouldReturnTrue_AndLockImmediately_WhenCalledAfterTheIdleTimeoutHasElapsed()
    {
        var clock = new FakeTimeProvider();
        var session = new VaultSession(clock);
        // StartAutomatically: false -- this test asserts what a MANUAL CheckNow() call does. With
        // the automatic poll left on, the background timer would already have locked the session by
        // the time CheckNow() runs, and CheckNow() would then correctly report false for a no-op call
        // on an already-locked session, which is not what this test is isolating.
        var service = new VaultAutoLockService(session, clock, new VaultAutoLockOptions
        {
            IdleTimeout = DefaultIdleTimeout,
            StartAutomatically = false,
        });

        session.Unlock(dbPath: "C:\\fake\\vault", key: [9, 9, 9]);
        clock.Advance(DefaultIdleTimeout + TimeSpan.FromSeconds(1));

        var locked = service.CheckNow();

        locked.Should().BeTrue();
        session.IsUnlocked.Should().BeFalse();
    }

    [Fact]
    public void CheckNow_ShouldReturnFalse_WhenTheSessionIsAlreadyLocked()
    {
        var (_, session, service) = CreateHarness();

        session.IsUnlocked.Should().BeFalse();

        service.CheckNow().Should().BeFalse();
    }

    [Fact]
    public void Locked_ShouldNotFire_ForARedundantLockCallOnAnAlreadyLockedSession()
    {
        var (_, session, _) = CreateHarness();
        var raiseCount = 0;
        session.Locked += (_, _) => raiseCount++;

        session.Unlock(dbPath: "C:\\fake\\vault", key: [1]);
        session.Lock(VaultLockReason.UserRequested);
        session.Lock(VaultLockReason.UserRequested);

        raiseCount.Should().Be(1, "a Lock() call on an already-locked session must be a silent no-op");
    }
}
