using System;
using Microsoft.Extensions.Logging;

namespace Pine.Infrastructure.Diagnostics;

/// <summary>
/// The redacted crash path (TD-25). Writes one <c>fatal</c> JSONL record for an exception that is
/// about to end the process, with no container, no <see cref="ILoggerFactory"/> and no prior
/// initialisation of any kind.
/// </summary>
/// <remarks>
/// <para>
/// WHY A STATIC ENTRY POINT AND NOT AN <c>ILogger</c>. This is called from the catch block around
/// the whole of <c>Program.Main</c>, where the most likely thing to have thrown is the construction
/// of the service provider itself. A crash handler that needs the container to be working cannot
/// record the failure of the container, which is the single most valuable record a desktop app can
/// produce. So this takes nothing and resolves nothing.
/// </para>
/// <para>
/// WHAT IT REPLACES. Both desktop heads wrote <c>File.WriteAllText(crash.log, $"{DateTime.UtcNow}\n{ex}")</c>
/// -- the entire exception, every inner exception and every stack frame, verbatim, into
/// <c>%LOCALAPPDATA%/Pine/crash.log</c>. A .NET stack trace from a build with PDBs carries the
/// source path of each frame, which on a developer or side-loaded build runs straight through the
/// user's profile directory; the message itself routinely carries the vault path. Every one of
/// those strings now goes through <see cref="LogRedactor"/>, and the record lands in the same JSONL
/// stream as everything else rather than in a bespoke file with a bespoke format that no reader
/// knows about.
/// </para>
/// <para>
/// IT CANNOT THROW. It is called while the process is already failing; an exception raised here
/// would replace a diagnosable crash with an undiagnosable one. The writer beneath it swallows its
/// own I/O failures, and this method swallows everything else.
/// </para>
/// </remarks>
public static class PineCrashLogger
{
    /// <summary>The event id of the crash record. Stable, dotted, snake_case, per §4.1.</summary>
    public const string CrashEventId = "app.crash";

    /// <summary>The logger category the crash record is attributed to.</summary>
    public const string CrashCategory = "Pine.Startup";

    /// <summary>
    /// Records a fatal, redacted crash record and returns.
    /// </summary>
    /// <param name="exception">The exception that is ending the process.</param>
    /// <param name="options">
    /// Where to write. Omit for the shipped defaults; the parameter exists so a test can point the
    /// crash path at a temporary directory instead of the developer's <c>%LOCALAPPDATA%</c>.
    /// </param>
    public static void WriteCrash(Exception exception, PineLoggingOptions? options = null)
    {
        try
        {
            if (exception is null)
            {
                return;
            }

            // A crash is fatal by definition, so the configured minimum level is deliberately
            // overridden here: a head that turned logging down to Critical-only still gets this
            // record, and a head that never configured anything gets it too.
            var effective = options ?? new PineLoggingOptions();

            using var writer = new PineFileLogWriter(effective);

            writer.Write(PineLogRecordFormatter.Format(
                DateTimeOffset.UtcNow,
                LogLevel.Critical,
                new EventId(0, CrashEventId),
                CrashCategory,
                "The application terminated with an unhandled exception.",
                context: null,
                exception));
        }
        catch (Exception)
        {
            // See the class remarks. There is nowhere left to report a failure of the reporter.
        }
    }
}
