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
/// What setting an opening balance did to the account.
/// </summary>
/// <param name="AccountId">The account.</param>
/// <param name="OpeningBalance">The figure the user supplied, now on record.</param>
/// <param name="PreviousBalance">The stored balance before the command.</param>
/// <param name="NewBalance">The balance after: the opening figure plus every transaction in force.</param>
/// <param name="Drift">
/// <paramref name="NewBalance"/> minus <paramref name="PreviousBalance"/>. Non-zero means the stored
/// balance disagreed with the opening figure the user supplied and has been corrected.
/// </param>
/// <param name="CurrencyCode">The account's currency.</param>
/// <param name="TransactionsCounted">How many transactions in force were summed.</param>
public sealed record SetOpeningBalanceResult(
    Guid AccountId,
    decimal OpeningBalance,
    decimal PreviousBalance,
    decimal NewBalance,
    decimal Drift,
    string CurrencyCode,
    int TransactionsCounted);

/// <summary>
/// Records the opening balance of an account that has none on record, and re-derives the balance from
/// it, in one database transaction (TD-70).
/// </summary>
/// <param name="AccountId">The account to set it on.</param>
/// <param name="OpeningBalance">The balance the account started from, in the account's own currency.</param>
public record SetOpeningBalanceCommand(Guid AccountId, decimal OpeningBalance)
    : IRequest<SetOpeningBalanceResult>, ITransactionalRequest;

/// <summary>
/// Handles <see cref="SetOpeningBalanceCommand"/>.
/// </summary>
/// <remarks>
/// <para>
/// THE EXPLICIT ALTERNATIVE TO INFERENCE. An account created before opening balances were tracked has
/// none, and <see cref="RecalculateAccountBalanceCommandHandler"/> would infer one from the current
/// balance, which cannot detect existing drift. Here the user supplies the figure, so the balance can
/// be checked against something independent of itself.
/// </para>
/// <para>
/// IT IS A ONE-TIME ACT, not an editor: <see cref="Account.AdoptOpeningBalance"/> refuses an account
/// that already has one, so the reference a recalculation is measured against cannot be moved to make
/// a disagreement disappear. Recording the figure and recomputing the balance from it are two changes
/// to one aggregate, committed together by <see cref="ITransactionalRequest"/>; recording alone would
/// leave a balance that does not match the figure just entered.
/// </para>
/// </remarks>
public class SetOpeningBalanceCommandHandler : IRequestHandler<SetOpeningBalanceCommand, SetOpeningBalanceResult>
{
    private readonly IAccountRepository _accountRepository;
    private readonly ITransactionRepository _transactionRepository;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initialises the handler.</summary>
    /// <param name="accountRepository">Repository for the account.</param>
    /// <param name="transactionRepository">Repository supplying the ledger amounts.</param>
    /// <param name="timeProvider">Clock used to stamp the update.</param>
    public SetOpeningBalanceCommandHandler(
        IAccountRepository accountRepository,
        ITransactionRepository transactionRepository,
        TimeProvider timeProvider)
    {
        _accountRepository = accountRepository;
        _transactionRepository = transactionRepository;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<SetOpeningBalanceResult> Handle(SetOpeningBalanceCommand request, CancellationToken cancellationToken)
    {
        // Existence and "has no opening balance yet" are established by the validator, outside this
        // transaction; Account.AdoptOpeningBalance throws if the second is violated regardless.
        var account = await _accountRepository.GetByIdAsync(request.AccountId, cancellationToken)
                      ?? throw new InvalidOperationException("Account not found.");

        var currency = account.Balance.Currency;
        var previousBalance = account.Balance.Amount;

        var amounts = await _transactionRepository.GetAmountsInForceAsync(request.AccountId, cancellationToken);
        var ledgerSum = amounts.Sum();

        var opening = new Money(request.OpeningBalance, currency);
        account.AdoptOpeningBalance(opening, _timeProvider);

        var recalculated = opening.Amount + ledgerSum;
        account.RecalculateBalance(new Money(recalculated, currency), _timeProvider);
        await _accountRepository.SaveAsync(account, cancellationToken);

        return new SetOpeningBalanceResult(
            account.Id, opening.Amount, previousBalance, recalculated, recalculated - previousBalance,
            currency.Value, amounts.Count);
    }
}

/// <summary>
/// Rejects a <see cref="SetOpeningBalanceCommand"/> that cannot be carried out, before the pipeline
/// opens a database transaction.
/// </summary>
public sealed class SetOpeningBalanceCommandValidator : IRequestValidator<SetOpeningBalanceCommand>
{
    private readonly IAccountRepository _accountRepository;
    private readonly ITransactionRepository _transactionRepository;

    /// <summary>Initialises the validator.</summary>
    /// <param name="accountRepository">Used to resolve the account.</param>
    /// <param name="transactionRepository">Used to check the ledger's currencies before it is summed.</param>
    public SetOpeningBalanceCommandValidator(
        IAccountRepository accountRepository,
        ITransactionRepository transactionRepository)
    {
        _accountRepository = accountRepository;
        _transactionRepository = transactionRepository;
    }

    /// <summary>
    /// Checks that the account exists and has no opening balance yet, that the figure fits the
    /// currency's scale, and that the ledger is single-currency (the same rule as a recalculation).
    /// </summary>
    /// <param name="request">The command about to be handled.</param>
    /// <param name="cancellationToken">Token observed while loading.</param>
    /// <returns>An empty list when the command may proceed, otherwise the errors that stop it.</returns>
    public async Task<IReadOnlyList<ValidationError>> ValidateAsync(
        SetOpeningBalanceCommand request,
        CancellationToken cancellationToken = default)
    {
        var account = await _accountRepository.GetByIdAsync(request.AccountId, cancellationToken);
        if (account is null)
        {
            return new[]
            {
                new ValidationError(
                    nameof(SetOpeningBalanceCommand.AccountId),
                    $"Account {request.AccountId} was not found.",
                    ValidationText.AccountNotFound()),
            };
        }

        var errors = new List<ValidationError>();

        if (account.OpeningBalanceAmount is not null)
        {
            errors.Add(new ValidationError(
                nameof(SetOpeningBalanceCommand.AccountId),
                $"'{account.Name}' already has an opening balance on record and it cannot be changed.",
                ValidationText.OpeningBalanceAlreadySet(account.Name)));
            return errors;
        }

        var currency = account.Balance.Currency;
        if (Money.Round(request.OpeningBalance, currency) != request.OpeningBalance)
        {
            errors.Add(new ValidationError(
                nameof(SetOpeningBalanceCommand.OpeningBalance),
                $"{request.OpeningBalance} has more decimal places than {currency.Value} allows ({currency.MinorUnits}).",
                ValidationText.OpeningBalanceTooPrecise(currency.Value, currency.MinorUnits)));
        }

        var currencies = await _transactionRepository.GetDistinctCurrenciesInForceAsync(
            request.AccountId, cancellationToken);
        var foreign = currencies
            .Where(code => !string.Equals(code, currency.Value, StringComparison.Ordinal))
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToArray();

        if (foreign.Length > 0)
        {
            errors.Add(new ValidationError(
                nameof(SetOpeningBalanceCommand.AccountId),
                $"'{account.Name}' holds transactions in {string.Join(", ", foreign)} but the account is in "
                + $"{currency.Value}, so its balance cannot be recomputed from an opening balance.",
                ValidationText.AccountHoldsForeignCurrencies(account.Name, string.Join(", ", foreign), currency.Value)));
        }

        return errors;
    }
}
