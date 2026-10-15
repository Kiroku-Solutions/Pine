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
    /// <summary>
    /// One page of transactions, newest first. When <paramref name="includeVoided"/> is false, voided
    /// rows are excluded from both the page and the total count, so paging stays dense (TD-71).
    /// </summary>
    Task<Pine.Core.Common.PagedResult<Transaction>> GetPagedAsync(int page, int pageSize, bool includeVoided, CancellationToken cancellationToken = default);
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

    /// <summary>
    /// Returns the transaction on the given account carrying the given import key, if one exists.
    /// </summary>
    /// <remarks>
    /// This is the single-key counterpart to <see cref="GetExistingImportKeysAsync"/>, used where a
    /// caller needs the matched transaction itself rather than just a yes/no on a batch of candidate
    /// keys — the scanned-receipt duplicate check (TD-20) reports the date, amount and payee of the
    /// transaction it collided with. A null <paramref name="importKey"/> never matches: transactions
    /// with no import key of their own are excluded, exactly as in the batch lookup, and a caller with
    /// no key to check should not call this method at all.
    /// </remarks>
    /// <param name="accountId">The account being checked.</param>
    /// <param name="importKey">The key to look up.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The matching transaction, or null when the account holds none with that key.</returns>
    Task<Transaction?> GetByImportKeyAsync(
        Guid accountId,
        string importKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the signed amount of every transaction on the given account that is still in force —
    /// that is, every transaction except the voided ones.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY THIS RETURNS THE AMOUNTS AND NOT THEIR SUM. Summing has to happen in .NET, over
    /// <see cref="decimal"/>, and this signature is what makes that non-negotiable. EF Core's SQLite
    /// provider stores <see cref="decimal"/> as TEXT (TD-31); pushing <c>SUM</c> down into SQL makes
    /// SQLite coerce that TEXT to REAL and add it in IEEE-754 binary floating point, where a handful of
    /// ordinary two-decimal amounts is already enough to produce a total that is off by a fraction of a
    /// cent. A balance repaired with a drifting total would be a worse defect than the drift this
    /// feature exists to repair.
    /// </para>
    /// <para>
    /// WHY NOT <c>Money</c>. The amounts all belong to one account and therefore to one currency, which
    /// the account itself already knows and can never change. Re-attaching a currency to each row here
    /// would invite the caller to trust a per-row currency that is not independently meaningful.
    /// </para>
    /// </remarks>
    /// <param name="accountId">The account whose ledger is being totalled.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// One entry per transaction still in force, signed as stored. Empty when the account has no
    /// transactions, or none that are still in force.
    /// </returns>
    Task<IReadOnlyList<decimal>> GetAmountsInForceAsync(
        Guid accountId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the distinct currency codes carried by the transactions on the given account that are
    /// still in force.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS IS THE GUARD THAT MAKES <see cref="GetAmountsInForceAsync"/> SAFE TO USE. That method
    /// returns bare amounts, on the reasoning that every transaction on an account shares the
    /// account's currency. That reasoning holds for anything the current write paths can produce — all
    /// four transaction validators enforce it — but NOT necessarily for rows written before those
    /// validators existed. A single legacy row in another currency would otherwise have its raw
    /// number folded into the account's total, and the balance would be "repaired" to a figure that
    /// is not a quantity of anything.
    /// </para>
    /// <para>
    /// It is a separate call rather than a currency attached to each amount because the caller does
    /// not want a per-row currency — it wants to know whether the assumption holds at all, before it
    /// starts adding. One row disagreeing is enough to refuse, and refusing is the only correct
    /// answer: silently skipping the odd row would produce a total that is wrong in a different and
    /// less visible way.
    /// </para>
    /// </remarks>
    /// <param name="accountId">The account being inspected.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The distinct currency codes, compared ordinally. Empty when the account has no transactions
    /// still in force.
    /// </returns>
    Task<IReadOnlyCollection<string>> GetDistinctCurrenciesInForceAsync(
        Guid accountId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts, in the store, the transactions with at least one split whose category is non-empty (TD-133).
    /// </summary>
    /// <remarks>
    /// The store trims only the space character, .NET trims every Unicode whitespace character, so
    /// this count can exceed the exact one by rows whose category is only a tab or newline. It is a
    /// cheap upper bound for gating; callers needing the exact figure re-check each row they page.
    /// </remarks>
    Task<int> CountPossiblyCategorisedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// One page of the transactions that have at least one split with a non-empty category, in a
    /// stable order (by identifier), so the whole set can be walked page by page (TD-133).
    /// </summary>
    /// <param name="skip">Rows to skip. Not negative.</param>
    /// <param name="take">Page size. Positive.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<Transaction>> GetCategorisedPageAsync(int skip, int take, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every transaction that carries a link to another, with the state of the row it points at
    /// (TD-124). Read-only; it exists so the shape of pre-existing transfer data can be measured.
    /// </summary>
    Task<IReadOnlyList<TransferLinkRow>> GetTransferLinkRowsAsync(CancellationToken cancellationToken = default);
}
