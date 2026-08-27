using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Pine.Core.Transactions;

public interface ITransactionRepository
{
    Task<Transaction?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<Transaction>> GetByAccountIdAsync(Guid accountId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Transaction>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<Pine.Core.Common.PagedResult<Transaction>> GetPagedAsync(int page, int pageSize, CancellationToken cancellationToken = default);
    Task SaveAsync(Transaction transaction, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the subset of <paramref name="candidateKeys"/> that already appear as the
    /// <see cref="Transaction.ImportKey"/> of a transaction on the given account.
    /// </summary>
    /// <remarks>
    /// This is the lookup import de-duplication is built on. It answers only "which of these keys do
    /// you already have?", so an importer can decide row by row without the repository loading, or
    /// the caller inspecting, whole transactions. Keys are compared ordinally, and transactions with
    /// no import key are never matched.
    /// </remarks>
    /// <param name="accountId">The account being imported into.</param>
    /// <param name="candidateKeys">The keys of the rows about to be imported.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The keys already present on that account; empty when <paramref name="candidateKeys"/> is empty.
    /// </returns>
    Task<IReadOnlyCollection<string>> GetExistingImportKeysAsync(
        Guid accountId,
        IReadOnlyCollection<string> candidateKeys,
        CancellationToken cancellationToken = default);
}
