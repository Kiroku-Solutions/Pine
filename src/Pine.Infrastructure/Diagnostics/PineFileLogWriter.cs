using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Pine.Infrastructure.Diagnostics;

/// <summary>
/// The single file handle behind the Pine log sink: appends JSONL lines, rotates by size, and
/// enforces retention. One instance is shared by every logger the provider hands out.
/// </summary>
/// <remarks>
/// <para>
/// A LOGGING FAILURE MUST NEVER CRASH THE APP. Every public entry point here is wrapped, and a
/// failed write is swallowed after ONE best-effort attempt: the handle is dropped so the next
/// record reopens it, but the record that failed is not retried and no exception escapes. The
/// alternative -- letting an <see cref="IOException"/> from a full disk propagate out of an
/// <c>ILogger.Log</c> call inside a catch block -- would turn every recoverable anomaly into a
/// crash, which is precisely the failure class TD-25 was raised over.
/// </para>
/// <para>
/// THREAD SAFETY IS ONE LOCK, DELIBERATELY. The alternative designs -- a background queue, a
/// channel, a dedicated writer thread -- all buy throughput at the cost of losing the tail of the
/// log at exactly the moment it matters most, which is the crash. A lock plus a flush means that
/// when the process dies, everything the process believed it had logged is on disk. Log volume in a
/// local-first desktop app is a few records per user action; this is not the hot path.
/// </para>
/// <para>
/// NAMING FOLLOWS <c>30-Observability</c> §4.4: the live file is <c>&lt;date&gt;.log.jsonl</c>, and
/// a rotated one gains a zero-padded index before the suffix. The index is padded so that ordinal
/// name order IS age order -- retention then needs no filesystem timestamps, which are the one
/// thing about a log directory a user can trivially perturb.
/// </para>
/// </remarks>
internal sealed class PineFileLogWriter : IDisposable
{
    private readonly PineLoggingOptions _options;
    private readonly object _gate = new();

    private FileStream? _stream;
    private string? _currentPath;
    private long _bytesWritten;
    private bool _disposed;

    /// <summary>Creates a writer over the given options. Opens nothing until the first record.</summary>
    /// <param name="options">Directory, rotation size and retention count.</param>
    public PineFileLogWriter(PineLoggingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options;
    }

    /// <summary>Appends one already-formatted JSON line. Never throws.</summary>
    /// <param name="line">The record, without a trailing newline.</param>
    public void Write(string line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return;
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                var payload = Encoding.UTF8.GetBytes(line + "\n");

                EnsureOpen();

                if (_stream is null)
                {
                    return;
                }

                // Rotate BEFORE writing, and never on an empty file: a single record larger than the
                // whole budget must still be written somewhere rather than triggering an endless
                // rotate-and-retry.
                if (_bytesWritten > 0 && _bytesWritten + payload.Length > _options.MaxBytesPerFile)
                {
                    Rotate();
                    EnsureOpen();

                    if (_stream is null)
                    {
                        return;
                    }
                }

                _stream.Write(payload, 0, payload.Length);
                _stream.Flush();
                _bytesWritten += payload.Length;
            }
            catch (Exception)
            {
                // One best-effort attempt, then silence. Dropping the handle means a transient
                // failure (a locked file, a directory deleted underneath us) heals on the next
                // record instead of poisoning the sink for the life of the process.
                DropHandle();
            }
        }
    }

    /// <summary>Closes the file. Safe to call more than once.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            DropHandle();
        }
    }

    /// <summary>Opens today's file if no handle is currently held.</summary>
    private void EnsureOpen()
    {
        var path = Path.Combine(_options.Directory, CurrentFileName());

        if (_stream is not null && string.Equals(_currentPath, path, StringComparison.Ordinal))
        {
            return;
        }

        // The date rolled over while the handle was open: close it so the new day gets its own file.
        DropHandle();

        Directory.CreateDirectory(_options.Directory);

        // FileShare.ReadWrite so a support engineer -- or the Diagnostics panel of §8 -- can read
        // the live file while the app is still writing to it.
        _stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        _currentPath = path;
        _bytesWritten = _stream.Length;

        ApplyRetention();
    }

    private void DropHandle()
    {
        try
        {
            _stream?.Dispose();
        }
        catch (Exception)
        {
            // Nothing useful is left to do with a stream that will not close.
        }

        _stream = null;
        _currentPath = null;
        _bytesWritten = 0;
    }

    /// <summary>Closes the live file, renames it to the next free index, and applies retention.</summary>
    private void Rotate()
    {
        var livePath = _currentPath;

        DropHandle();

        if (livePath is null || !File.Exists(livePath))
        {
            return;
        }

        var baseName = CurrentFileName()[..^PineLoggingOptions.FileSuffix.Length];

        for (var index = 1; index <= 9999; index++)
        {
            var archive = Path.Combine(
                _options.Directory,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{baseName}.{index:D4}{PineLoggingOptions.FileSuffix}"));

            if (File.Exists(archive))
            {
                continue;
            }

            File.Move(livePath, archive);
            break;
        }

        ApplyRetention();
    }

    /// <summary>
    /// Deletes the oldest files until at most <see cref="PineLoggingOptions.RetainedFileCount"/>
    /// remain, the live file included.
    /// </summary>
    /// <remarks>
    /// Ordinal name order is age order (see the class remarks), so this needs no <c>FileInfo</c>
    /// stat call per file and cannot be confused by a clock change or a file copy. A deletion that
    /// fails -- another process has the file open -- is skipped rather than aborting the sweep.
    /// </remarks>
    private void ApplyRetention()
    {
        var retained = _options.RetainedFileCount;

        if (retained <= 0)
        {
            return;
        }

        var files = new List<string>(Directory.GetFiles(_options.Directory, "*" + PineLoggingOptions.FileSuffix));

        if (files.Count <= retained)
        {
            return;
        }

        files.Sort(StringComparer.Ordinal);

        for (var index = 0; index < files.Count - retained; index++)
        {
            if (string.Equals(files[index], _currentPath, StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                File.Delete(files[index]);
            }
            catch (Exception)
            {
                // A file another process is holding open stays; the next sweep will try again.
            }
        }
    }

    /// <summary>The live file's name: <c>&lt;date&gt;.log.jsonl</c>, per §4.4.</summary>
    private static string CurrentFileName()
        => string.Create(
            CultureInfo.InvariantCulture,
            $"{DateTime.UtcNow:yyyy-MM-dd}{PineLoggingOptions.FileSuffix}");
}
