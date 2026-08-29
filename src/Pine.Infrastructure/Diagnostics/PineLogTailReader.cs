using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Pine.Core.Diagnostics;

namespace Pine.Infrastructure.Diagnostics;

/// <summary>
/// The read side of the TD-14 file sink: replays the newest records out of the JSONL files
/// <see cref="PineFileLogWriter"/> leaves in <see cref="PineLoggingOptions.Directory"/>, so the
/// Diagnostics panel of <c>30-Observability</c> §8 can show what the application actually logged.
/// </summary>
/// <remarks>
/// <para>
/// TD-15 IS WHY THIS TYPE EXISTS. The Diagnostics panel used to build seven log lines in its own
/// constructor and render them as the application's output. Any implementation of
/// <see cref="ILogTailReader"/> that fabricates, pads or synthesises a record re-creates that bug in
/// a new place, so this reader has exactly one source of truth -- the bytes on disk -- and returns
/// an empty list rather than an explanatory pseudo-record when there is nothing to read.
/// </para>
/// <para>
/// <c>FileShare.ReadWrite</c> IS THE SINGLE MOST IMPORTANT LINE IN THIS FILE. The live file is held
/// open for writing by <see cref="PineFileLogWriter"/> for the entire lifetime of the process, and
/// on Windows a <see cref="FileStream"/> opened with the default <c>FileShare.Read</c> would fail
/// with a sharing violation against it. That failure would land precisely when the reader is most
/// wanted -- the app is running, something has just gone wrong, and the user has opened the
/// diagnostics screen -- while every offline test over a closed file would pass. The writer already
/// opens its handle with <c>FileShare.ReadWrite</c> for this exact reason; the reader has to meet it
/// halfway or the share mode negotiation fails.
/// </para>
/// <para>
/// ORDINAL NAME ORDER IS AGE ORDER, AND NO FILESYSTEM TIMESTAMP IS CONSULTED. §4.4's naming gives
/// the live file <c>&lt;date&gt;.log.jsonl</c> and a rotated one <c>&lt;date&gt;.NNNN.log.jsonl</c>
/// with a zero-padded index. Ordinally, <c>'0'</c> (U+0030) sorts before <c>'l'</c> (U+006C), so
/// <c>2026-08-29.0001.log.jsonl</c> sorts BEFORE <c>2026-08-29.log.jsonl</c> -- which is exactly
/// right, because rotation moves the older content out to the indexed name and leaves the live file
/// holding the newest. Indices then order among themselves because they are zero-padded, and dates
/// order among themselves because they are ISO-8601. So the last name in an ordinal sort is always
/// the newest content, and this reader walks the sorted list BACKWARDS. Retention in the writer
/// leans on the same property; a change to the naming scheme breaks both, which is why both say so.
/// </para>
/// <para>
/// A FORWARD SCAN WITH A BOUNDED WINDOW, NOT A SEEK FROM THE END. Files here can reach §4.4's 50 MB
/// cap, so reading one into a string is not an option. The tempting alternative -- seeking to the
/// end and walking backwards in chunks -- is O(tail) instead of O(file), but it has to split UTF-8
/// on a byte boundary it chose itself, and a multi-byte character straddling a chunk edge corrupts
/// the very line the panel is about to display. Scanning forward through a <see cref="StreamReader"/>
/// and keeping only the last <c>maxEntries</c> lines in a <see cref="Queue{T}"/> is O(file) in time
/// but O(maxEntries) in memory and cannot mis-decode anything, because the decoder never sees a
/// boundary it did not reach naturally. A diagnostics panel reads a few dozen lines on demand, not
/// in a loop; correctness is worth the sequential read.
/// </para>
/// <para>
/// THE WORK IS SYNCHRONOUS AND RETURNS <see cref="Task.FromResult{TResult}(TResult)"/>. The
/// interface is asynchronous because a future reader might not be file-backed, not because this one
/// benefits: the reads are buffered, local and small, and wrapping them in <c>Task.Run</c> would
/// merely rent a thread-pool thread to do blocking IO that the caller is awaiting anyway. Doing the
/// work inline keeps the whole operation on one stack, which also means the never-throws guarantee
/// is enforced by one try/catch rather than by exception plumbing across a continuation.
/// </para>
/// <para>
/// IT NEVER THROWS, PER THE INTERFACE CONTRACT. A deleted directory, a revoked ACL, a file removed
/// by retention between the enumeration and the open -- all of them yield whatever was gathered
/// before the fault, down to an empty list. A diagnostics screen that crashes because diagnostics
/// are unavailable is worse than one that reports nothing, and the sink itself already swallows its
/// own write failures on the same reasoning.
/// </para>
/// </remarks>
public sealed class PineLogTailReader : ILogTailReader
{
    private readonly PineLoggingOptions _options;

