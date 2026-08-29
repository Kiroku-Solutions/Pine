using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Pine.Infrastructure.Diagnostics;

/// <summary>
/// Turns one log call into one JSONL line with the record shape <c>30-Observability</c> §4.1
/// mandates. Nothing else in the sink knows the wire format.
/// </summary>
/// <remarks>
/// <para>
/// EVERY STRING THAT GOES IN COMES OUT OF <see cref="LogRedactor"/>. That is the whole point of
/// funnelling formatting through one place: there is exactly one method that can write a value into
/// a log file, and it redacts unconditionally rather than trusting each call site to have thought
/// about it. Keys of the context object are NOT redacted -- they are compile-time literals from the
/// message template, never user data -- but every value is.
/// </para>
/// <para>
/// <c>Utf8JsonWriter</c> RATHER THAN <c>JsonSerializer</c>. Serialising a record type by reflection
/// would need a DTO per shape and would silently include any property later added to it; writing
/// the fields by hand means the record shape is the code, and adding a field to the file is a
/// deliberate edit here rather than a side effect elsewhere.
/// </para>
/// <para>
/// <c>trace_id</c> AND <c>span_id</c> FROM §4.1 ARE ABSENT ON PURPOSE. Pine has no tracing
/// infrastructure yet (§6 is unimplemented), and §4.1 makes both fields conditional on a trace
/// being active. Emitting a null or a fabricated id would be worse than omitting a field the
/// specification already marks optional.
/// </para>
/// </remarks>
internal static class PineLogRecordFormatter
{
    /// <summary>The application version stamped into every record's <c>version</c> field.</summary>
    private static readonly string ApplicationVersion = ResolveApplicationVersion();

