using System;

namespace Pine.Core.Diagnostics;

/// <summary>
/// One record read back out of the Pine JSONL log, reduced to the fields a diagnostics panel shows.
/// </summary>
/// <remarks>
/// <para>
/// THIS IS A READ MODEL, NOT THE WIRE FORMAT. <c>30-Observability</c> §4.1's record carries an
/// event id, an application version, a context object and an exception tree as well; none of them
/// belong on a screen that exists to answer "what just happened". Projecting to four fields here
/// means the panel cannot accidentally grow a surface that renders material the file format is free
/// to change.
/// </para>
/// <para>
/// <see cref="Message"/> ARRIVES ALREADY REDACTED. It is redacted once, by <c>LogRedactor</c>, on
/// the way INTO the file -- so anything a reader can retrieve has already crossed that boundary.
/// There is deliberately no second redaction pass on this side: a reader that redacted again would
/// imply the file itself is unsafe, and the whole design of TD-14 is that it is not.
/// </para>
/// </remarks>
/// <param name="Timestamp">When the record was produced, in UTC.</param>
/// <param name="Level">The level, in <c>30-Observability</c> §4.2's vocabulary (<c>info</c>, <c>warn</c>, <c>fatal</c>, ...).</param>
/// <param name="Category">The logger category the record came from.</param>
/// <param name="Message">The already-redacted, already-formatted message.</param>
public sealed record LogTailEntry(
    DateTimeOffset Timestamp,
    string Level,
    string Category,
    string Message);
