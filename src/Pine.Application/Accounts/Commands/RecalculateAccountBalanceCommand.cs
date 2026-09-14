using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Pine.Application.Common;
using Pine.Core.Accounts;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Transactions;

namespace Pine.Application.Accounts.Commands;

/// <summary>
/// What a recalculation found and what it did about it.
/// </summary>
/// <remarks>
/// <para>
/// This is deliberately a report, not an acknowledgement. The user is being told that the number they
/// have been reading may have been wrong, so "done" is not an adequate answer: they need the old
/// figure, the new one, how far apart they were, and how many transactions the new one was built from.
/// </para>
/// <para>
/// <see cref="OpeningBalanceWasInferred"/> is the honest part. For an account that predates opening
/// balance tracking there is no recorded figure to add the ledger to, so one is inferred from the
/// balance as it stands — which makes THIS recalculation an identity by construction, drift and all.
/// A caller that does not surface this flag is telling the user their balance was verified when it was
/// only anchored. Every recalculation after this one is a real check.
/// </para>
/// </remarks>
/// <param name="AccountId">The account that was recalculated.</param>
/// <param name="PreviousBalance">The balance as it stood before the recalculation.</param>
/// <param name="RecalculatedBalance">The balance computed from the opening figure plus the ledger.</param>
/// <param name="Drift">
/// <paramref name="RecalculatedBalance"/> minus <paramref name="PreviousBalance"/>: how far the stored
/// balance had wandered from its own ledger. Zero means the balance was already correct.
/// </param>
/// <param name="CurrencyCode">The account's currency, so the figures above can be rendered.</param>
/// <param name="TransactionsCounted">How many transactions still in force were summed.</param>
/// <param name="OpeningBalance">The opening figure used, whether recorded or inferred.</param>
/// <param name="OpeningBalanceWasInferred">
/// <see langword="true"/> when the account had no opening balance on record and one was inferred from
/// the current balance during this command — in which case <paramref name="Drift"/> is necessarily zero
/// and proves nothing.
/// </param>
public sealed record RecalculateAccountBalanceResult(
    Guid AccountId,
    decimal PreviousBalance,
    decimal RecalculatedBalance,
    decimal Drift,
    string CurrencyCode,
    int TransactionsCounted,
    decimal OpeningBalance,
    bool OpeningBalanceWasInferred);

/// <summary>
/// Recomputes an account's balance from its opening figure plus every transaction still in force.
/// </summary>
/// <param name="AccountId">The account to recalculate.</param>
public record RecalculateAccountBalanceCommand(Guid AccountId)
    : IRequest<RecalculateAccountBalanceResult>, ITransactionalRequest;

