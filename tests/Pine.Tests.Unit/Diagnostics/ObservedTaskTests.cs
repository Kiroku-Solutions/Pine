using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Pine.Core.Common;
using Pine.Tests.Unit.Localization;
using Pine.Ui.Avalonia.Diagnostics;
using System.Collections.Generic;
using Xunit;

namespace Pine.Tests.Unit.Diagnostics;

/// <summary>
/// The seam that ends silent fire-and-forget in the UI (TD-35).
/// </summary>
/// <remarks>
/// <para>
/// WHAT WAS ACTUALLY WRONG. The UI had twenty-five discarded tasks, nine of them started from a
/// constructor, and nine of those had a callee with no <c>catch</c> of any kind. A discarded
/// <see cref="Task"/> that faults is collected in silence: this process installs no
/// <c>TaskScheduler.UnobservedTaskException</c> handler and sets no
/// <c>&lt;ThrowUnobservedTaskExceptions&gt;</c>, so nothing anywhere observed those failures. The
/// screens simply stayed empty.
/// </para>
/// <para>
/// THE TESTS BELOW ARE ABOUT THE RULE, NOT THE CALL SITES. Whether any particular view model routes
/// through this class is a different proposition, pinned elsewhere; a seam that behaves correctly
/// while nothing calls it is the shape of a fix that is not one.
/// </para>
/// </remarks>
public class ObservedTaskTests
{
    private static readonly UserFacingMessage Fallback = new("AddTransaction.Failure.Save");

    /// <summary>An exception whose own text must never reach a screen — it does not opt in.</summary>
    private sealed class SecretiveException : Exception
    {
        public SecretiveException()
            : base(@"C:\Users\someone\AppData\Local\Pine\vaults\pine.db is locked")
        {
        }
    }

    private static readonly UserFacingMessage Speakable = new("Failure.VaultLocked");

    /// <summary>An exception that DID opt in, so its messages may be shown.</summary>
    private sealed class SpeakableException : Exception, IUserFacingFailure
    {
        public IReadOnlyList<UserFacingMessage> UserFacingMessages => [Speakable];
    }

    [Fact]
    public async Task AFaultedTask_IsReported_AndLogged()
    {
        var logger = Substitute.For<ILogger<ObservedTaskTests>>();

        // Source-generated logging (CA1848, used throughout this repository) asks IsEnabled before
        // it writes. A substitute answers false by default, so without this the Log assertion below
        // would pass or fail for a reason unrelated to the code under test.
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);

        IReadOnlyList<UserFacingMessage>? reported = null;

        ObservedTask.Run(
            Task.FromException(new SecretiveException()),
            logger,
            "unit-test-operation",
            Fallback,
            message => reported = message);

        await WaitForAsync(() => reported is not null);

        reported.Should().ContainSingle().Which.Should().BeSameAs(
            Fallback,
            "an exception that did not opt in to being shown must be replaced by the caller's message (TD-24).");
        Rendered.Text(reported!).Should().NotContain("pine.db", "the store's path must never reach the screen.");

        logger.ReceivedWithAnyArgs(1).Log(
            LogLevel.Error, default, default(object)!, default, default!);
    }

    [Fact]
    public async Task AnOptedInException_KeepsItsOwnSentence()
    {
        IReadOnlyList<UserFacingMessage>? reported = null;

        ObservedTask.Run(
            Task.FromException(new SpeakableException()),
            logger: null,
            "unit-test-operation",
            Fallback,
            message => reported = message);

        await WaitForAsync(() => reported is not null);

        reported.Should().ContainSingle().Which.Should().BeSameAs(Speakable);
    }

    /// <summary>
    /// A cancellation is an outcome somebody asked for, not a failure to tell them about.
    /// </summary>
    /// <remarks>
    /// This is the one policy decision in the class, so it is pinned rather than left to the
    /// reader. Reporting cancellations would put an error banner on every screen a user navigates
    /// away from mid-load, which is the opposite of the improvement TD-35 is after.
    /// </remarks>
    [Fact]
    public async Task ACancelledTask_IsNotReportedAsAFailure()
    {
        var logger = Substitute.For<ILogger<ObservedTaskTests>>();
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);

        var reported = false;

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        ObservedTask.Run(
            Task.FromCanceled(cts.Token),
            logger,
            "unit-test-operation",
            Fallback,
            _ => reported = true);

        await Task.Delay(200);

        reported.Should().BeFalse("a cancellation is not a failure the user needs a sentence about.");

        // WithAnyArgs would ignore the level, so the levels are read off the recorded calls
        // instead. A cancellation IS recorded -- at Debug -- and that is the point: it is kept for
        // whoever is reading the log, without being dressed up as a failure.
        var levels = logger.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(ILogger.Log))
            .Select(call => (LogLevel)call.GetArguments()[0]!)
            .ToArray();

        levels.Should().NotContain(LogLevel.Error);
        levels.Should().Contain(LogLevel.Debug, "a cancellation is recorded, just not reported.");
    }

    /// <summary>
    /// The <c>Func&lt;Task&gt;</c> overload also catches a SYNCHRONOUS throw from the factory.
    /// </summary>
    /// <remarks>
    /// This is not a hypothetical shape. <c>LiveScannerViewModel.CaptureAndNavigateAsync</c> is
    /// declared <c>private Task</c> and is not <c>async</c> — it returns <c>Task.CompletedTask</c> —
    /// so its throw propagated synchronously out of a relay command while the caller held the
    /// camera lock. A plain discard could not have caught that; this overload can.
    /// </remarks>
    [Fact]
    public async Task ASynchronousThrowFromTheFactory_IsCaughtToo()
    {
        IReadOnlyList<UserFacingMessage>? reported = null;

        ObservedTask.Run(
            () => throw new SecretiveException(),
            logger: null,
            "unit-test-operation",
            Fallback,
            message => reported = message);

        await WaitForAsync(() => reported is not null);

        reported.Should().ContainSingle().Which.Should().BeSameAs(Fallback);
    }

    [Fact]
    public async Task ASucceedingTask_ReportsNothing()
    {
        var reported = false;

        ObservedTask.Run(
            Task.CompletedTask,
            logger: null,
            "unit-test-operation",
            Fallback,
            _ => reported = true);

        await Task.Delay(200);

        reported.Should().BeFalse();
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100 && !condition(); attempt++)
        {
            await Task.Delay(20);
        }
    }
}
