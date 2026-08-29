using System;
using System.IO;
using Microsoft.Extensions.Logging;

namespace Pine.Infrastructure.Diagnostics;

/// <summary>
/// The knobs of the Pine file log sink: where records are written, how verbose they are, and how
/// much of them is kept on disk. Defaults come from <c>30-Observability</c> §4.4.
/// </summary>
/// <remarks>
/// <para>
/// A MUTABLE OPTIONS CLASS RATHER THAN <c>IOptions&lt;T&gt;</c>. The sink has to be usable before a
/// container exists at all -- <see cref="PineCrashLogger"/> runs from <c>Program.Main</c>'s catch
/// block, where the failure being recorded may well BE the container -- so the options object must
/// be constructible with <c>new</c> and no options infrastructure. The DI extension configures it
/// with the same <c>Action&lt;T&gt;</c> shape <c>AddPineVaultAutoLock</c> already uses, so the two
/// registrations read alike.
/// </para>
/// <para>
/// ENCRYPTION AT REST IS DELIBERATELY ABSENT. <c>30-Observability</c> §4.4 asks for logs encrypted
/// with the data-store key, and this type has no key property because half the records this sink
/// exists to capture -- startup, key-derivation failure, crash -- happen while the vault is locked
/// and the key does not exist in the process. Redaction at the boundary
/// (<see cref="LogRedactor"/>) plus a path outside the vault directory is this iteration's posture;
/// the encryption clause is tracked as a separate debt rather than silently satisfied.
/// </para>
/// </remarks>
public sealed class PineLoggingOptions
{
    /// <summary>50 MB per file, per <c>30-Observability</c> §4.4.</summary>
    public const long DefaultMaxBytesPerFile = 50L * 1024 * 1024;

    /// <summary>Ten retained files, per <c>30-Observability</c> §4.4.</summary>
    public const int DefaultRetainedFileCount = 10;

    /// <summary>The file-name suffix every Pine log file carries, per <c>30-Observability</c> §4.4.</summary>
    public const string FileSuffix = ".log.jsonl";

    /// <summary>
    /// The directory log files are written into. Defaults to
    /// <c>%LOCALAPPDATA%/Pine/logs</c> (<see cref="Environment.SpecialFolder.LocalApplicationData"/>),
    /// which is outside the vault directory on every head.
    /// </summary>
    public string Directory { get; set; } = DefaultDirectory();

    /// <summary>
    /// The lowest level that reaches the file. Defaults to
    /// <see cref="LogLevel.Information"/> -- <c>30-Observability</c> §4.2 reserves <c>trace</c> for
    /// per-detail records such as SQL parameter values, which is exactly the material §4.3 forbids
    /// leaving the process, so it is off unless a maintainer turns it on.
    /// </summary>
    public LogLevel MinimumLevel { get; set; } = LogLevel.Information;

    /// <summary>The size at which the current file is rotated. See <see cref="DefaultMaxBytesPerFile"/>.</summary>
    public long MaxBytesPerFile { get; set; } = DefaultMaxBytesPerFile;

    /// <summary>
    /// How many log files (the live one included) survive retention. See
    /// <see cref="DefaultRetainedFileCount"/>.
    /// </summary>
    public int RetainedFileCount { get; set; } = DefaultRetainedFileCount;

    /// <summary>Computes the shipped default log directory.</summary>
    /// <returns><c>%LOCALAPPDATA%/Pine/logs</c>, or <c>&lt;base directory&gt;/logs</c> when the
    /// platform reports no local application data folder.</returns>
    public static string DefaultDirectory()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        return string.IsNullOrEmpty(localAppData)
            ? Path.Combine(AppContext.BaseDirectory, "logs")
            : Path.Combine(localAppData, "Pine", "logs");
    }
}
