using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Pine.Application.Common;
using Pine.Core.Accounts;

using Pine.Core.Common.ValueObjects;

namespace Pine.Application.Accounts.Commands;

public record CreateAccountCommand(
    string Name,
    string CurrencyCode,
    AccountType Type,
    decimal InitialBalance,
    Guid? ParentAccountId = null,
    decimal? MonthlyAllocation = null) : IRequest<Guid>, ITransactionalRequest;

public class CreateAccountCommandHandler : IRequestHandler<CreateAccountCommand, Guid>
{
    private readonly IAccountRepository _repository;
    private readonly TimeProvider _timeProvider;

    public CreateAccountCommandHandler(IAccountRepository repository, TimeProvider timeProvider)
    {
        _repository = repository;
        _timeProvider = timeProvider;
    }

    public async Task<Guid> Handle(CreateAccountCommand request, CancellationToken cancellationToken)
    {
        var currency = new CurrencyCode(request.CurrencyCode);
        var initialBalance = new Money(request.InitialBalance, currency);
        var parentId = request.ParentAccountId is { } pid && pid != Guid.Empty ? pid : (Guid?)null;

        var account = Account.Create(
            request.Name,
            request.Type,
            initialBalance,
            _timeProvider,
            parentId,
            request.MonthlyAllocation);

        await _repository.SaveAsync(account, cancellationToken);

        return account.Id;
    }
}

/// <summary>
/// Refuses an account whose currency is not a real ISO 4217 code before the domain throws (TD-78),
/// and validates parent-account hierarchy rules for shared-pool sub-accounts.
/// </summary>
public sealed class CreateAccountCommandValidator : IRequestValidator<CreateAccountCommand>
{
    private readonly IAccountRepository? _accounts;

    public CreateAccountCommandValidator(IAccountRepository? accounts = null)
    {
        _accounts = accounts;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ValidationError>> ValidateAsync(
        CreateAccountCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new List<ValidationError>();
        var validCurrency = CurrencyCodeText.TryNormalise(request.CurrencyCode, out var normalizedCurrency);

        if (!validCurrency)
        {
            errors.Add(new ValidationError(
                nameof(CreateAccountCommand.CurrencyCode),
                $"'{request.CurrencyCode}' is not a valid ISO 4217 currency code.",
                ValidationText.CurrencyCodeInvalid(request.CurrencyCode)));
        }

        if (request.MonthlyAllocation.HasValue && request.MonthlyAllocation.Value < 0m)
        {
            errors.Add(new ValidationError(
                nameof(CreateAccountCommand.MonthlyAllocation),
                "Monthly allocation cannot be negative.",
                ValidationText.AccountMonthlyAllocationNegative()));
        }
        else if (request.MonthlyAllocation.HasValue && validCurrency)
        {
            var currency = new CurrencyCode(normalizedCurrency);
            if (Money.Round(request.MonthlyAllocation.Value, currency) != request.MonthlyAllocation.Value)
            {
                errors.Add(new ValidationError(
                    nameof(CreateAccountCommand.MonthlyAllocation),
                    "Monthly allocation exceeds currency precision.",
                    ValidationText.BudgetLimitTooPrecise(currency.Value, currency.MinorUnits)));
            }
        }

        if (request.ParentAccountId is { } parentId && parentId != Guid.Empty)
        {
            if (request.InitialBalance != 0m)
            {
                errors.Add(new ValidationError(
                    nameof(CreateAccountCommand.InitialBalance),
                    "A sub-account must start with a zero initial balance.",
                    ValidationText.AccountSubAccountInitialBalanceNonZero()));
            }

            if (_accounts is not null)
            {
                var parent = await _accounts.GetByIdAsync(parentId, cancellationToken).ConfigureAwait(false);
                if (parent is null)
                {
                    errors.Add(new ValidationError(
                        nameof(CreateAccountCommand.ParentAccountId),
                        "Parent account not found.",
                        ValidationText.AccountParentNotFound()));
                }
                else if (parent.IsSubAccount)
                {
                    errors.Add(new ValidationError(
                        nameof(CreateAccountCommand.ParentAccountId),
                        "A sub-account cannot be selected as a parent account.",
                        ValidationText.AccountParentCannotBeSubAccount()));
                }
                else if (validCurrency && !string.Equals(parent.Balance.Currency.Value, normalizedCurrency, StringComparison.Ordinal))
                {
                    errors.Add(new ValidationError(
                        nameof(CreateAccountCommand.CurrencyCode),
                        $"Sub-account currency '{normalizedCurrency}' must match parent currency '{parent.Balance.Currency.Value}'.",
                        ValidationText.AccountSubAccountCurrencyMismatch(normalizedCurrency, parent.Balance.Currency.Value)));
                }
            }
        }

        return errors;
    }
}