    /// <summary>
    /// Formats a single record.
    /// </summary>
    /// <param name="timestamp">When the record was produced. Written as RFC 3339, UTC.</param>
    /// <param name="level">The level, mapped to §4.2's vocabulary.</param>
    /// <param name="eventId">The event id; its <c>Name</c> is preferred as the stable event id.</param>
    /// <param name="category">The logger category.</param>
    /// <param name="message">The already-formatted message. Redacted here, not by the caller.</param>
    /// <param name="context">Optional structured state, written as the <c>context</c> object.</param>
    /// <param name="exception">Optional exception; type, redacted message and redacted frames.</param>
    /// <returns>One line of JSON, without a trailing newline.</returns>
    public static string Format(
        DateTimeOffset timestamp,
        LogLevel level,
        EventId eventId,
        string category,
        string? message,
        IReadOnlyList<KeyValuePair<string, object?>>? context,
        Exception? exception)
    {
        using var buffer = new System.IO.MemoryStream(512);

        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();

            // §4.1: timestamp (RFC 3339, UTC). "O" on a UTC DateTimeOffset is RFC 3339 with a "+00:00"
            // offset; ToUniversalTime first so a head in a local time zone still writes UTC.
            writer.WriteString("timestamp", timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            writer.WriteString("level", ToLevelName(level));
            writer.WriteString("event", ResolveEventId(eventId, category));
            writer.WriteString("version", ApplicationVersion);
            writer.WriteString("category", category);
            writer.WriteString("message", LogRedactor.Redact(message) ?? string.Empty);

            WriteContext(writer, context);
            WriteException(writer, exception);

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    /// <summary>
    /// Maps <see cref="LogLevel"/> onto the level vocabulary of <c>30-Observability</c> §4.2.
    /// </summary>
    /// <remarks>
    /// <see cref="LogLevel.Warning"/> becomes <c>warn</c> and <see cref="LogLevel.Critical"/>
    /// becomes <c>fatal</c>: §4.2's table names those two, not the BCL's spellings, and the file is
    /// read against the specification rather than against the enum.
    /// </remarks>
    public static string ToLevelName(LogLevel level) => level switch
    {
        LogLevel.Trace => "trace",
        LogLevel.Debug => "debug",
        LogLevel.Information => "info",
        LogLevel.Warning => "warn",
        LogLevel.Error => "error",
        LogLevel.Critical => "fatal",
        _ => "none",
    };

    /// <summary>
    /// Produces §4.1's "stable, dotted, snake_case identifier": the <see cref="EventId.Name"/> when
    /// the call site supplied one, otherwise an id derived from the logger category.
    /// </summary>
    /// <remarks>
    /// A category-derived fallback is used rather than a literal such as <c>unknown</c> so that
    /// every record is still groupable by origin, and so that adding an explicit event id to an
    /// existing call site is a refinement rather than a correction. Both paths go through the same
    /// normaliser, so an event id written as <c>Ocr.InitFailed</c> at the call site still lands in
    /// the file as <c>ocr.init_failed</c>.
    /// </remarks>
    public static string ResolveEventId(EventId eventId, string category)
        => Normalise(string.IsNullOrEmpty(eventId.Name) ? category : eventId.Name);

    /// <summary>Lower-cases a dotted identifier and snake_cases each of its segments.</summary>
    private static string Normalise(string value)
    {
        var builder = new StringBuilder(value.Length + 8);
        var previousWasLower = false;

        foreach (var candidate in value)
        {
            if (candidate == '.' || candidate == '_' || candidate == '+')
            {
                builder.Append(candidate == '.' ? '.' : '_');
                previousWasLower = false;
                continue;
            }

            if (!char.IsAsciiLetterOrDigit(candidate))
            {
                continue;
            }

            if (char.IsAsciiLetterUpper(candidate) && previousWasLower)
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(candidate));
            previousWasLower = char.IsAsciiLetterLower(candidate) || char.IsAsciiDigit(candidate);
        }

        return builder.Length == 0 ? "unspecified" : builder.ToString();
    }

    /// <summary>
    /// Writes §4.1's optional <c>context</c> object from the structured state of the log call.
    /// </summary>
    /// <remarks>
    /// <c>{OriginalFormat}</c> is dropped: it is the message template, which the formatted
    /// <c>message</c> field already carries, and repeating it doubles the surface a redaction miss
    /// could escape through. The object is omitted entirely when there is nothing to put in it,
    /// because §4.1 makes it optional and an empty object is noise in every line.
    /// </remarks>
    private static void WriteContext(
        Utf8JsonWriter writer,
        IReadOnlyList<KeyValuePair<string, object?>>? context)
    {
        if (context is null || context.Count == 0)
        {
            return;
        }

        var started = false;

        for (var index = 0; index < context.Count; index++)
        {
            var entry = context[index];

            if (string.IsNullOrEmpty(entry.Key) || entry.Key == "{OriginalFormat}")
            {
                continue;
            }

            if (!started)
            {
                writer.WriteStartObject("context");
                started = true;
            }

            WriteContextValue(writer, entry.Key, entry.Value);
        }

        if (started)
        {
            writer.WriteEndObject();
        }
    }

    /// <summary>
    /// Writes one context value, keeping numbers and booleans as JSON scalars and redacting
    /// everything that becomes text.
    /// </summary>
    /// <remarks>
    /// Numeric types are passed through unredacted BY TYPE, not by inspection: a count, a duration
    /// and a confidence score are the diagnostics this sink exists to carry, and running them
    /// through the digit-run rule would erase them. A number that is actually an amount must
    /// therefore never be handed to a logger as a number -- which is why <c>30-Observability</c>
    /// §4.3's amount clause is enforced at the CALL SITES as well as here.
    /// </remarks>
    private static void WriteContextValue(Utf8JsonWriter writer, string key, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNull(key);
                break;
            case bool boolean:
                writer.WriteBoolean(key, boolean);
                break;
            case int or long or short or byte or sbyte or ushort or uint:
                writer.WriteNumber(key, Convert.ToInt64(value, CultureInfo.InvariantCulture));
                break;
            case float or double:
                writer.WriteNumber(key, Convert.ToDouble(value, CultureInfo.InvariantCulture));
                break;
            default:
                writer.WriteString(key, LogRedactor.Redact(Stringify(value)) ?? string.Empty);
                break;
        }
    }

