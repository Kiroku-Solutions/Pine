using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Pine.Core.Common;

namespace Pine.Ui.Avalonia.Diagnostics;

/// <summary>
/// Starts work that cannot be awaited, and guarantees its failure is seen (TD-35).
/// </summary>
/// <remarks>
/// <para>
/// THE DEFECT THIS REPLACES. Twenty-five call sites in this assembly started a <see cref="Task"/>
/// with a bare discard -- <c>_ = LoadAccountsAsync();</c> -- nine of them from a constructor. A
/// discarded <see cref="Task"/> is not merely un-awaited: it is UNOBSERVED. When it faults, the
/// exception is stored on the task object and nothing ever reads it. There is no
/// <c>TaskScheduler.UnobservedTaskException</c> subscription in this repository, no
/// <c>AppDomain.UnhandledException</c> handler, and no
/// <c>&lt;ThrowUnobservedTaskExceptions&gt;</c> in any csproj, so when the finaliser eventually
/// collects that task the stored exception is dropped on the floor. Nothing is logged, nothing is
/// shown, and the screen simply stays empty -- which the user reads as "I have no accounts", not
/// as "the query failed".
/// </para>
/// <para>
/// A DISCARDED TASK IS WORSE THAN <c>async void</c>, WHICH IS THE OPPOSITE OF THE FOLKLORE. An
/// <c>async void</c> method is genuinely dangerous for a different reason -- it rethrows on
/// whatever synchronisation context started it, which on a UI thread means an unhandled exception
/// that takes the process down -- but that loudness is a form of observability: a crash report
/// exists, a stack trace exists, someone finds out. A discarded faulted <see cref="Task"/> is
/// silent, and silence is the failure mode that survives to production. <c>MainViewModel.Initialize</c>
/// carried a doc comment claiming it used a discard "so exceptions stay observable"; that comment
/// had the guarantee exactly backwards and has been corrected. The honest third option is this
/// class: attach a continuation that awaits the task, catch what comes out, and route it through
/// the seam that already knows how to turn an exception into a sentence and a log record.
/// </para>
/// <para>
/// WHY IT DELEGATES TO <see cref="UserFacingError"/> RATHER THAN FORMATTING ANYTHING. The taxonomy
/// of "which exceptions may show the user their own words" is TD-24's, is closed, and is written
/// once in <see cref="UserFacingError.Report"/>. This class adds exactly one thing to it: the
/// awaiting continuation. It notably does NOT unwrap <see cref="AggregateException"/> itself --
/// <see cref="UserFacingError.Describe"/> already unwraps a single-inner one, which is precisely
/// the shape an async boundary produces, so the sentence a fire-and-forget failure yields is
/// identical to the sentence the same failure yields when it is awaited normally.
/// </para>
/// <para>
/// CANCELLATION IS NOT A FAILURE, AND IS THE ONE THING NOT REPORTED TO THE USER. An
/// <see cref="OperationCanceledException"/> reaching here means somebody asked for the work to
/// stop -- a page was navigated away from mid-load, a token was cancelled during shutdown -- and
/// showing "the page could not be loaded" for a load the user themselves abandoned would be a
/// fabricated failure on a screen they are no longer looking at. It is therefore NOT passed to
/// <paramref name="report"/> and NOT logged at error level. It is still not silent: it is recorded
/// at debug level, so a cancellation storm remains visible in the TD-14 log to anyone looking for
/// one. Note that this treats "cancelled" as a property of the exception type only -- a callee that
/// wraps cancellation in some other exception type is reported as a failure, which is the safe
/// direction to be wrong in.
/// </para>
/// </remarks>
public static partial class ObservedTask
{
    /// <summary>
    /// Awaits <paramref name="task"/> in the background and reports whatever it throws.
    /// </summary>
    /// <param name="task">
    /// The already-started work. Must not be <see langword="null"/>: a null task here is a call
    /// site that thought it started something and did not, which is the same silence this class
    /// exists to remove, so it throws rather than returning quietly.
    /// </param>
    /// <param name="logger">
    /// The originating view model's logger, or <see langword="null"/>. Nullable for the same
    /// reason <see cref="UserFacingError.Report"/>'s is: view models are constructed directly by
    /// the designer and by tests. A null logger loses the record, never the sentence.
    /// </param>
    /// <param name="operation">
    /// A short stable identifier for what was being attempted -- <c>"dashboard-initialise"</c>.
    /// Reaches the log only.
    /// </param>
    /// <param name="fallback">
    /// The message the user reads when the exception is not one that may speak for itself, as a key.
    /// See <see cref="UserFacingError.Describe(Exception, UserFacingMessage)"/>.
    /// </param>
    /// <param name="report">
    /// Where the messages go -- typically a <c>LocalizedMessageSlot</c>'s <c>Set</c> on the view model
    /// that started the work, so that they are re-worded when the language changes (TD-139). <see langword="null"/> means "log it, show nothing", which is
    /// honest only where the screen has nowhere to put a sentence; it is never the reason a failure
    /// disappears, because the log record is written either way.
    /// </param>
    public static void Run(
        Task task,
        ILogger? logger,
        string operation,
        UserFacingMessage fallback,
        Action<IReadOnlyList<UserFacingMessage>>? report = null)
    {
        ArgumentNullException.ThrowIfNull(task);

        // The returned task is itself discarded, and that is safe ONLY because ObserveAsync
        // catches everything including a throw from `report`. It is the single discard in this
        // assembly that is provably incapable of faulting.
        _ = ObserveAsync(task, logger, operation, fallback, report);
    }