/// <summary>
/// Handles <see cref="RecalculateAccountBalanceCommand"/>.
/// </summary>
/// <remarks>
/// <para>
/// THE OTHER HALF OF TD-02. Voiding gives the user a way to correct a transaction; this gives them a
/// way to repair a balance that no longer agrees with its transactions — from a partial write before
/// TD-01 closed, from a hand-edited vault, or from any future defect not yet found. Without it, a
/// drifted balance is permanent even though every fact needed to fix it is sitting in the same
/// database.
/// </para>
/// <para>
/// THE SUM IS COMPUTED IN .NET, NOT IN SQL, and that is load-bearing rather than incidental.
/// <c>ITransactionRepository.GetAmountsInForceAsync</c> projects the amounts and this handler adds
/// them as <see cref="decimal"/>. SQLite stores these values as TEXT (TD-31) and would coerce them to
/// REAL to add them, so a pushed-down <c>SUM</c> would return a floating-point approximation — and a
/// balance "repaired" to an approximation is a worse defect than the drift being repaired.
/// </para>
/// <para>
/// VOIDED TRANSACTIONS ARE EXCLUDED, WHICH IS WHAT TIES THE TWO HALVES TOGETHER. The void handler
/// already moved the balance incrementally when it voided the row; this recomputation reaches the same
/// figure independently, from the rows alone. If the two ever disagree, the drift this command reports
/// is exactly that disagreement.
/// </para>
/// </remarks>
public class RecalculateAccountBalanceCommandHandler
    : IRequestHandler<RecalculateAccountBalanceCommand, RecalculateAccountBalanceResult>
{
    private readonly IAccountRepository _accountRepository;
    private readonly ITransactionRepository _transactionRepository;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initialises the handler.
    /// </summary>
    /// <param name="accountRepository">Repository for the account being repaired.</param>
    /// <param name="transactionRepository">Repository supplying the ledger amounts.</param>
    /// <param name="timeProvider">Clock used to stamp the account update.</param>
    public RecalculateAccountBalanceCommandHandler(
        IAccountRepository accountRepository,
        ITransactionRepository transactionRepository,
        TimeProvider timeProvider)
    {
        _accountRepository = accountRepository;
        _transactionRepository = transactionRepository;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<RecalculateAccountBalanceResult> Handle(
        RecalculateAccountBalanceCommand request,
        CancellationToken cancellationToken)
    {
        // Existence is established by the validator, outside this transaction.
        var account = await _accountRepository.GetByIdAsync(request.AccountId, cancellationToken)
                      ?? throw new InvalidOperationException("Account not found.");

        var currency = account.Balance.Currency;
        var previousBalance = account.Balance.Amount;

        var amounts = await _transactionRepository.GetAmountsInForceAsync(request.AccountId, cancellationToken);

        // decimal addition, in .NET. See the remarks on this class and on GetAmountsInForceAsync.
        var ledgerSum = amounts.Sum();

        var openingBalanceWasInferred = account.OpeningBalanceAmount is null;
        if (openingBalanceWasInferred)
        {
            // The account predates opening-balance tracking, so the figure is genuinely unrecorded.
            // The only expression available is "balance minus ledger", which assumes the balance is
            // right — the very assumption this command exists to test. It is used anyway, because the
            // alternative is that the account can never be checked at all, and it is computed here in
            // exact decimal and reported back as inferred rather than written silently by a migration.
            // The consequence, stated plainly: this first run cannot detect drift. Every later one can.
            account.AdoptOpeningBalance(new Money(previousBalance - ledgerSum, currency), _timeProvider);
        }

        var openingBalance = account.OpeningBalanceAmount!.Value;
        var recalculated = openingBalance + ledgerSum;

        account.RecalculateBalance(new Money(recalculated, currency), _timeProvider);
        await _accountRepository.SaveAsync(account, cancellationToken);

        return new RecalculateAccountBalanceResult(
            AccountId: account.Id,
            PreviousBalance: previousBalance,
            RecalculatedBalance: recalculated,
            Drift: recalculated - previousBalance,
            CurrencyCode: currency.Value,
            TransactionsCounted: amounts.Count,
            OpeningBalance: openingBalance,
            OpeningBalanceWasInferred: openingBalanceWasInferred);
    }
}

/// <summary>
/// Rejects a <see cref="RecalculateAccountBalanceCommand"/> for an account that does not exist,
/// before the pipeline opens a database transaction.
/// </summary>
/// <remarks>
/// <para>
/// Most conditions this command could refuse are conditions it EXISTS to handle — a drifted balance,
/// an absent opening figure, an empty ledger — and refusing them would be refusing to do the job. An
/// account with no transactions at all recalculates to its opening balance, which is correct and
/// worth confirming. So there are only two rules, and the second one is the interesting one.
/// </para>
/// <para>
/// THE CURRENCY RULE, AND WHY IT IS THE MOST IMPORTANT LINE IN THIS FILE. The handler sums bare
/// decimals and applies the total in the account's currency. That is sound for anything the current
/// write paths can produce, because all four transaction validators check a transaction's currency
/// against its account's. It is NOT sound for a row written before those validators existed —
/// <c>VoidTransactionCommandValidator</c> already documents that such rows are reachable and refuses
/// to void them. Without this rule the same row would have its raw number folded into a total in a
/// different currency, the balance would be overwritten with a figure that is not a quantity of
/// anything, and <c>SettingsViewModel</c> would report it to the user as a successful check.
/// </para>
/// <para>
/// That is the one failure mode this whole feature must not have: every other way it can fail leaves
/// the balance as it found it, whereas this one would make a balance WORSE while announcing that it
/// had been verified. A repair tool that can silently corrupt is worse than no repair tool. The
/// command refuses instead, and names the currencies, so the user is told there is a real problem
/// rather than handed a fabricated all-clear.
/// </para>
/// <para>
/// Refusing rather than skipping is also deliberate. Dropping the odd row from the sum would produce
/// a total that is wrong in a quieter way, and would silently delete a transaction's contribution to
/// a balance — which is the defect class TD-03 closed on the import path.
/// </para>
/// </remarks>
public sealed class RecalculateAccountBalanceCommandValidator
    : IRequestValidator<RecalculateAccountBalanceCommand>
{
    private readonly IAccountRepository _accountRepository;
    private readonly ITransactionRepository _transactionRepository;

    /// <summary>
    /// Initialises the validator.
    /// </summary>
    /// <param name="accountRepository">Used to resolve the account being recalculated.</param>
    /// <param name="transactionRepository">Used to check the ledger's currencies before it is summed.</param>
    public RecalculateAccountBalanceCommandValidator(
        IAccountRepository accountRepository,
        ITransactionRepository transactionRepository)
    {
        _accountRepository = accountRepository;
        _transactionRepository = transactionRepository;
    }

    /// <summary>
    /// Checks that the account exists and that every transaction about to be summed is denominated in
    /// that account's currency.
    /// </summary>
    /// <param name="request">The command about to be handled.</param>
    /// <param name="cancellationToken">Token observed while loading.</param>
    /// <returns>An empty list when the command may proceed, otherwise the errors that stop it.</returns>
    public async Task<IReadOnlyList<ValidationError>> ValidateAsync(
        RecalculateAccountBalanceCommand request,
        CancellationToken cancellationToken = default)
    {
        var account = await _accountRepository.GetByIdAsync(request.AccountId, cancellationToken);
        if (account is null)
        {
            return new[]
            {
                new ValidationError(
                    nameof(RecalculateAccountBalanceCommand.AccountId),
                    $"Account {request.AccountId} was not found.",
                    ValidationText.AccountNotFound()),
            };
        }

        var accountCurrency = account.Balance.Currency.Value;
        var currencies = await _transactionRepository.GetDistinctCurrenciesInForceAsync(
            request.AccountId, cancellationToken);

        var foreign = currencies
            .Where(code => !string.Equals(code, accountCurrency, StringComparison.Ordinal))
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToArray();

        if (foreign.Length > 0)
        {
            return new[]
            {
                new ValidationError(
                    nameof(RecalculateAccountBalanceCommand.AccountId),
                    $"'{account.Name}' holds transactions in {string.Join(", ", foreign)} but the "
                    + $"account is in {accountCurrency}. Its balance cannot be recomputed until those "
                    + "transactions are corrected, because there is no meaningful total to compute.",
                    ValidationText.AccountHoldsForeignCurrencies(account.Name, string.Join(", ", foreign), accountCurrency)),
            };
        }

        return Array.Empty<ValidationError>();
    }
}
