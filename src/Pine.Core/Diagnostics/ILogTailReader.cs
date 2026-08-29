using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Pine.Core.Diagnostics;

/// <summary>
/// Reads back the most recent records the Pine log sink has written to disk.
/// </summary>
/// <remarks>
/// TD-15 EXISTS BECAUSE THE DIAGNOSTICS PANEL HAD NO SUCH SEAM. It fabricated seven log lines in a
/// constructor -- including <c>"Migrations are up to date."</c>, which no code had checked -- and
/// presented them as the application's own output during the one activity the screen exists for.
/// The sink built for TD-14 is the real source; this is the read side of it, and it is an interface
/// so the view model can be tested against a known tail rather than against whatever happens to be
/// in the developer's log directory.
/// </remarks>
public interface ILogTailReader
{
    /// <summary>
    /// Reads at most <paramref name="maxEntries"/> of the newest records, oldest first.
    /// </summary>
    /// <param name="maxEntries">The upper bound on records returned. Zero or less returns nothing.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// The records in chronological order, so the caller can append them to a terminal view without
    /// reversing. An empty list when no log file exists yet -- which is a real, displayable state
    /// and MUST NOT be padded with a synthetic record.
    /// </returns>
    /// <remarks>
    /// Implementations must not throw for an unreadable log directory. A diagnostics screen that
    /// crashes because diagnostics are unavailable is worse than one that reports nothing, and the
    /// sink itself already swallows its own write failures for the same reason.
    /// </remarks>
    Task<IReadOnlyList<LogTailEntry>> ReadTailAsync(int maxEntries, CancellationToken cancellationToken = default);
}
