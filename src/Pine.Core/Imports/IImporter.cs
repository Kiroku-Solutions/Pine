using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Pine.Core.Transactions;

namespace Pine.Core.Importing;

/// <summary>
/// Contract for transaction importers (CSV, Excel, OFX, etc.).
/// </summary>
public interface IImporter
{
    /// <summary>
    /// Parses a file stream into a list of transactions based on the provided profile.
    /// </summary>
    Task<IReadOnlyList<Transaction>> ImportAsync(Pine.Core.Accounts.Account targetAccount, Stream fileStream, ImportProfile profile, CancellationToken cancellationToken = default);
}
