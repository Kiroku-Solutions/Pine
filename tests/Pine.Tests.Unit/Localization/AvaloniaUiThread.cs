using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless;

namespace Pine.Tests.Unit.Localization;

/// <summary>
/// Runs an assertion on an Avalonia UI thread that is actually pumping, which is the only place a
/// binding can be observed.
/// </summary>
/// <remarks>
/// <para>
/// THIS SUITE COULD NOT TEST A BINDING BEFORE, AND FAILED AT IT SILENTLY. <c>TestAppInitializer</c>
/// calls <c>SetupWithoutStarting()</c> from a module initializer, which is exactly right for what
/// it was written for -- giving Skia a headless render surface so <c>new Bitmap(stream)</c> works.
/// What it does not do is start a dispatcher loop. Avalonia schedules binding evaluation onto its
/// UI thread, so with nothing pumping, <c>control.Bind(...)</c> SUCCEEDS and the bound property
/// then reads back <see langword="null"/>. No exception, no failure -- the same shape of silence
/// that let a language picker ship for thirty iterations doing nothing.
/// </para>
/// <para>
/// Measured while building TD-45: a plain <c>[Fact]</c> binding a <c>TextBlock</c> to a POCO
/// property, to a <c>DataContext</c>, and to the localizer's indexer returned <c>null</c> in all
/// three cases; <c>Dispatcher.UIThread.RunJobs()</c> threw "the calling thread cannot access this
/// object"; and <c>Dispatcher.UIThread.InvokeAsync</c> from the test thread timed out after 15
/// seconds, because there is no loop to run the work. The design's §13 risk -- "the binding-path
/// parser may not accept dots inside an indexer" -- looked confirmed by every one of those, and was
/// not the cause of any of them.
/// </para>
/// <para>
/// WHY NOT <c>Avalonia.Headless.XUnit</c>, WHICH EXISTS FOR THIS. Its 12.0.5 package depends on
/// **xunit v3**, and this suite is on xunit 2.9.3: adding it produces CS0433 on every
/// <c>[Fact]</c>, <c>[Theory]</c> and <c>[Collection]</c> in 865 tests, because
/// <c>FactAttribute</c> then exists in two assemblies. Verified by adding it and reading the build.
/// <c>HeadlessUnitTestSession</c> is the machinery that package is built on and it lives in
/// <c>Avalonia.Headless</c> itself, so it can be used directly with no new dependency at all.
/// </para>
/// </remarks>
internal static class AvaloniaUiThread
{
    private static readonly Lazy<HeadlessUnitTestSession> Session = new(
        () => HeadlessUnitTestSession.StartNew(typeof(Pine.Ui.Avalonia.App)),
        LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Runs <paramref name="assertion"/> on the UI thread and waits for it.</summary>
    /// <param name="assertion">The body to run. Exceptions propagate to the caller.</param>
    /// <remarks>
    /// One session for the whole assembly, created on first use: starting one costs a real thread
    /// and an application instance, and the tests that need it are few. It is never disposed -- the
    /// process ending is what reclaims it -- because disposing a session another test class may be
    /// about to use is the kind of ordering bug a test suite should not have.
    /// </remarks>
    public static Task On(Action assertion)
    {
        ArgumentNullException.ThrowIfNull(assertion);
        return Session.Value.Dispatch(assertion, CancellationToken.None);
    }
}
