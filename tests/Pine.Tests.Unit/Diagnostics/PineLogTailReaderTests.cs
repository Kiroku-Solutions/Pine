using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Pine.Core.Diagnostics;
using Pine.Infrastructure.Diagnostics;
using Xunit;

namespace Pine.Tests.Unit.Diagnostics;

/// <summary>
/// Covers the read side of the TD-14 sink (TD-15): the ordinal-name-is-age-order walk over the
/// rotated set, the bounded tail window, chronological output, the never-throws guarantee, and the
/// share-mode trap that would otherwise make the reader fail only while the app is running.
/// </summary>
/// <remarks>
/// <para>
/// EVERY TEST OWNS A DIRECTORY IT CREATES AND DELETES, for the same reason
/// <see cref="PineFileLoggerTests"/> does: nothing here may resolve
/// <see cref="PineLoggingOptions.DefaultDirectory"/> and start reading -- or worse, writing -- in
/// the developer's <c>%LOCALAPPDATA%\Pine\logs</c>.
/// </para>
/// <para>
/// THE ROUND-TRIP TESTS DRIVE THE REAL PRODUCER THROUGH <see cref="PineFileLoggerProvider"/>.
/// <c>PineLogRecordFormatter</c> and <c>PineFileLogWriter</c> are <c>internal</c> and the test
/// assembly has no <c>InternalsVisibleTo</c> into Pine.Infrastructure, so the public provider is
/// used instead. That is the stronger test anyway: it exercises the same path the application does,
/// formatter and writer and rotation included, so the reader cannot drift from the record shape
/// without a failure here. The hand-written fixtures below spell the §4.1 record out literally on
/// purpose -- they are what proves the reader tolerates a file it did not itself produce.
/// </para>
/// </remarks>
public class PineLogTailReaderTests
{
    /// <summary>A scratch log directory that deletes itself, so a failing test cannot leak files.</summary>
    private sealed class TempLogDirectory : IDisposable
    {
        public TempLogDirectory(bool create = true)
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "PineLogTailTests",
                Guid.NewGuid().ToString("N"));

