using System;
using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Pine.Application.Security;
using Xunit;

namespace Pine.Tests.Unit.Security;

/// <summary>
/// Ports the key-zeroing portion of the 25-check console harness described in
/// <c>60-Production-Readiness-Audit.md</c> §12.16 into the committed suite, per §12.17's first item:
/// "the key is zeroed, not merely dropped ... captured the session's internal <c>_key</c> array by
/// reflection before the lock and re-read it after ... all 32 bytes zero".
/// </summary>
/// <remarks>
/// <para>
/// NO REFLECTION NEEDED HERE, UNLIKE THE HARNESS. The harness reached for reflection because it had
/// no access to <see cref="VaultSession"/>'s private <c>_key</c> field from outside the assembly.
/// <see cref="IVaultSession.Key"/> is documented to return "the session's own buffer, not a copy" (see
/// its remarks), which means the public getter IS the seam the harness had to fake with reflection:
/// capturing the reference returned by <c>Key</c> before calling <c>Lock</c>, then reading that same
/// array afterwards, observes exactly what the harness observed by fishing out the private field --
/// because it is the same array, not a snapshot of it. This is the "cleaner seam" the task brief asks
/// to prefer over replicating the reflection.
/// </para>
/// </remarks>
public class VaultSessionKeyZeroingTests
{
    /// <summary>
    /// Audit table row: "The key is zeroed, not merely dropped ... all 32 bytes zero". Uses a
    /// 32-byte key to match the harness's own byte count, though the property holds at any length.
    /// </summary>
    [Fact]
    public void Lock_ShouldZeroEveryByteOfTheKeyBuffer_NotMerelyDropTheReference()
    {
        var clock = new FakeTimeProvider();
        var session = new VaultSession(clock);
        var key = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();

        session.Unlock(dbPath: "C:\\fake\\vault", key: key);

        // Captured BEFORE the lock, exactly as the harness captured the session's private field
        // before the lock -- the point is to observe the SAME buffer transition across the call, not
        // a fresh read afterwards (which would just be null and prove nothing about zeroing).
        var capturedKeyReference = session.Key;
        capturedKeyReference.Should().NotBeNull();
        capturedKeyReference!.Should().NotContain((byte)0, "a freshly unlocked key should not already be all zeros");

        session.Lock(VaultLockReason.UserRequested);

        capturedKeyReference.Should().OnlyContain(b => b == 0, "Lock() must zero the buffer in place, not merely null out the field");
        capturedKeyReference.Should().HaveCount(32);
    }

    /// <summary>The public seam after a lock: <see cref="IVaultSession.Key"/> itself reads null.</summary>
    [Fact]
    public void Lock_ShouldMakeKeyReadAsNull_AfterTheSessionIsLocked()
    {
        var clock = new FakeTimeProvider();
        var session = new VaultSession(clock);

        session.Unlock(dbPath: "C:\\fake\\vault", key: [1, 2, 3, 4]);
        session.Lock(VaultLockReason.UserRequested);

        session.Key.Should().BeNull();
        session.DbPath.Should().BeNull();
        session.IsUnlocked.Should().BeFalse();
    }

    /// <summary>
    /// The original key array supplied to <see cref="IVaultSession.Unlock"/> is copied, never held
    /// directly -- <c>VaultSession.Unlock</c>'s own remarks say so ("Copied; the caller keeps
    /// ownership of its own array"). Locking the session must therefore NOT zero the caller's
    /// original array, only the session's internal copy.
    /// </summary>
    [Fact]
    public void Lock_ShouldNotZeroTheCallersOriginalKeyArray_BecauseUnlockCopiesIt()
    {
        var clock = new FakeTimeProvider();
        var session = new VaultSession(clock);
        var callerOwnedKey = Enumerable.Range(1, 32).Select(i => (byte)(i + 1)).ToArray();
        var callerOwnedKeySnapshot = (byte[])callerOwnedKey.Clone();

        session.Unlock(dbPath: "C:\\fake\\vault", key: callerOwnedKey);
        session.Lock(VaultLockReason.UserRequested);

        callerOwnedKey.Should().Equal(callerOwnedKeySnapshot, "the session must own a copy, and locking must not reach back into the caller's array");
    }

    [Fact]
    public void Dispose_ShouldAlsoZeroTheKey_ReportingShutdown()
    {
        var clock = new FakeTimeProvider();
        var session = new VaultSession(clock);
        session.Unlock(dbPath: "C:\\fake\\vault", key: [5, 6, 7, 8]);

        var capturedKeyReference = session.Key!;
        VaultLockedEventArgs? capturedArgs = null;
        session.Locked += (_, args) => capturedArgs = args;

        session.Dispose();

        capturedKeyReference.Should().OnlyContain(b => b == 0);
        capturedArgs.Should().NotBeNull();
        capturedArgs!.Reason.Should().Be(VaultLockReason.Shutdown);
    }
}
