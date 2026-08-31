using System;

namespace Pine.Infrastructure.Diagnostics;

/// <summary>
/// Shapes a raw assembly informational version into the build identifier Pine shows and logs
/// (TD-49).
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS IS A TYPE OF ITS OWN AND NOT A PRIVATE METHOD. The version reaches a user through three
/// different doors — the diagnostics panel, the <c>version</c> field of every log record, and
/// <c>pine --version</c> — and the resolution behind all three has two halves with very different
/// testability. One half reads <see cref="System.Reflection.Assembly.GetEntryAssembly"/>; under
/// <c>dotnet test</c> that is the VSTest host rather than Pine, so a test asserting on its result
/// would be asserting on Microsoft's version number while appearing to assert on Pine's. The other
/// half is this: a pure string rule, with no ambient state, covered exhaustively by
/// <c>ApplicationVersionTests</c>. Splitting them is what makes the tested part honestly tested and
/// the untested part honestly named.
/// </para>
/// <para>
/// THE RULE REVERSES AN EARLIER DECISION. <c>PineLogRecordFormatter</c> used to discard everything
/// after <c>+</c>, on the stated grounds that build metadata is a repository detail which would
/// "put a different version in every developer's log for the same release". That reasoning rested on
/// a premise that was never true: there was no release version. Nothing set one, so the SDK default
/// <c>1.0.0+&lt;sha&gt;</c> was trimmed to the literal <c>1.0.0</c> — identical for every build Pine had
/// ever produced. The trim was not removing noise from a signal; it was removing the only signal.
/// With a real version now set in <c>Directory.Build.props</c>, both halves say something, and a
/// developer build and a CI build of <c>0.1.0-alpha.1</c> are different binaries that a bug report
/// must be able to tell apart.
/// </para>
/// </remarks>
public static class ApplicationVersionText
{
    /// <summary>
    /// How much of the commit id survives into the version string. Twelve hex characters is far
    /// beyond the collision range of a repository this size and still fits inside a log line.
    /// </summary>
    public const int ShortCommitLength = 12;

    /// <summary>The value returned when there is nothing to shape.</summary>
    public const string Unknown = "unknown";

    /// <summary>
    /// Shapes an informational version into the string the <c>version</c> field carries.
    /// </summary>
    /// <param name="informationalVersion">
    /// An assembly's informational version, or <c>null</c>/blank when it has none.
    /// </param>
    /// <returns>
    /// The release version followed by at most <see cref="ShortCommitLength"/> characters of commit
    /// id, or <see cref="Unknown"/> when there was nothing to shape.
    /// </returns>
    public static string Shorten(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion))
        {
            return Unknown;
        }

        var version = informationalVersion.Trim();
        var plus = version.IndexOf('+', StringComparison.Ordinal);

        if (plus <= 0)
        {
            // No build metadata at all: either the SDK's source-control query found no repository,
            // or a release build stripped it. The version stands on its own. `plus == 0` is a
            // malformed version consisting of nothing but metadata; it is returned unchanged rather
            // than reduced to an empty string, because a blank version field reads as "not
            // recorded" and would hide the malformation instead of showing it.
            return version;
        }

        var release = version[..plus];
        var metadata = version[(plus + 1)..];

        // Only the leading commit id is kept. The SDK writes the full 40-character SHA-1, and a
        // build may append further dot-separated metadata after it; anything past the first dot is
        // not a commit and is dropped rather than guessed at.
        var dot = metadata.IndexOf('.', StringComparison.Ordinal);
        var commit = dot >= 0 ? metadata[..dot] : metadata;

        if (commit.Length == 0)
        {
            // A trailing '+' with nothing after it: no dangling separator reaches a log line or a
            // release filename.
            return release;
        }

        return string.Concat(
            release,
            "+",
            commit.Length > ShortCommitLength ? commit[..ShortCommitLength] : commit);
    }
}
