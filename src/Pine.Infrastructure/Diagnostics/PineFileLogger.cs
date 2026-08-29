using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Pine.Infrastructure.Diagnostics;

/// <summary>
/// The <see cref="ILogger"/> a category gets from <see cref="PineFileLoggerProvider"/>: formats one
/// record through <see cref="PineLogRecordFormatter"/> and hands it to the shared writer.
/// </summary>
/// <remarks>
/// <para>
/// SCOPES ARE ACCEPTED AND IGNORED. <c>BeginScope</c> returns a no-op disposable rather than
/// throwing or returning null, because <c>ILoggerFactory</c> callers -- MediatR among them -- may
/// open a scope and a provider that cannot cope with one is a provider that crashes the app it was
/// added to observe. Scope STATE is deliberately not written to the file: a scope is where an
/// ambient identifier such as a vault path or an account id would ride along unnoticed, and
/// <c>30-Observability</c> §4.3 forbids both. When tracing arrives (§6) the trace and span ids are
/// the fields that belong here, and they are enumerated values, not free text.
/// </para>
/// <para>
/// FILTERING HAPPENS TWICE ON PURPOSE. <see cref="IsEnabled"/> honours the sink's own minimum level
/// as well as whatever the <c>ILoggerFactory</c> filter chain decided, so a head that forgets to set
/// a filter still cannot write <c>trace</c> records -- which §4.2 defines as the level that carries
/// SQL parameter values -- into a plaintext file.
/// </para>
/// </remarks>
internal sealed class PineFileLogger : ILogger
{
    private static readonly NullScope Scope = new();

    private readonly string _category;
    private readonly PineFileLogWriter _writer;
    private readonly PineLoggingOptions _options;
    private readonly TimeProvider _timeProvider;

    public PineFileLogger(
        string category,
        PineFileLogWriter writer,
        PineLoggingOptions options,
        TimeProvider timeProvider)
    {
        _category = category;
        _writer = writer;
        _options = options;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
        => Scope;

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel)
        => logLevel != LogLevel.None && logLevel >= _options.MinimumLevel;

    /// <inheritdoc />
    /// <remarks>
    /// Wrapped whole: <paramref name="formatter"/> is caller-supplied code running on a failure
    /// path, and neither it nor the sink behind it is allowed to take the process down.
    /// </remarks>
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        try
        {
            var message = formatter is null ? state?.ToString() : formatter(state, exception);

            var line = PineLogRecordFormatter.Format(
                _timeProvider.GetUtcNow(),
                logLevel,
                eventId,
                _category,
                message,
                state as IReadOnlyList<KeyValuePair<string, object?>>,
                exception);

            _writer.Write(line);
        }
        catch (Exception)
        {
            // See the remarks: a logging failure is never allowed to become an application failure.
        }
    }

    /// <summary>The disposable returned by <see cref="BeginScope{TState}"/>. Does nothing, by design.</summary>
    private sealed class NullScope : IDisposable
    {
        public void Dispose()
        {
            // Intentionally empty; see the class remarks on why scope state is not recorded.
        }
    }
}
