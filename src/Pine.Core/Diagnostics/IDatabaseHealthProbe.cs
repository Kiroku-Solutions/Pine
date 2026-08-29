using System.Threading;
using System.Threading.Tasks;

namespace Pine.Core.Diagnostics;

/// <summary>
/// Asks the vault database whether it is actually there and actually migrated.
/// </summary>
/// <remarks>
/// The point of the seam is that the answer is MEASURED. <c>DiagnosticsViewModel</c> previously
/// asserted both halves of it from constants -- <c>DatabaseStatus = "Connected (Healthy)"</c> and a
/// hardcoded <c>"Migrations are up to date."</c> log line -- so the screen was at its most confident
/// in exactly the situation where the database was missing.
/// </remarks>
public interface IDatabaseHealthProbe
{
    /// <summary>Probes the database behind the current vault session.</summary>
    /// <param name="cancellationToken">Cancels the probe.</param>
    /// <returns>The reading. Never throws; a failure is reported as a status.</returns>
    Task<DatabaseHealth> CheckAsync(CancellationToken cancellationToken = default);
}
