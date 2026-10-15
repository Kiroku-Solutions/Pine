using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Pine.Tests.Unit.Localization;

namespace Pine.Tests.Unit.TestSupport;

/// <summary>What <see cref="UiThreadHarness.Observe"/> measured about one entry point.</summary>
/// <param name="UiThreadId">The managed thread id of the Avalonia UI thread the entry point was called on.</param>
/// <param name="ReturnedAfter">How long the entry point took to hand back its <see cref="Task"/>, i.e. how long it held the UI thread before its first real suspension.</param>
/// <param name="LongestStall">The longest the UI thread went without servicing a queued dispatcher job while the entry point ran to completion.</param>
public readonly record struct UiThreadObservation(int UiThreadId, TimeSpan ReturnedAfter, TimeSpan LongestStall);

/// <summary>
/// A UI-thread harness (TD-93): calls a view-model entry point on a pumping Avalonia headless
/// dispatcher and measures whether it yields.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS EXISTS. TD-92 froze the app on every receipt scan: <c>async</c> methods whose bodies did
/// CPU work inline after an <c>await</c> that completed synchronously. It was shipped, lived with and
/// fixed without one test changing colour, because nothing in the suite ran a command on a UI thread
/// or noticed the thread being held. A blocked UI thread is invisible to a test that awaits the
/// result from a thread-pool thread.
/// </para>
/// <para>
/// WHAT IT MEASURES, AND HOW. The entry point is invoked on the real headless UI thread
/// (<see cref="AvaloniaUiThread"/>). Two things are timed: (1) how long the call takes to return its
/// <see cref="Task"/> - the synchronous prefix, which is exactly the shape of TD-92; (2) a heartbeat
/// posted to <see cref="Dispatcher.UIThread"/> from a thread-pool thread every few milliseconds for
/// the whole duration of the call, whose queueing latency is the time the UI thread could not run a
/// job. The second catches work done inline in a continuation after the first suspension, which the
/// first cannot see.
/// </para>
/// <para>
/// WHAT IT DOES NOT DO. It does not prove that nothing blocks: it proves that whatever the fakes
/// make slow does not run on the UI thread. A view model that blocks on work no fake exercises is
/// not caught. Time-based, so thresholds are generous multiples of the work injected (a stall of
/// the injected length versus a limit a fraction of it) rather than tight absolutes.
/// </para>
/// </remarks>
public static class UiThreadHarness
{
    /// <summary>Calls <paramref name="entryPoint"/> on the UI thread and measures how it yields.</summary>
    public static async Task<UiThreadObservation> Observe(Func<Task> entryPoint)
    {
        ArgumentNullException.ThrowIfNull(entryPoint);

        UiThreadObservation result = default;

        await AvaloniaUiThread.OnAsync(async () =>
        {
            var uiThreadId = Environment.CurrentManagedThreadId;
            var dispatcher = Dispatcher.UIThread;
            using var stop = new CancellationTokenSource();
            long longestTicks = 0;
            var beats = 0;

            // Posted from the pool, so the posting side keeps running while the UI thread is held.
            var heartbeat = Task.Run(async () =>
            {
                while (!stop.IsCancellationRequested)
                {
                    var posted = Stopwatch.GetTimestamp();
                    await dispatcher.InvokeAsync(() =>
                    {
                        var latency = Stopwatch.GetTimestamp() - posted;
                        if (latency > Interlocked.Read(ref longestTicks))
                        {
                            Interlocked.Exchange(ref longestTicks, latency);
                        }

                        Interlocked.Increment(ref beats);
                    });

                    await Task.Delay(5);
                }
            });

            // Do not start the clock until the pump is demonstrably servicing jobs.
            var waiting = Stopwatch.StartNew();
            while (Volatile.Read(ref beats) < 2)
            {
                if (waiting.Elapsed > TimeSpan.FromSeconds(5))
                {
                    stop.Cancel();
                    throw new InvalidOperationException("The UI thread never serviced the harness heartbeat; the dispatcher is not pumping.");
                }

                await Task.Delay(5);
            }

            Interlocked.Exchange(ref longestTicks, 0);

            var started = Stopwatch.GetTimestamp();
            var running = entryPoint();
            var returnedAfter = Stopwatch.GetElapsedTime(started);

            try
            {
                await running;
            }
            finally
            {
                stop.Cancel();
                await heartbeat;
            }

            result = new UiThreadObservation(
                uiThreadId,
                returnedAfter,
                TimeSpan.FromSeconds((double)Interlocked.Read(ref longestTicks) / Stopwatch.Frequency));
        });

        return result;
    }
}