    /// <summary>
    /// Starts <paramref name="work"/> and reports whatever it throws, SYNCHRONOUSLY OR NOT.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS OVERLOAD EXISTS BECAUSE "IT RETURNS A TASK" DOES NOT MEAN "IT THROWS INTO A TASK". A
    /// method declared <c>async Task</c> captures its own exceptions into the task it returns, so
    /// <see cref="Run(Task, ILogger?, string, UserFacingMessage, Action{IReadOnlyList{UserFacingMessage}}?)"/> is enough for one. A
    /// method declared <c>Task Foo()</c> that is NOT <c>async</c> does not: everything before its
    /// first <c>return</c> throws straight out at the call site, before there is any task to
    /// observe. <c>LiveScannerViewModel.CaptureAndNavigateAsync</c> is exactly that shape -- a
    /// non-async method returning <c>Task.CompletedTask</c> whose first statement is a PNG encode
    /// -- and it was being invoked as <c>_ = CaptureAndNavigateAsync(frame)</c> from inside a
    /// <c>lock</c>, so an encode failure propagated out of the relay command while holding the
    /// capture lock. Passing a delegate instead of a task moves the invocation inside the
    /// <c>try</c>, which is the only way a helper can cover that case.
    /// </para>
    /// <para>
    /// Prefer this overload whenever the callee is not visibly <c>async</c>, and whenever the call
    /// happens under a lock or in a constructor, where an escaping synchronous throw is worst.
    /// </para>
    /// </remarks>
    /// <param name="work">Produces the task. Invoked immediately, inside the guard.</param>
    /// <param name="logger">See the other overload.</param>
    /// <param name="operation">See the other overload.</param>
    /// <param name="fallback">See the other overload.</param>
    /// <param name="report">See the other overload.</param>
    public static void Run(
        Func<Task> work,
        ILogger? logger,
        string operation,
        UserFacingMessage fallback,
        Action<IReadOnlyList<UserFacingMessage>>? report = null)
    {
        ArgumentNullException.ThrowIfNull(work);

        _ = ObserveAsync(work, logger, operation, fallback, report);
    }

    private static async Task ObserveAsync(
        Func<Task> work,
        ILogger? logger,
        string operation,
        UserFacingMessage fallback,
        Action<IReadOnlyList<UserFacingMessage>>? report)
    {
        Task task;

        try
        {
            task = work();
        }
        catch (Exception ex)
        {
            // A synchronous throw from the factory itself. Same treatment as a faulted task; the
            // user cannot tell the difference and should not have to.
            Surface(ex, logger, operation, fallback, report);

            return;
        }

        if (task is null)
        {
            // A factory that returns null is a programming error, not a runtime condition, but
            // throwing it out of a fire-and-forget continuation would put it right back into the
            // silence this class removes. Report it like any other failure.
            Surface(
                new InvalidOperationException(
                    $"The work factory for '{operation}' returned a null Task, so nothing was started."),
                logger,
                operation,
                fallback,
                report);

            return;
        }

        await ObserveAsync(task, logger, operation, fallback, report).ConfigureAwait(true);
    }

    private static async Task ObserveAsync(
        Task task,
        ILogger? logger,
        string operation,
        UserFacingMessage fallback,
        Action<IReadOnlyList<UserFacingMessage>>? report)
    {
        try
        {
            // ConfigureAwait(true) deliberately: `report` almost always writes an observable
            // property that a control is bound to, and Avalonia requires that on the UI thread.
            // Resuming on the captured context is what makes the report safe by construction
            // rather than by every call site remembering to marshal.
            await task.ConfigureAwait(true);

            return;
        }
        catch (OperationCanceledException cancelled)
        {
            // See the class remarks: a cancellation is an outcome somebody asked for, not a
            // failure to tell them about. Recorded, not reported.
            if (logger is not null)
            {
                LogCancelled(logger, operation, cancelled);
            }

            return;
        }
        catch (Exception ex)
        {
            Surface(ex, logger, operation, fallback, report);
        }
    }

    /// <summary>
    /// Turns one caught exception into a log record and, where there is somewhere to put it, a
    /// sentence on the screen.
    /// </summary>
    /// <remarks>
    /// The <paramref name="report"/> callback is invoked OUTSIDE the catch that produced the
    /// message, and guarded separately. A view model whose error setter throws would otherwise
    /// fault this continuation, and a faulted continuation is the exact silence the class exists
    /// to remove -- so the second failure gets its own record instead of erasing the first.
    /// </remarks>
    private static void Surface(
        Exception exception,
        ILogger? logger,
        string operation,
        UserFacingMessage fallback,
        Action<IReadOnlyList<UserFacingMessage>>? report)
    {
        var message = UserFacingError.Report(logger, exception, operation, fallback);

        if (report is null)
        {
            return;
        }

        try
        {
            report(message);
        }
        catch (Exception reportFailure)
        {
            if (logger is not null)
            {
                LogReportFailed(logger, operation, reportFailure);
            }
        }
    }

    // Source-generated, because CA1848 is an error in this repository.
    [LoggerMessage(
        EventId = 1,
        EventName = "ui.background_work_cancelled",
        Level = LogLevel.Debug,
        Message = "Background work was cancelled before it finished: {Operation}.")]
    private static partial void LogCancelled(ILogger logger, string operation, Exception exception);

    [LoggerMessage(
        EventId = 2,
        EventName = "ui.failure_report_failed",
        Level = LogLevel.Error,
        Message = "Showing the user the failure of '{Operation}' itself failed.")]
    private static partial void LogReportFailed(ILogger logger, string operation, Exception exception);
}
