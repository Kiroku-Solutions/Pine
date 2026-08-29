using System;
using System.Globalization;
using System.Runtime.InteropServices;
using Pine.Core.Diagnostics;

namespace Pine.Infrastructure.Diagnostics;

/// <summary>
/// Reports the three host facts a diagnostics screen shows, measured rather than typed in.
/// </summary>
/// <remarks>
/// <para>
/// TD-15: TWO OF THESE THREE USED TO BE FICTION. <c>DiagnosticsViewModel</c> carried the literal
/// <c>"Pine Vault v1.0.0-beta.1"</c> -- frozen at whatever the version had been on the day somebody
/// typed it -- and labelled a managed-heap reading as total engine memory. Nothing here decides
/// anything: every value is read from the runtime at the moment it is asked for.
/// </para>
/// <para>
/// <see cref="AppVersion"/> IS THE LOG SINK'S VERSION, NOT A SECOND OPINION. It returns
/// <c>PineLogRecordFormatter.ApplicationVersion</c> -- the same string stamped into every record's
/// <c>version</c> field, per <c>30-Observability</c> §4.1 -- because the panel renders that build
/// number directly above a tail of that same log file. Re-resolving the entry assembly's
/// informational version here would produce the right answer today and a divergent one the first
/// time either side is touched, which during an incident is the one failure mode that actively
/// misleads: the reader would trust a build number that does not belong to the records beneath it.
/// If this ever needs to change, change it in the formatter and let this follow.
/// </para>
/// <para>
/// STATELESS, HENCE SAFE AS A SINGLETON. There is nothing to cache -- the version is already a
/// static readonly field, and the other two must be read fresh or they are not measurements.
/// </para>
/// </remarks>
public sealed class PineRuntimeInfoProvider : IRuntimeInfoProvider
{
    /// <inheritdoc />
    /// <remarks>
    /// The single source is <c>PineLogRecordFormatter.ApplicationVersion</c>. See the type remarks:
    /// these two consumers MUST NOT diverge.
    /// </remarks>
    public string AppVersion => PineLogRecordFormatter.ApplicationVersion;

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// <see cref="RuntimeInformation"/> RATHER THAN <c>Environment.OSVersion</c>. On Windows,
    /// <c>Environment.OSVersion</c> is subject to the application-compatibility shim: an executable
    /// without the right entries in its manifest is told it is running on Windows 8, on Windows 11,
    /// forever. A diagnostics panel whose entire value is that the number in it is real cannot use
    /// an API that is documented to lie. <see cref="RuntimeInformation.OSDescription"/> is read from
    /// the OS itself and is not shimmed.
    /// </para>
    /// <para>
    /// Both architectures are reported because they can legitimately differ -- an x64 process under
    /// emulation on an arm64 machine is exactly the configuration whose native-library failures a
    /// bug report needs to name, and either figure alone hides it.
    /// </para>
    /// </remarks>
    public string OsDescription => string.Create(
        CultureInfo.InvariantCulture,
        $"{RuntimeInformation.OSDescription.Trim()} ({RuntimeInformation.OSArchitecture}, process {RuntimeInformation.ProcessArchitecture})");

    /// <inheritdoc />
    /// <remarks>
    /// <c>forceFullCollection: false</c>, AND THAT IS THE WHOLE POINT. Passing <c>true</c> makes the
    /// reading more accurate by running a blocking full collection first -- that is, by perturbing
    /// the process the screen exists to observe, on a refresh the user may hold down. An instrument
    /// that changes what it measures is not a diagnostic; a slightly stale heap figure is.
    /// </remarks>
    public long ManagedMemoryBytes => GC.GetTotalMemory(forceFullCollection: false);
}
