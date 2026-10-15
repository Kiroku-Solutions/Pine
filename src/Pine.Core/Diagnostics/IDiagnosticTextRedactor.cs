namespace Pine.Core.Diagnostics;

/// <summary>
/// The redaction boundary as the application layer sees it (TD-84). <c>Pine.Application</c> cannot
/// reference <c>Pine.Infrastructure</c>, where <c>LogRedactor</c> lives, so the diagnostic bundle
/// reaches it through this seam. The bundle re-redacts every log line it packs even though the sink
/// redacted it on the way to disk: a file written by an older build, or edited by hand, is not
/// trusted just because it sits in the log directory.
/// </summary>
public interface IDiagnosticTextRedactor
{
    /// <summary>Redacts one string. Never throws; on failure returns a fixed placeholder.</summary>
    /// <param name="value">The raw text. May be <see langword="null"/>.</param>
    string? Redact(string? value);

    /// <summary>True when <paramref name="key"/> names personal data whatever its value looks like.</summary>
    /// <param name="key">A setting or context key.</param>
    bool IsSensitiveKey(string? key);
}
