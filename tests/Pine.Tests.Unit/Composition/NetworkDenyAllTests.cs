using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pine.Ui.Avalonia.Composition;
using Xunit;

namespace Pine.Tests.Unit.Composition;

/// <summary>
/// In-process network-deny-all check for Pine's composition (TD-111): build every head's container,
/// start its hosted services, resolve what can be resolved, and assert the .NET networking stack
/// recorded no activity that Pine's own code initiated.
/// </summary>
/// <remarks>
/// <para>
/// HOW IT WORKS. A process-wide <see cref="EventListener"/> subscribes to every
/// <c>System.Net*</c> / <c>Microsoft-System-Net*</c> EventSource (sockets, DNS, HTTP, TLS). Listener
/// callbacks run synchronously on the thread that raised the event, so the stack of the callback is
/// the stack of the code that caused it. Any HTTP event at all is a violation (the test host does not
/// use HttpClient); a sockets, DNS or TLS event is a violation when a <c>Pine.*</c> (non-test) frame
/// is on that stack. The test host's own socket traffic to the test runner therefore does not
/// trigger it. A POSITIVE CONTROL proves the listener sees a real loopback connection attempt and a
/// real HttpClient call, because a harness that cannot fail proves nothing.
/// </para>
/// <para>
/// WHAT THIS DOES NOT PROVE, PLAINLY. It sees only MANAGED networking. A socket opened by
/// PaddleInference, ONNX Runtime or OpenCV native code, or by a child process, is invisible to
/// EventSource. It runs composition and service start-up, not the receipt-scanning workload, and it
/// does not run the packaged artefact. The real TD-111 remedy (run the packaged application under
/// a firewall or network-namespace deny-all, driving an OCR import) is still a manual step.
/// </para>
/// </remarks>
[Collection("ProcessWideState")]
public sealed class NetworkDenyAllTests
{
    public static TheoryData<PineHeadProfile> AllProfiles() =>
        new(Enum.GetValues<PineHeadProfile>());

    [Theory]
    [MemberData(nameof(AllProfiles))]
    public async Task CompositionAndStartUp_ShouldAttemptNoNetworkConnection(PineHeadProfile profile)
    {
        using var recorder = new NetworkRecorder(IsPineFrame);

        using (var provider = TestContainer.Build(profile))
        {
            PineCompositionRoot.Start(provider);

            ResolveEverythingResolvable(provider);

            foreach (var hosted in provider.GetServices<IHostedService>())
            {
                await hosted.StopAsync(CancellationToken.None);
            }
        }

        // Let any fire-and-forget work the start-up queued actually reach the network stack.
        await Task.Delay(300);

        recorder.Violations.Should().BeEmpty(
            "Pine performs no network I/O at all (ADR-0012); every violation below is a networking event "
            + "raised from Pine code or an HTTP event from anywhere");
    }

    [Fact]
    public async Task TheHarnessItself_ShouldSeeALoopbackSocketAttemptAndAnHttpCall()
    {
        using var recorder = new NetworkRecorder(frame => IsPineFrame(frame) || frame.Contains("Pine.Tests.", StringComparison.Ordinal));

        // Port 1 on loopback: refused immediately, nothing leaves the machine.
        using (var client = new TcpClient())
        {
            var connect = async () => await client.ConnectAsync(IPAddress.Loopback, 1);
            await connect.Should().ThrowAsync<SocketException>();
        }

        using (var http = new HttpClient())
        {
            var call = async () => await http.GetAsync(new Uri("http://127.0.0.1:1/"));
            await call.Should().ThrowAsync<HttpRequestException>();
        }

        recorder.Violations.Should().Contain(v => v.Contains("Sockets", StringComparison.Ordinal),
            "the listener must notice a managed socket connect, or the deny-all test above is blind");
        recorder.Violations.Should().Contain(v => v.Contains("Http", StringComparison.Ordinal),
            "the listener must notice an HttpClient call");
    }

    private static bool IsPineFrame(string stackFrame) =>
        PineProductFrame.IsMatch(stackFrame);

    private static readonly Regex PineProductFrame = new(
        @"^\s*at Pine\.(?!Tests\.)",
        RegexOptions.Multiline | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    private static void ResolveEverythingResolvable(ServiceProvider provider)
    {
        using var scope = provider.CreateScope();

        // Ask for every Pine interface. Failures are expected for services that need an unlocked
        // vault; the point is that constructing whatever CAN be constructed opens no connection.
        var interfaces = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => a.GetName().Name?.StartsWith("Pine.", StringComparison.Ordinal) == true
                && a.GetName().Name != "Pine.Tests.Unit")
            .SelectMany(a => a.GetTypes())
            .Where(t => t.IsInterface && !t.ContainsGenericParameters);

        foreach (var type in interfaces)
        {
            try
            {
                _ = scope.ServiceProvider.GetService(type);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or NotSupportedException)
            {
            }
        }
    }

    private sealed class NetworkRecorder : EventListener
    {
        private readonly Func<string, bool> _isCausedByPine;
        private readonly ConcurrentQueue<EventSource> _pending = new();
        private readonly ConcurrentQueue<string> _violations = new();
        private volatile bool _ready;

        public NetworkRecorder(Func<string, bool> isCausedByPine)
        {
            _isCausedByPine = isCausedByPine;

            // The base constructor delivers already-existing sources BEFORE this constructor body has
            // run, so they are queued there and enabled here.
            while (_pending.TryDequeue(out var source))
            {
                Enable(source);
            }

            _ready = true;
        }

        public IReadOnlyCollection<string> Violations => _violations.ToArray();

        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            if (!IsNetworkSource(eventSource.Name))
            {
                return;
            }

            if (_ready)
            {
                Enable(eventSource);
            }
            else
            {
                _pending.Enqueue(eventSource);
            }
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            if (!IsNetworkSource(eventData.EventSource.Name))
            {
                return;
            }

            var stack = Environment.StackTrace;
            var anyHttp = eventData.EventSource.Name.Contains("Http", StringComparison.OrdinalIgnoreCase);

            if (anyHttp || _isCausedByPine(stack))
            {
                _violations.Enqueue($"{eventData.EventSource.Name}/{eventData.EventName}");
            }
        }

        private static bool IsNetworkSource(string name) =>
            name.StartsWith("System.Net", StringComparison.Ordinal)
            || name.StartsWith("Microsoft-System-Net", StringComparison.Ordinal);

        private void Enable(EventSource source) =>
            EnableEvents(source, EventLevel.Verbose, EventKeywords.All);
    }
}
