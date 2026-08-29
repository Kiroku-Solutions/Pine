using System;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Pine.Infrastructure.Diagnostics;

/// <summary>
/// The <see cref="ILoggerProvider"/> that writes Pine's JSONL log files (TD-14). Registered by
/// <see cref="PineLoggingServiceCollectionExtensions.AddPineLogging"/> in all five composition
/// roots, replacing the <c>AddConsole()</c> that went nowhere in a WinExe and the bare
/// <c>AddLogging()</c> that had no provider at all.
/// </summary>
/// <remarks>
/// <para>
/// ONE WRITER, MANY LOGGERS. Every category shares a single <see cref="PineFileLogWriter"/>, so
/// there is one file handle, one rotation decision and one retention sweep no matter how many
/// categories the app logs under. Two writers over one directory would race on rotation and could
/// each delete the other's live file.
/// </para>
/// <para>
/// IT IS PUBLIC SO IT CAN BE TESTED AND SO A HEAD CAN ADD IT DIRECTLY. Everything it composes --
/// the writer, the logger, the record formatter -- is internal, because the wire format and the
/// rotation policy are implementation, whereas "add this provider" is the contract.
/// </para>
/// </remarks>
[ProviderAlias("PineFile")]
public sealed class PineFileLoggerProvider : ILoggerProvider
{
    private readonly PineLoggingOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly PineFileLogWriter _writer;
    private readonly ConcurrentDictionary<string, PineFileLogger> _loggers = new(StringComparer.Ordinal);

    /// <summary>Creates the provider over the given options and the system clock.</summary>
    /// <param name="options">Directory, minimum level, rotation size and retention count.</param>
    public PineFileLoggerProvider(PineLoggingOptions options)
        : this(options, TimeProvider.System)
    {
    }

    /// <summary>Creates the provider over the given options and an explicit clock.</summary>
    /// <param name="options">Directory, minimum level, rotation size and retention count.</param>
    /// <param name="timeProvider">
    /// The clock stamped into every record. Injectable for the same reason
    /// <c>VaultAutoLockService</c> takes one: a test asserting the <c>timestamp</c> field should not
    /// have to reason about the wall clock.
    /// </param>
    public PineFileLoggerProvider(PineLoggingOptions options, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _options = options;
        _timeProvider = timeProvider;
        _writer = new PineFileLogWriter(options);
    }

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName)
        => _loggers.GetOrAdd(
            categoryName ?? string.Empty,
            name => new PineFileLogger(name, _writer, _options, _timeProvider));

    /// <inheritdoc />
    public void Dispose()
    {
        _loggers.Clear();
        _writer.Dispose();
    }
}