            if (create)
            {
                Directory.CreateDirectory(Path);
            }
        }

        public string Path { get; }

        /// <summary>Writes a whole file verbatim, so a test can plant a truncated final line.</summary>
        public string WriteRaw(string fileName, string contents)
        {
            var path = System.IO.Path.Combine(Path, fileName);

            File.WriteAllText(path, contents, Encoding.UTF8);

            return path;
        }

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

    private static PineLoggingOptions OptionsFor(TempLogDirectory directory)
        => new() { Directory = directory.Path, MinimumLevel = LogLevel.Trace };

    /// <summary>
    /// One §4.1 record, written by hand rather than by the formatter, so these fixtures also assert
    /// that the reader parses the documented wire format and not merely its own output.
    /// </summary>
    private static string Record(DateTimeOffset timestamp, string level, string category, string message)
        => string.Create(
            CultureInfo.InvariantCulture,
            $$"""
              {"timestamp":"{{timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)}}","level":"{{level}}","event":"test.record","version":"1.0.0","category":"{{category}}","message":"{{message}}"}
              """);

    private static readonly DateTimeOffset Origin = new(2026, 8, 29, 10, 0, 0, TimeSpan.Zero);

    /// <summary>Builds <paramref name="count"/> records one minute apart, oldest first.</summary>
    private static string Lines(int firstIndex, int count)
        => string.Concat(
            Enumerable.Range(firstIndex, count).Select(index =>
                Record(Origin.AddMinutes(index), "info", "Pine.Tests", "message-" + index.ToString(CultureInfo.InvariantCulture)) + "\n"));

    private static string LiveFileName(DateTimeOffset day)
        => day.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + PineLoggingOptions.FileSuffix;

    private static string RotatedFileName(DateTimeOffset day, int index)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"{day.UtcDateTime:yyyy-MM-dd}.{index:D4}{PineLoggingOptions.FileSuffix}");

    // ===== The empty states, which must be empty and not padded (TD-15) =====

    /// <summary>
    /// A directory that has never been written to is a REAL, DISPLAYABLE state. The reader returns
    /// nothing for it and does not throw -- padding it with an explanatory pseudo-record is exactly
    /// the fabrication TD-15 exists to remove.
    /// </summary>
    [Fact]
    public async Task ReadTailAsync_ShouldReturnEmpty_WhenTheDirectoryDoesNotExist()
    {
        using var directory = new TempLogDirectory(create: false);
        var reader = new PineLogTailReader(OptionsFor(directory));

        var entries = await reader.ReadTailAsync(50, CancellationToken.None);

        entries.Should().BeEmpty();
    }

    /// <summary>An existing but empty directory behaves the same way.</summary>
    [Fact]
    public async Task ReadTailAsync_ShouldReturnEmpty_WhenTheDirectoryHasNoLogFiles()
    {
        using var directory = new TempLogDirectory();
        var reader = new PineLogTailReader(OptionsFor(directory));

        var entries = await reader.ReadTailAsync(50, CancellationToken.None);

        entries.Should().BeEmpty();
    }

    /// <summary>
    /// A non-positive bound returns nothing without touching the disk, per the interface's
    /// "zero or less returns nothing".
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public async Task ReadTailAsync_ShouldReturnEmpty_WhenMaxEntriesIsNotPositive(int maxEntries)
    {
        using var directory = new TempLogDirectory();
        directory.WriteRaw(LiveFileName(Origin), Lines(0, 5));

        var reader = new PineLogTailReader(OptionsFor(directory));

        var entries = await reader.ReadTailAsync(maxEntries, CancellationToken.None);

        entries.Should().BeEmpty();
    }

    /// <summary>A null options object is a programming error and is refused at construction.</summary>
    [Fact]
    public void Constructor_ShouldReject_NullOptions()
    {
        var construct = () => new PineLogTailReader(null!);

        construct.Should().Throw<ArgumentNullException>();
    }

    // ===== Ordering and the bounded window =====

    /// <summary>
    /// Records come back OLDEST FIRST, so the panel can append them to a terminal view without
    /// reversing -- the interface's stated contract.
    /// </summary>
    [Fact]
    public async Task ReadTailAsync_ShouldReturnRecords_InChronologicalOrder()
    {
        using var directory = new TempLogDirectory();
        directory.WriteRaw(LiveFileName(Origin), Lines(0, 5));

        var reader = new PineLogTailReader(OptionsFor(directory));

        var entries = await reader.ReadTailAsync(50, CancellationToken.None);

        entries.Select(entry => entry.Message).Should().Equal(
            "message-0", "message-1", "message-2", "message-3", "message-4");
        entries.Select(entry => entry.Timestamp).Should().BeInAscendingOrder();
    }

    /// <summary>
    /// The bound keeps the NEWEST records and discards the oldest. Truncating from the wrong end is
    /// the classic tail bug: it compiles, it returns the right count, and it shows the user the
    /// start of the log when they asked what just happened.
    /// </summary>
    [Fact]
    public async Task ReadTailAsync_ShouldKeepTheNewestRecords_WhenMaxEntriesCapsTheResult()
    {
        using var directory = new TempLogDirectory();
        directory.WriteRaw(LiveFileName(Origin), Lines(0, 10));

        var reader = new PineLogTailReader(OptionsFor(directory));

        var entries = await reader.ReadTailAsync(3, CancellationToken.None);

        entries.Select(entry => entry.Message).Should().Equal("message-7", "message-8", "message-9");
    }

    /// <summary>
    /// THE TAIL SPANS ROTATION. When the live file alone cannot satisfy the bound, the reader steps
    /// back into <c>....0001.log.jsonl</c> and the combined result is still oldest-first. This is
    /// the test that fails if the ordinal-order-is-age-order reasoning in the class remarks is ever
    /// inverted: a reader that treated the rotated name as the NEWER file would return the two
    /// halves in the wrong order while still returning the right count.
    /// </summary>
    [Fact]
    public async Task ReadTailAsync_ShouldSpanRotatedAndLiveFiles_InOneChronologicalSequence()
    {
        using var directory = new TempLogDirectory();

        // Rotation moves the OLDER content out to the indexed name and leaves the newest in the
        // live file, so records 0-3 belong in .0001 and records 4-6 in the live file.
        directory.WriteRaw(RotatedFileName(Origin, 1), Lines(0, 4));
        directory.WriteRaw(LiveFileName(Origin), Lines(4, 3));

        var reader = new PineLogTailReader(OptionsFor(directory));

        var entries = await reader.ReadTailAsync(5, CancellationToken.None);

        entries.Select(entry => entry.Message).Should().Equal(
            "message-2", "message-3", "message-4", "message-5", "message-6");
        entries.Select(entry => entry.Timestamp).Should().BeInAscendingOrder();
    }

    /// <summary>Two rotated generations order among themselves by their zero-padded index.</summary>
    [Fact]
    public async Task ReadTailAsync_ShouldOrderRotatedGenerations_ByTheirPaddedIndex()
    {
        using var directory = new TempLogDirectory();

        directory.WriteRaw(RotatedFileName(Origin, 1), Lines(0, 2));
        directory.WriteRaw(RotatedFileName(Origin, 2), Lines(2, 2));
        directory.WriteRaw(LiveFileName(Origin), Lines(4, 2));

        var reader = new PineLogTailReader(OptionsFor(directory));

        var entries = await reader.ReadTailAsync(100, CancellationToken.None);

        entries.Select(entry => entry.Message).Should().Equal(
            "message-0", "message-1", "message-2", "message-3", "message-4", "message-5");
    }

    // ===== Damaged input =====

    /// <summary>
    /// A TRUNCATED FINAL LINE IS DROPPED IN SILENCE, and its neighbours survive. The sink appends
    /// one line at a time, so a process killed mid-write leaves exactly this file; surfacing the
    /// fragment as an entry reading "malformed record" would be the reader inventing output, which
    /// is the habit TD-15 was raised to end.
    /// </summary>
    [Fact]
    public async Task ReadTailAsync_ShouldSkipATruncatedFinalLine_AndKeepTheValidRecords()
    {
        using var directory = new TempLogDirectory();

        var truncated = Record(Origin.AddMinutes(3), "info", "Pine.Tests", "message-3");
        directory.WriteRaw(
            LiveFileName(Origin),
            Lines(0, 3) + truncated[..(truncated.Length / 2)]);

        var reader = new PineLogTailReader(OptionsFor(directory));

        var entries = await reader.ReadTailAsync(50, CancellationToken.None);

        entries.Select(entry => entry.Message).Should().Equal("message-0", "message-1", "message-2");
        entries.Should().NotContain(entry => entry.Message.Contains("malformed", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Garbage in the MIDDLE of a file is skipped without losing the lines around it.</summary>
    [Fact]
    public async Task ReadTailAsync_ShouldSkipNonJsonLines_Anywhere()
    {
        using var directory = new TempLogDirectory();

        directory.WriteRaw(
            LiveFileName(Origin),
            Lines(0, 1) + "this is not json\n" + "[1,2,3]\n" + "\n" + Lines(1, 1));

        var reader = new PineLogTailReader(OptionsFor(directory));

        var entries = await reader.ReadTailAsync(50, CancellationToken.None);

        entries.Select(entry => entry.Message).Should().Equal("message-0", "message-1");
    }

    /// <summary>
    /// A record missing optional fields DEGRADES rather than disqualifying itself: only unparseable
    /// JSON is dropped, so a later format revision cannot blank the panel wholesale.
    /// </summary>
    [Fact]
    public async Task ReadTailAsync_ShouldDegradeMissingFields_RatherThanDroppingTheRecord()
    {
        using var directory = new TempLogDirectory();

        directory.WriteRaw(LiveFileName(Origin), """{"message":"only a message"}""" + "\n");

        var reader = new PineLogTailReader(OptionsFor(directory));

        var entry = (await reader.ReadTailAsync(50, CancellationToken.None)).Should().ContainSingle().Subject;

        entry.Message.Should().Be("only a message");
        entry.Level.Should().BeEmpty();
        entry.Category.Should().BeEmpty();
        entry.Timestamp.Should().Be(default);
    }

    // ===== The share-mode trap =====

    /// <summary>
    /// THE REGRESSION TEST FOR THE SHARE MODE. <see cref="PineFileLogWriter"/> holds the live file
    /// open for writing with <c>FileShare.ReadWrite</c> for the whole life of the process, so a
    /// reader that opened with the default <c>FileShare.Read</c> would fail with a sharing violation
    /// on Windows -- and would fail ONLY while the application is running, which is the one moment
    /// the diagnostics panel is ever used. A real <see cref="FileStream"/> is held open here for the
    /// duration of the read so the sharing negotiation is genuinely exercised, not simulated.
    /// </summary>
    [Fact]
    public async Task ReadTailAsync_ShouldReadTheLiveFile_WhileAnotherStreamHoldsItOpenForWriting()
    {
        using var directory = new TempLogDirectory();
        var path = directory.WriteRaw(LiveFileName(Origin), Lines(0, 3));

        // Exactly the handle PineFileLogWriter.EnsureOpen creates.
        using var held = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);

        var reader = new PineLogTailReader(OptionsFor(directory));

        var entries = await reader.ReadTailAsync(50, CancellationToken.None);

        entries.Select(entry => entry.Message).Should().Equal("message-0", "message-1", "message-2");
    }

    /// <summary>
    /// Records appended and flushed through the open handle are visible to a subsequent read, which
    /// is what makes the panel a live view rather than a snapshot of whatever was on disk at start-up.
    /// </summary>
    [Fact]
    public async Task ReadTailAsync_ShouldSeeRecordsAppended_ThroughAStillOpenHandle()
    {
        using var directory = new TempLogDirectory();
        var path = directory.WriteRaw(LiveFileName(Origin), Lines(0, 2));

        using var held = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        var payload = Encoding.UTF8.GetBytes(Lines(2, 1));
        held.Write(payload, 0, payload.Length);
        held.Flush();

        var reader = new PineLogTailReader(OptionsFor(directory));

        var entries = await reader.ReadTailAsync(50, CancellationToken.None);

        entries.Select(entry => entry.Message).Should().Equal("message-0", "message-1", "message-2");
    }

    // ===== Round trip against the real producer =====

    /// <summary>
    /// THE TEST THAT KEEPS THE READER HONEST. Records are written by the shipping sink -- provider,
    /// formatter and writer, the same path the application takes -- and read straight back. If the
    /// §4.1 record shape is ever changed without updating the reader, this fails even though every
    /// hand-written fixture above still passes.
    /// </summary>
    [Fact]
    public async Task ReadTailAsync_ShouldReadBack_WhatThePineFileSinkWrote()
    {
        using var directory = new TempLogDirectory();
        var clock = new FakeTimeProvider(Origin);
        var options = OptionsFor(directory);

        using (var provider = new PineFileLoggerProvider(options, clock))
        {
            var logger = provider.CreateLogger("Pine.Tests.RoundTrip");

            for (var index = 0; index < 4; index++)
            {
                logger.LogInformation("round trip {Index}", index);
                clock.Advance(TimeSpan.FromMinutes(1));
            }
        }

        var reader = new PineLogTailReader(options);

        var entries = await reader.ReadTailAsync(50, CancellationToken.None);

        entries.Select(entry => entry.Message).Should().Equal(
            "round trip 0", "round trip 1", "round trip 2", "round trip 3");
        entries.Select(entry => entry.Category).Should().AllBe("Pine.Tests.RoundTrip");
        entries[0].Timestamp.Should().Be(Origin);
        entries[3].Timestamp.Should().Be(Origin.AddMinutes(3));
    }

    /// <summary>
    /// Level and category survive the round trip IN §4.2's VOCABULARY, not the BCL's:
    /// <see cref="LogLevel.Warning"/> comes back as <c>warn</c> and <see cref="LogLevel.Critical"/>
    /// as <c>fatal</c>. A reader that re-derived the level from the enum names would pass every
    /// other test in this file and put the wrong word on the screen.
    /// </summary>
    [Fact]
    public async Task ReadTailAsync_ShouldPreserveLevelAndCategory_InTheSection42Vocabulary()
    {
        using var directory = new TempLogDirectory();
        var clock = new FakeTimeProvider(Origin);
        var options = OptionsFor(directory);

        using (var provider = new PineFileLoggerProvider(options, clock))
        {
            provider.CreateLogger("Pine.Tests.Levels").LogWarning("a warning");
            clock.Advance(TimeSpan.FromSeconds(1));
            provider.CreateLogger("Pine.Tests.Other").LogCritical("a crash");
        }

        var reader = new PineLogTailReader(options);

        var entries = await reader.ReadTailAsync(50, CancellationToken.None);

        entries.Should().HaveCount(2);
        entries[0].Level.Should().Be("warn", "§4.2 spells it warn, not Warning");
        entries[0].Category.Should().Be("Pine.Tests.Levels");
        entries[1].Level.Should().Be("fatal", "§4.2 spells it fatal, not Critical");
        entries[1].Category.Should().Be("Pine.Tests.Other");
    }

    // ===== The never-throws guarantee =====

    /// <summary>
    /// An already-cancelled token yields whatever was in hand rather than an
    /// <see cref="OperationCanceledException"/>. The interface's remarks make "must not throw" an
    /// unconditional promise, and a diagnostics screen tearing down its own read is the most likely
    /// way that token is ever cancelled.
    /// </summary>
    [Fact]
    public async Task ReadTailAsync_ShouldNotThrow_WhenTheTokenIsAlreadyCancelled()
    {
        using var directory = new TempLogDirectory();
        directory.WriteRaw(LiveFileName(Origin), Lines(0, 5));

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var reader = new PineLogTailReader(OptionsFor(directory));

        var read = async () => await reader.ReadTailAsync(50, cancellation.Token);

        (await read.Should().NotThrowAsync()).Which.Should().NotBeNull();
    }

    /// <summary>
    /// A directory path that is actually a FILE is the cheap, portable stand-in for the unreadable
    /// directory this reader must survive: enumeration faults, and the contract says the result is
    /// an empty list rather than an exception on a diagnostics screen.
    /// </summary>
    [Fact]
    public async Task ReadTailAsync_ShouldReturnEmpty_WhenTheDirectoryPathIsUnusable()
    {
        using var directory = new TempLogDirectory();
        var filePath = directory.WriteRaw("not-a-directory" + PineLoggingOptions.FileSuffix, Lines(0, 1));

        var reader = new PineLogTailReader(new PineLoggingOptions { Directory = filePath });

        var entries = await reader.ReadTailAsync(50, CancellationToken.None);

        entries.Should().BeEmpty();
    }

    /// <summary>
    /// Reading a 50 MB file must not need 50 MB of memory: the tail window is bounded by
    /// <c>maxEntries</c>, not by the file. This is a behavioural proxy -- a file with far more lines
    /// than the bound still returns exactly the bound, and the newest ones -- because the allocation
    /// itself is not observable from a test.
    /// </summary>
    [Fact]
    public async Task ReadTailAsync_ShouldBoundItsWindow_ByMaxEntriesRatherThanByFileLength()
    {
        using var directory = new TempLogDirectory();
        directory.WriteRaw(LiveFileName(Origin), Lines(0, 5_000));

        var reader = new PineLogTailReader(OptionsFor(directory));

        var entries = await reader.ReadTailAsync(2, CancellationToken.None);

        entries.Select(entry => entry.Message).Should().Equal("message-4998", "message-4999");
    }
}
