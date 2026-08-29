using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Pine.Infrastructure.Diagnostics;
using Xunit;

namespace Pine.Tests.Unit.Diagnostics;

/// <summary>
/// Covers the file sink itself: the record shape <c>30-Observability</c> §4.1 mandates, size-based
/// rotation and retention from §4.4, level filtering from §4.2, the never-throws guarantee, and the
/// redacted crash path that replaced <c>crash.log</c> (TD-25).
/// </summary>
/// <remarks>
/// EVERY TEST OWNS A DIRECTORY IT CREATES AND DELETES. Nothing here resolves
/// <see cref="PineLoggingOptions.DefaultDirectory"/>, so no test can write into the developer's
/// <c>%LOCALAPPDATA%\Pine</c> -- which matters more than usual for a sink whose whole job is to
/// leave files behind on disk.
/// </remarks>
public class PineFileLoggerTests
{
    /// <summary>A scratch log directory that deletes itself, so a failing test cannot leak files.</summary>
    private sealed class TempLogDirectory : IDisposable
    {
        public TempLogDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "PineLogTests",
                Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string[] Files()
            => Directory.Exists(Path)
                ? Directory.GetFiles(Path, "*" + PineLoggingOptions.FileSuffix).OrderBy(f => f, StringComparer.Ordinal).ToArray()
                : Array.Empty<string>();

        public JsonElement[] Records()
            => Files()
                .SelectMany(File.ReadAllLines)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => JsonDocument.Parse(line).RootElement.Clone())
                .ToArray();

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

    private static PineLoggingOptions OptionsFor(
        TempLogDirectory directory,
        LogLevel minimumLevel = LogLevel.Trace,
        long maxBytes = PineLoggingOptions.DefaultMaxBytesPerFile,
        int retained = PineLoggingOptions.DefaultRetainedFileCount)
        => new()
        {
            Directory = directory.Path,
            MinimumLevel = minimumLevel,
            MaxBytesPerFile = maxBytes,
            RetainedFileCount = retained,
        };

    // ===== §4.1: the record shape =====

