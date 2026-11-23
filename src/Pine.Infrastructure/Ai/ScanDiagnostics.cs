using System;

namespace Pine.Infrastructure.Ai;

/// <summary>
/// A one-line, PII-free account of what a receipt scan did and how long each stage took (counts and
/// milliseconds, never recognised text). A head with a system log the file logger does not reach --
/// Android's logcat -- subscribes to <see cref="Reported"/> and forwards it; nothing subscribes by default.
/// </summary>
public static class ScanDiagnostics
{
    /// <summary>Raised after each scan with a line such as <c>scan prep_ms=120 ocr_ms=4000 lines=31 ...</c>.</summary>
    public static event Action<string>? Reported;

    internal static void Report(string line)
    {
        try
        {
            Reported?.Invoke(line);
        }
        catch (Exception)
        {
            // A diagnostic must never fail a scan.
        }
    }
}
