using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Pine.Core.Diagnostics;
using Pine.Infrastructure.Diagnostics;
using Xunit;

namespace Pine.Tests.Unit.Diagnostics;

/// <summary>
/// Covers the host facts TD-15 replaced with measurements, and -- the reason this file exists at
/// all -- the invariant that the version the panel shows is the version the log file carries.
/// </summary>
/// <remarks>
/// THE ANTI-DIVERGENCE TEST IS THE POINT. Anyone can write
/// <c>AppVersion.Should().NotBeNullOrWhiteSpace()</c>; that passes just as happily against a second,
/// forked copy of the version-resolution logic, which is exactly the failure
/// <c>30-Observability</c> §4.1 and <see cref="IRuntimeInfoProvider.AppVersion"/>'s own remarks warn
/// about. So the test drives the real sink, reads the <c>version</c> field back off disk, and
/// compares it. If the two resolutions are ever split apart, this fails.
/// </remarks>
public class PineRuntimeInfoProviderTests
{
    /// <summary>A scratch log directory that deletes itself, so a failing test cannot leak files.</summary>
    private sealed class TempLogDirectory : IDisposable
    {
        public TempLogDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "PineRuntimeInfoTests",
                Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public JsonElement[] Records()
            => Directory.Exists(Path)
                ? Directory.GetFiles(Path, "*" + PineLoggingOptions.FileSuffix)
                    .OrderBy(file => file, StringComparer.Ordinal)
                    .SelectMany(File.ReadAllLines)
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .Select(line => JsonDocument.Parse(line).RootElement.Clone())
                    .ToArray()
                : Array.Empty<JsonElement>();

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch (IOException)
            {
                // A leaked temp directory is not worth failing a green test over.
            }
        }
    }

    private static readonly PineRuntimeInfoProvider Provider = new();

    /// <summary>
    /// The version is a real string rather than the empty default of a provider that failed to
    /// resolve one. <c>"unknown"</c> is an acceptable value -- it is the formatter's documented
    /// fallback -- but a blank is not.
    /// </summary>
    [Fact]
    public void AppVersion_ShouldNotBeBlank()
    {
        Provider.AppVersion.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// THE INVARIANT: the version the panel reports is byte-for-byte the version the sink stamps
    /// into every record. A panel naming a different build than the log beneath it is worse than
    /// useless during an incident, so the two resolutions must be one resolution.
    /// </summary>
    [Fact]
    public void AppVersion_ShouldEqualTheVersionTheLogSinkWritesIntoEveryRecord()
    {
        using var directory = new TempLogDirectory();

        using (var provider = new PineFileLoggerProvider(new PineLoggingOptions
        {
            Directory = directory.Path,
            MinimumLevel = LogLevel.Trace,
        }))
        {
            provider.CreateLogger("Pine.Tests.Unit.Diagnostics").LogInformation("a record to read back");
        }

        var record = directory.Records().Should().ContainSingle().Subject;

        record.GetProperty("version").GetString().Should().Be(
            Provider.AppVersion,
            "the diagnostics panel and the log file must name the same build (30-Observability §4.1)");
    }

    /// <summary>
    /// The OS description is measured, not blank -- and it comes from <c>RuntimeInformation</c>
    /// rather than the compatibility-shimmed <c>Environment.OSVersion</c>.
    /// </summary>
    [Fact]
    public void OsDescription_ShouldNotBeBlank_AndShouldNameTheArchitecture()
    {
        var description = Provider.OsDescription;

        description.Should().NotBeNullOrWhiteSpace();
        description.Should().Contain(
            System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(),
            "a bug report needs the architecture as well as the OS name");
    }

    /// <summary>
    /// A managed heap reading of zero would mean the provider is not reading the heap at all: a
    /// running .NET process always holds some managed memory.
    /// </summary>
    [Fact]
    public void ManagedMemoryBytes_ShouldBePositive()
    {
        Provider.ManagedMemoryBytes.Should().BePositive();
    }
}