    /// <summary>Creates a reader over the same options object the sink writes with.</summary>
    /// <param name="options">
    /// Supplies <see cref="PineLoggingOptions.Directory"/>. The reader shares the options instance
    /// with <see cref="PineFileLoggerProvider"/> rather than taking a bare path, so a head that
    /// redirects its logs cannot end up with a panel reading the default directory instead.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    public PineLogTailReader(PineLoggingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<LogTailEntry>> ReadTailAsync(
        int maxEntries,
        CancellationToken cancellationToken = default)
        => Task.FromResult(ReadTail(maxEntries, cancellationToken));

    /// <summary>Gathers the tail synchronously. The whole body is guarded; nothing escapes.</summary>
    private IReadOnlyList<LogTailEntry> ReadTail(int maxEntries, CancellationToken cancellationToken)
    {
        // Blocks are collected newest FILE first, each block internally chronological, and flattened
        // in reverse at the end. Reversing a single flat list instead would also reverse the records
        // WITHIN each file, which is the subtle way an implementation like this gets the order wrong.
        var blocks = new List<List<LogTailEntry>>();
        var gathered = 0;

        try
        {
            if (maxEntries <= 0 || !Directory.Exists(_options.Directory))
            {
                return Array.Empty<LogTailEntry>();
            }

            var files = Directory.GetFiles(_options.Directory, "*" + PineLoggingOptions.FileSuffix);

            // See the class remarks: ordinal name order is age order, so the newest content is at
            // the END of this array and the walk below runs backwards from it.
            Array.Sort(files, StringComparer.Ordinal);

            for (var index = files.Length - 1; index >= 0 && gathered < maxEntries; index--)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                var block = ReadTailOfFile(files[index], maxEntries - gathered, cancellationToken);

                if (block.Count == 0)
                {
                    continue;
                }

                blocks.Add(block);
                gathered += block.Count;
            }
        }
        catch (Exception)
        {
            // An unreadable directory, a permission fault or a file deleted underneath the walk
            // degrades to "as much of the tail as was already in hand" -- never to an exception
            // escaping into a diagnostics screen. See the interface's remarks.
        }

        var result = new List<LogTailEntry>(gathered);

        for (var index = blocks.Count - 1; index >= 0; index--)
        {
            result.AddRange(blocks[index]);
        }

        return result;
    }

    /// <summary>
    /// Reads at most <paramref name="limit"/> records from the END of one file, oldest first.
    /// </summary>
    /// <remarks>
    /// A file that cannot be opened -- retention deleted it a millisecond ago, or the ACL says no --
    /// contributes nothing and does not stop the walk over the remaining files.
    /// </remarks>
    private static List<LogTailEntry> ReadTailOfFile(
        string path,
        int limit,
        CancellationToken cancellationToken)
    {
        var entries = new List<LogTailEntry>(limit);

        try
        {
            // FileShare.ReadWrite: the live file is open for writing by PineFileLogWriter right now.
            // Any narrower share mode makes this reader fail exactly when it is needed most.
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite);

            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false);

            var window = new Queue<string>(limit);
            string? line;

            while ((line = reader.ReadLine()) is not null)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                // The bound is what makes this O(limit) in memory over an arbitrarily large file.
                if (window.Count == limit)
                {
                    window.Dequeue();
                }

                window.Enqueue(line);
            }

            foreach (var candidate in window)
            {
                if (TryParseRecord(candidate, out var entry))
                {
                    entries.Add(entry);
                }
            }
        }
        catch (Exception)
        {
            // Whatever was parsed before the fault is kept; the caller carries on with older files.
        }

        return entries;
    }

    /// <summary>
    /// Projects one JSONL record onto <see cref="LogTailEntry"/>, or reports that it is unusable.
    /// </summary>
    /// <remarks>
    /// <para>
    /// AN UNPARSEABLE LINE IS DROPPED IN SILENCE. The sink appends and flushes one line at a time,
    /// so a process killed mid-write leaves a legitimately truncated final line; a reader that
    /// surfaced that as an entry reading "malformed record" would be inventing output, which is the
    /// exact habit TD-15 was raised to end. Dropping it means the panel shows one fewer real record
    /// instead of one more fake one.
    /// </para>
    /// <para>
    /// MISSING OPTIONAL FIELDS DEGRADE, THEY DO NOT DISQUALIFY. Only a line that is not valid JSON
    /// (or not a JSON object) is dropped; a record whose <c>category</c> the format later stops
    /// emitting still displays with its message intact. That keeps a format revision from blanking
    /// the panel wholesale.
    /// </para>
    /// </remarks>
    private static bool TryParseRecord(string line, out LogTailEntry entry)
    {
        entry = null!;

        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            entry = new LogTailEntry(
                ReadTimestamp(root),
                ReadString(root, "level"),
                ReadString(root, "category"),
                ReadString(root, "message"));

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Reads §4.1's RFC 3339 <c>timestamp</c>, normalised to UTC as <see cref="LogTailEntry"/> promises.
    /// </summary>
    /// <remarks>
    /// <see cref="CultureInfo.InvariantCulture"/> and <see cref="DateTimeStyles.RoundtripKind"/> are
    /// both deliberate: the writer formats with <c>"O"</c> under the invariant culture, and parsing
    /// under the ambient culture would make the reader's behaviour depend on the user's regional
    /// settings for a value the file states unambiguously. A timestamp that will not parse degrades
    /// to <see langword="default"/> rather than dropping an otherwise readable record.
    /// </remarks>
    private static DateTimeOffset ReadTimestamp(JsonElement root)
    {
        if (root.TryGetProperty("timestamp", out var element)
            && element.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(
                element.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsed))
        {
            return parsed.ToUniversalTime();
        }

        return default;
    }

    /// <summary>Reads a string field, degrading a missing or non-string value to the empty string.</summary>
    private static string ReadString(JsonElement root, string name)
        => root.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString() ?? string.Empty
            : string.Empty;
}
