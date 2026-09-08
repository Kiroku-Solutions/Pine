using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Pine.Core.Common;

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
/// IT IS ON <see cref="IUserFacingFailure"/> (TD-24) BECAUSE THE SENTENCES ARE ALREADY FOR A
/// PERSON. Each problem names the configuration key, the bound it violated and the value that was
/// read. Those are three things the reader supplied themselves, so none of them is a disclosure --
/// which is exactly the test that interface's remarks set.
/// </para>
/// </remarks>
public sealed class PineOptionsValidationException : Exception, IUserFacingFailure
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

    /// <inheritdoc />
    public string UserFacingMessage => Message;

    private static string BuildMessage(IEnumerable<string> problems)
    {
        var lines = (problems ?? Array.Empty<string>()).ToList();

        return lines.Count == 0
            ? "Pine's configuration could not be used."
            : "Pine's configuration could not be used:" + Environment.NewLine
              + string.Join(Environment.NewLine, lines.Select(line => "  - " + line));
    }
}