    /// <summary>
    /// Every field §4.1 lists as REQUIRED is present, with the spelling the specification uses --
    /// not the BCL's. This is the assertion that fails if the wire format is ever quietly changed.
    /// </summary>
    [Fact]
    public void Log_ShouldWriteEveryFieldRequiredBy_Section41()
    {
        using var directory = new TempLogDirectory();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 28, 9, 30, 0, TimeSpan.Zero));

        using (var provider = new PineFileLoggerProvider(OptionsFor(directory), clock))
        {
            provider.CreateLogger("Pine.Infrastructure.Ai.Vision.PaddleOcrEngine").Log(
                LogLevel.Warning,
                new EventId(3, "ocr.extracted"),
                new List<KeyValuePair<string, object?>>
                {
                    new("RegionCount", 42),
                    new("{OriginalFormat}", "OCR recognised {RegionCount} regions."),
                },
                exception: null,
                (_, _) => "OCR recognised 42 regions.");
        }

        var record = directory.Records().Should().ContainSingle().Subject;

        record.GetProperty("timestamp").GetString().Should().Be("2026-08-28T09:30:00.0000000+00:00");
        record.GetProperty("level").GetString().Should().Be("warn", "§4.2 spells it warn, not Warning");
        record.GetProperty("event").GetString().Should().Be("ocr.extracted");
        record.GetProperty("version").GetString().Should().NotBeNullOrWhiteSpace();
        record.GetProperty("category").GetString().Should().Be("Pine.Infrastructure.Ai.Vision.PaddleOcrEngine");
        record.GetProperty("message").GetString().Should().Be("OCR recognised 42 regions.");
        record.GetProperty("context").GetProperty("RegionCount").GetInt32().Should().Be(42);

        record.TryGetProperty("context", out var context).Should().BeTrue();
        context.TryGetProperty("{OriginalFormat}", out _).Should().BeFalse(
            "the message template is already carried by the message field");
    }

    /// <summary>
    /// With no <see cref="EventId.Name"/>, §4.1's "stable, dotted, snake_case identifier" is derived
    /// from the category rather than left blank or invented.
    /// </summary>
    [Fact]
    public void Log_ShouldDeriveTheEventId_FromTheCategory_WhenTheCallSiteSuppliedNone()
    {
        using var directory = new TempLogDirectory();

        using (var provider = new PineFileLoggerProvider(OptionsFor(directory)))
        {
            provider.CreateLogger("Pine.Infrastructure.Ai.Vision.PaddleOcrEngine")
                .LogInformation("no event id here");
        }

        directory.Records().Single().GetProperty("event").GetString()
            .Should().Be("pine.infrastructure.ai.vision.paddle_ocr_engine");
    }

    /// <summary>
    /// The exception is written as a structured object with a REDACTED message and REDACTED frames.
    /// TD-25's whole point: an exception dump is a path leak.
    /// </summary>
    [Fact]
    public void Log_ShouldRedactTheExceptionMessage_AndEveryStackFrame()
    {
        using var directory = new TempLogDirectory();

        Exception captured;

        try
        {
            throw new InvalidOperationException(@"Cannot open D:\vaults\family\pine.db for user@example.com");
        }
        catch (InvalidOperationException ex)
        {
            captured = ex;
        }

        using (var provider = new PineFileLoggerProvider(OptionsFor(directory)))
        {
            provider.CreateLogger("Pine.Startup").LogError(captured, "startup failed");
        }

        var exception = directory.Records().Single().GetProperty("exception");

        exception.GetProperty("type").GetString().Should().Be("System.InvalidOperationException");
        exception.GetProperty("message").GetString().Should().NotContain("vaults");
        exception.GetProperty("message").GetString().Should().NotContain("example.com");
        exception.GetProperty("message").GetString().Should().Contain(LogRedactor.PathPlaceholder);
        exception.GetProperty("stack").GetArrayLength().Should().BeGreaterThan(0);

        foreach (var frame in exception.GetProperty("stack").EnumerateArray())
        {
            frame.GetString().Should().NotContain(".cs:line", "a frame's source path must not survive");
        }
    }

    /// <summary>The formatted message is redacted too, not only the exception.</summary>
    [Fact]
    public void Log_ShouldRedactTheFormattedMessage()
    {
        using var directory = new TempLogDirectory();

        using (var provider = new PineFileLoggerProvider(OptionsFor(directory)))
        {
            provider.CreateLogger("Pine.Test").LogInformation(
                "Imported from {Source}", @"D:\statements\august.csv");
        }

        var record = directory.Records().Single();

        record.GetProperty("message").GetString().Should().NotContain("statements");
        record.GetProperty("context").GetProperty("Source").GetString().Should().Be(LogRedactor.PathPlaceholder);
    }

    // ===== §4.2: level filtering =====

    [Fact]
    public void Log_ShouldDropRecords_BelowTheConfiguredMinimumLevel()
    {
        using var directory = new TempLogDirectory();

        using (var provider = new PineFileLoggerProvider(OptionsFor(directory, LogLevel.Warning)))
        {
            var logger = provider.CreateLogger("Pine.Test");

            logger.IsEnabled(LogLevel.Debug).Should().BeFalse();
            logger.LogTrace("trace");
            logger.LogDebug("debug");
            logger.LogInformation("info");
            logger.LogWarning("warning");
            logger.LogError("error");
        }

        directory.Records().Select(r => r.GetProperty("level").GetString())
            .Should().Equal("warn", "error");
    }

    // ===== §4.4: rotation and retention =====

    [Fact]
    public void Log_ShouldRotate_OnceTheFileExceedsTheByteThreshold()
    {
        using var directory = new TempLogDirectory();

        // Retention is deliberately set out of the way: this test is about rotation, and the default
        // ten-file retention would delete the very files it is counting.
        using (var provider = new PineFileLoggerProvider(OptionsFor(directory, maxBytes: 512, retained: 100)))
        {
            var logger = provider.CreateLogger("Pine.Test");

            for (var index = 0; index < 40; index++)
            {
                logger.LogInformation("record padding padding padding padding padding");
            }
        }

        var files = directory.Files();

        files.Length.Should().BeGreaterThan(1, "512 bytes cannot hold forty records");

        foreach (var file in files)
        {
            // Only the record that crossed the threshold may push a file past it, and only when it
            // is the first record in that file.
            new FileInfo(file).Length.Should().BeLessThan(1024);
        }

        directory.Records().Should().HaveCount(40, "rotation must not lose a record");
    }

    [Fact]
    public void Log_ShouldDeleteTheOldestFile_OnceRetentionIsExceeded()
    {
        using var directory = new TempLogDirectory();

        using (var provider = new PineFileLoggerProvider(OptionsFor(directory, maxBytes: 256, retained: 3)))
        {
            var logger = provider.CreateLogger("Pine.Test");

            for (var index = 0; index < 60; index++)
            {
                logger.LogInformation("record padding padding padding padding padding");
            }
        }

        var files = directory.Files();

        files.Should().HaveCount(3, "retention keeps the live file plus two archives");
        files.Select(Path.GetFileName).Should().NotContain(
            name => name!.Contains(".0001.", StringComparison.Ordinal),
            "the OLDEST archive is the one retention deletes");
    }

    // ===== The never-throws guarantee =====

    /// <summary>
    /// The directory cannot be created because a FILE already occupies its path. Every write must be
    /// swallowed: a logging failure is never allowed to become an application failure.
    /// </summary>
    [Fact]
    public void Log_ShouldNeverThrow_WhenTheDirectoryCannotBeCreated()
    {
        using var directory = new TempLogDirectory();

        var blocker = Path.Combine(directory.Path, "blocked");
        File.WriteAllText(blocker, "this is a file, not a directory");

        var options = new PineLoggingOptions { Directory = Path.Combine(blocker, "logs") };

        var act = () =>
        {
            using var provider = new PineFileLoggerProvider(options);
            var logger = provider.CreateLogger("Pine.Test");

            logger.LogError(new InvalidOperationException("boom"), "this cannot be written anywhere");
            logger.LogInformation("nor can this");
        };

        act.Should().NotThrow();
        Directory.Exists(options.Directory).Should().BeFalse();
    }

    // ===== The crash path (TD-25) =====

    [Fact]
    public void WriteCrash_ShouldWriteOneRedactedFatalRecord_WithoutAContainer()
    {
        using var directory = new TempLogDirectory();

        Exception captured;

        try
        {
            throw new InvalidOperationException(@"Vault D:\vaults\family\pine.db is corrupt");
        }
        catch (InvalidOperationException ex)
        {
            captured = ex;
        }

        PineCrashLogger.WriteCrash(captured, OptionsFor(directory));

        var record = directory.Records().Should().ContainSingle().Subject;

        record.GetProperty("level").GetString().Should().Be("fatal");
        record.GetProperty("event").GetString().Should().Be(PineCrashLogger.CrashEventId);
        record.GetProperty("category").GetString().Should().Be(PineCrashLogger.CrashCategory);
        record.GetProperty("exception").GetProperty("message").GetString()
            .Should().NotContain("vaults").And.Contain(LogRedactor.PathPlaceholder);
    }

    /// <summary>
    /// A crash record is written even when the sink is configured to drop everything below
    /// <see cref="LogLevel.None"/>: the crash path deliberately does not consult the minimum level.
    /// </summary>
    [Fact]
    public void WriteCrash_ShouldIgnoreTheConfiguredMinimumLevel()
    {
        using var directory = new TempLogDirectory();

        PineCrashLogger.WriteCrash(
            new InvalidOperationException("boom"),
            OptionsFor(directory, LogLevel.None));

        directory.Records().Should().ContainSingle();
    }

    [Fact]
    public void WriteCrash_ShouldNeverThrow_WhenItCannotWrite()
    {
        using var directory = new TempLogDirectory();

        var blocker = Path.Combine(directory.Path, "blocked");
        File.WriteAllText(blocker, "this is a file, not a directory");

        var act = () => PineCrashLogger.WriteCrash(
            new InvalidOperationException("boom"),
            new PineLoggingOptions { Directory = Path.Combine(blocker, "logs") });

        act.Should().NotThrow();
    }

    // ===== Registration =====

    /// <summary>
    /// <c>AddPineLogging</c> is the single registration all five composition roots call. If it stops
    /// adding a PROVIDER, every head silently goes back to formatting records and dropping them --
    /// which is precisely the shape TD-14 describes, and which no other test would notice.
    /// </summary>
    [Fact]
    public void AddPineLogging_ShouldRegister_ThePineFileLoggerProvider()
    {
        var services = new ServiceCollection();

        services.AddPineLogging(options => options.MinimumLevel = LogLevel.Warning);

        services.Should().ContainSingle(descriptor =>
            !descriptor.IsKeyedService
            && descriptor.ServiceType == typeof(ILoggerProvider)
            && descriptor.ImplementationType == typeof(PineFileLoggerProvider));

        services.Should().ContainSingle(descriptor =>
            !descriptor.IsKeyedService && descriptor.ServiceType == typeof(PineLoggingOptions));
    }

    /// <summary>Calling it twice must not produce two providers racing over one directory.</summary>
    [Fact]
    public void AddPineLogging_ShouldBeIdempotent()
    {
        var services = new ServiceCollection();

        services.AddPineLogging();
        services.AddPineLogging();

        // Enumerable.Count explicitly: ServiceCollection has its own Count PROPERTY, which shadows
        // the LINQ extension and turns the fluent form into a compile error.
        services.Where(descriptor =>
            !descriptor.IsKeyedService
            && descriptor.ServiceType == typeof(ILoggerProvider)
            && descriptor.ImplementationType == typeof(PineFileLoggerProvider))
            .Should().ContainSingle();
    }

    /// <summary>
    /// The shipped default directory is under <c>%LOCALAPPDATA%</c> and OUTSIDE any vault, per §4.4
    /// and the decision recorded on <see cref="PineLoggingOptions"/> that logs are not encrypted at
    /// rest this iteration.
    /// </summary>
    [Fact]
    public void DefaultDirectory_ShouldSitUnderLocalApplicationData()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        if (string.IsNullOrEmpty(localAppData))
        {
            return;
        }

        PineLoggingOptions.DefaultDirectory()
            .Should().Be(Path.Combine(localAppData, "Pine", "logs"));

        new PineLoggingOptions().MaxBytesPerFile.Should().Be(50L * 1024 * 1024, "§4.4 says 50 MB");
        new PineLoggingOptions().RetainedFileCount.Should().Be(10, "§4.4 says 10 files");
    }
}
