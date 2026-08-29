namespace Pine.Core.Diagnostics;

/// <summary>
/// The host facts a diagnostics screen reports: which build this is, what it is running on, and how
/// much managed memory it currently holds.
/// </summary>
/// <remarks>
/// A SEAM FOR THREE ONE-LINERS, BECAUSE TWO OF THEM WERE WRONG. <c>OsVersion</c> and the memory
/// reading were already real; <see cref="AppVersion"/> was the literal
/// <c>"Pine Vault v1.0.0-beta.1"</c>, frozen at whatever the value had been when someone typed it.
/// Putting all three behind one interface is what lets a test assert that the view model reports
/// what it was given rather than what it decided, which is the property TD-15 is really about.
/// </remarks>
public interface IRuntimeInfoProvider
{
    /// <summary>
    /// The application's informational version.
    /// </summary>
    /// <remarks>
    /// This MUST be the same value the log sink stamps into every record's <c>version</c> field
    /// (<c>30-Observability</c> §4.1). A panel that named a different build than the log file it is
    /// displaying would be actively misleading during the incident it exists to serve.
    /// </remarks>
    string AppVersion { get; }

    /// <summary>A description of the operating system and architecture the process is running on.</summary>
    string OsDescription { get; }

    /// <summary>Bytes currently held by the managed heap.</summary>
    /// <remarks>
    /// The MANAGED heap specifically, not the process working set. The old label said "Engine
    /// Memory" over a <c>GC.GetTotalMemory</c> reading, which are not the same quantity; the view
    /// labels it honestly now rather than the provider guessing at a number the user would read as
    /// total process memory.
    /// </remarks>
    long ManagedMemoryBytes { get; }
}
