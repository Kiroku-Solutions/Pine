using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Pine.Core.Configuration;

/// <summary>
/// Thrown at startup when the configuration Pine was given cannot be used (TD-27).
/// </summary>
/// <remarks>
/// <para>
/// FAIL AT STARTUP, NOT AT THE FIRST USE. A configuration system whose bad values surface one at a
/// time, months apart, at whichever screen happens to read them first, is worse than no
/// configuration system: it converts one editing mistake into several unrelated-looking defects.
/// The container is built once, so this is thrown once, with EVERY problem listed rather than the
/// first one -- a person fixing a config file should need one round trip, not five.
/// </para>
/// <para>
/// NOT ON <c>IUserFacingFailure</c>, AND IT USED TO BE (TD-139). It was put there in TD-24 because
/// its sentences are safe to show: each names a configuration key, a bound and the value read, all
/// supplied by the reader. Safe, and never shown. It is thrown by <c>PineConfiguration.Bind</c> while
/// <c>PineCompositionRoot.Build</c> is still building the container — before any window, view model
/// or localizer exists — and every head's handler passes it to <c>PineCrashLogger</c> and rethrows. No
/// path from this throw reaches <c>UserFacingError</c>. When TD-139 turned the interface's sentences
/// into keys, translating these would have produced Spanish text for a crash log. The opt-in is
/// removed instead of kept as a claim nothing exercises; the sentences stay in
/// <see cref="Exception.Message"/>, which is where a person editing the file will read them.
/// </para>
/// </remarks>
public sealed class PineOptionsValidationException : Exception
{
    /// <summary>Creates the exception from the problems validation found.</summary>
    /// <param name="problems">One sentence per problem. Must not be empty.</param>
    public PineOptionsValidationException(IEnumerable<string> problems)
        : base(BuildMessage(problems))
    {
        Problems = new ReadOnlyCollection<string>((problems ?? Array.Empty<string>()).ToList());
    }

    /// <summary>Creates the exception with a caller-supplied message.</summary>
    public PineOptionsValidationException()
        : base("Pine's configuration could not be used.")
    {
        Problems = new ReadOnlyCollection<string>(Array.Empty<string>());
    }

    /// <summary>Creates the exception with a caller-supplied message.</summary>
    public PineOptionsValidationException(string message)
        : base(message)
    {
        Problems = new ReadOnlyCollection<string>(Array.Empty<string>());
    }

    /// <summary>Creates the exception with a caller-supplied message.</summary>
    public PineOptionsValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
        Problems = new ReadOnlyCollection<string>(Array.Empty<string>());
    }

    /// <summary>Every problem found, in the order the sections were checked.</summary>
    public IReadOnlyList<string> Problems { get; }

    private static string BuildMessage(IEnumerable<string> problems)
    {
        var lines = (problems ?? Array.Empty<string>()).ToList();

        return lines.Count == 0
            ? "Pine's configuration could not be used."
            : "Pine's configuration could not be used:" + Environment.NewLine
              + string.Join(Environment.NewLine, lines.Select(line => "  - " + line));
    }
}