    /// <summary>Converts an arbitrary context value to text without ever letting its ToString throw.</summary>
    private static string Stringify(object value)
    {
        try
        {
            return value is IFormattable formattable
                ? formattable.ToString(format: null, CultureInfo.InvariantCulture)
                : value.ToString() ?? string.Empty;
        }
        catch (Exception)
        {
            return LogRedactor.RedactionFailed;
        }
    }

    /// <summary>
    /// Writes the optional <c>exception</c> object: type name, redacted message, redacted stack
    /// frames, and the same for each inner exception.
    /// </summary>
    /// <remarks>
    /// THE STACK IS AN ARRAY OF REDACTED FRAMES, NOT ONE BLOB. TD-25 was written because
    /// <c>Program.Main</c> dumped <c>ex.ToString()</c> verbatim, and a .NET stack trace embeds the
    /// source path of every frame compiled with a PDB -- which on a developer or side-loaded build
    /// is a path straight through the user's home directory. Splitting the trace into frames and
    /// redacting each one is what makes that mechanical rather than hopeful.
    /// </remarks>
    private static void WriteException(Utf8JsonWriter writer, Exception? exception)
    {
        if (exception is null)
        {
            return;
        }

        writer.WriteStartObject("exception");
        WriteExceptionBody(writer, exception, depth: 0);
        writer.WriteEndObject();
    }

    /// <summary>The inner-exception chain is bounded so a cyclic or pathological chain cannot hang the sink.</summary>
    private const int MaxInnerExceptionDepth = 5;

    private static void WriteExceptionBody(Utf8JsonWriter writer, Exception exception, int depth)
    {
        writer.WriteString("type", exception.GetType().FullName ?? exception.GetType().Name);
        writer.WriteString("message", LogRedactor.Redact(exception.Message) ?? string.Empty);

        writer.WriteStartArray("stack");

        foreach (var frame in EnumerateFrames(exception.StackTrace))
        {
            writer.WriteStringValue(LogRedactor.Redact(frame) ?? string.Empty);
        }

        writer.WriteEndArray();

        if (exception.InnerException is { } inner && depth < MaxInnerExceptionDepth)
        {
            writer.WriteStartObject("inner");
            WriteExceptionBody(writer, inner, depth + 1);
            writer.WriteEndObject();
        }
    }

    private static IEnumerable<string> EnumerateFrames(string? stackTrace)
    {
        if (string.IsNullOrEmpty(stackTrace))
        {
            yield break;
        }

        foreach (var line in stackTrace.Split('\n'))
        {
            var frame = line.Trim('\r', ' ', '\t');

            if (frame.Length > 0)
            {
                yield return frame;
            }
        }
    }

    /// <summary>
    /// Resolves §4.1's <c>version</c> field from the entry assembly's informational version.
    /// </summary>
    /// <remarks>
    /// The entry assembly is preferred because §4.1 asks for "the application version", and Pine
    /// ships five heads out of one infrastructure assembly. Build metadata after <c>+</c> (the
    /// source-control commit the SDK appends by default) is trimmed: it is a repository detail, and
    /// leaving it in would put a different <c>version</c> in every developer's log for the same
    /// release.
    /// </remarks>
    private static string ResolveApplicationVersion()
    {
        try
        {
            var assembly = Assembly.GetEntryAssembly() ?? typeof(PineLogRecordFormatter).Assembly;
            var informational = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;

            var version = string.IsNullOrEmpty(informational)
                ? assembly.GetName().Version?.ToString()
                : informational;

            if (string.IsNullOrEmpty(version))
            {
                return "unknown";
            }

            var plus = version.IndexOf('+', StringComparison.Ordinal);

            return plus > 0 ? version[..plus] : version;
        }
        catch (Exception)
        {
            return "unknown";
        }
    }
}
