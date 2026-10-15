using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Pine.Application.Common;
using Pine.Core.Accounts;
using Pine.Core.Budgets;
using Pine.Core.Common;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Configuration;
using Pine.Core.Sync;

namespace Pine.Application.Budgets.Commands;

/// <summary>Creates a budget against a category label, in the vault's base currency.</summary>
/// <remarks>
/// <para>
/// NO CURRENCY PARAMETER (TD-63, decided 2026-10-07): a budget's currency is the vault's base currency
/// (<see cref="BaseCurrency"/>), resolved by the handler and the validator, never chosen by the caller.
/// </para>
/// The first parameter was a <c>Guid CategoryId</c> into a table nothing in production had written
/// to since <c>67af1f3</c> removed the Diagnostics "Test Features" button. Plan item
/// 2.3 decided TD-132 in favour of the label, so a caller now passes the same kind of string the
/// user types on the transaction form and the import wizard writes into a split. The budgets page sends
/// it since post-audit plan item 3.0 (TD-152); before that, nothing in the product did.
/// </remarks>
public record CreateBudgetCommand(
    string Category,
    decimal Amount,
    BudgetPeriod Period,
    DateTimeOffset? StartDate = null,
    DateTimeOffset? EndDate = null) : IRequest<Guid>, ITransactionalRequest;

public class CreateBudgetCommandHandler : IRequestHandler<CreateBudgetCommand, Guid>
{
    private readonly IBudgetRepository _budgetRepository;
    private readonly ISettingRepository _settings;
    private readonly IAccountRepository _accounts;

    public CreateBudgetCommandHandler(
        IBudgetRepository budgetRepository,
        ISettingRepository settings,
        IAccountRepository accounts)
    {
        _budgetRepository = budgetRepository;
        _settings = settings;
        _accounts = accounts;
    }

    public async Task<Guid> Handle(CreateBudgetCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The validator has already refused a vault with no base currency; this is the backstop for a
        // caller that bypasses the pipeline.
        var currency = await BaseCurrency.ResolveAsync(_settings, _accounts, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The vault has no base currency, so a budget cannot be created.");
        var limit = new Money(request.Amount, currency);
        var budget = Budget.Create(request.Category, limit, request.Period, request.StartDate, request.EndDate);
        
        await _budgetRepository.SaveAsync(budget, cancellationToken);
        
        return budget.Id;
    }
}

/// <summary>
/// Refuses a budget that cannot measure anything, or that already exists, before a row is written
/// (TD-152).
/// </summary>
/// <remarks>
/// <para>
/// THE AGGREGATE REFUSES MOST OF THESE TOO, AND THAT IS NOT DUPLICATION. <c>Budget.Create</c> and
/// <c>Money</c> throw <see cref="ArgumentException"/>s whose English sentences name constructor
/// parameters; a user would have read "A budget must name the category it is measured against.
/// (Parameter 'category')" or a generic failure. A validator reports a sentence the screen can show in
/// the user's language, and the aggregate stays the backstop for any caller that bypasses the pipeline.
/// </para>
/// <para>
/// TWO REFUSALS THE AGGREGATE DOES NOT MAKE, BOTH ABOUT MEASURING. A limit of zero or less is a budget
/// every purchase exceeds. A fortnightly budget with no dates is accepted by the aggregate and then
/// reported as "not known" for ever, because a fortnight has no calendar anchor
/// (<c>Budget.ResolveWindow</c>) — creating one would produce a row that can never show a figure.
/// </para>
/// <para>
/// A DUPLICATE IS REFUSED BECAUSE THE PAGE WOULD SHOW IT TWICE, IDENTICALLY. Two budgets with the same
/// category, currency, period and dates measure the same splits against the same window; the second is
/// almost always a double press. The category is compared trimmed and ordinally, case intact — the rule
/// <c>Budget.Category</c> documents.
/// </para>
/// </remarks>
public sealed class CreateBudgetCommandValidator : IRequestValidator<CreateBudgetCommand>
{
    private readonly IBudgetRepository _budgets;
    private readonly ISettingRepository _settings;
    private readonly IAccountRepository _accounts;

    /// <summary>Creates the validator.</summary>
    /// <param name="budgets">Read for the duplicate check.</param>
    /// <param name="settings">Read for the base currency.</param>
    /// <param name="accounts">Read for the base currency.</param>
    public CreateBudgetCommandValidator(IBudgetRepository budgets, ISettingRepository settings, IAccountRepository accounts)
    {
        ArgumentNullException.ThrowIfNull(budgets);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(accounts);
        _budgets = budgets;
        _settings = settings;
        _accounts = accounts;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ValidationError>> ValidateAsync(
        CreateBudgetCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new List<ValidationError>();

        if (string.IsNullOrWhiteSpace(request.Category))
        {
            errors.Add(new ValidationError(
                nameof(CreateBudgetCommand.Category),
                "A budget must name the category it measures.",
                ValidationText.BudgetCategoryRequired()));
        }

        if (request.Amount <= 0)
        {
            errors.Add(new ValidationError(
                nameof(CreateBudgetCommand.Amount),
                $"A budget limit must be greater than zero; it was {request.Amount}.",
                ValidationText.BudgetLimitNotPositive()));
        }

        // THE CURRENCY IS THE VAULT'S, NOT THE CALLER'S (TD-63).
        var currency = await BaseCurrency.ResolveAsync(_settings, _accounts, cancellationToken).ConfigureAwait(false);
        if (currency is null)
        {
            errors.Add(new ValidationError(
                nameof(CreateBudgetCommand.Amount),
                "The vault has no base currency yet; create an account first.",
                ValidationText.BudgetNoBaseCurrency()));
        }
        else if (request.Amount > 0 && Money.Round(request.Amount, currency) != request.Amount)
        {
            errors.Add(new ValidationError(
                nameof(CreateBudgetCommand.Amount),
                $"{request.Amount} has more decimal places than {currency.Value} allows ({currency.MinorUnits}).",
                ValidationText.BudgetLimitTooPrecise(currency.Value, currency.MinorUnits)));
        }

        var hasBothDates = request.StartDate.HasValue && request.EndDate.HasValue;

        // Half a window is ignored by Budget.ResolveWindow, which falls back to the calendar period: a
        // monthly budget sent with only a start date would silently not start then (independent review
        // of item 3.0). Custom and bi-weekly report their own, more specific refusal below.
        if (request.StartDate.HasValue != request.EndDate.HasValue
            && request.Period is not (BudgetPeriod.Custom or BudgetPeriod.BiWeekly))
        {
            errors.Add(new ValidationError(
                nameof(CreateBudgetCommand.StartDate),
                "A budget's dates are both given or both left out; one alone would be ignored.",
                ValidationText.BudgetDatesIncomplete()));
        }

        if (request.Period == BudgetPeriod.Custom && !hasBothDates)
        {
            errors.Add(new ValidationError(
                nameof(CreateBudgetCommand.Period),
                "A custom budget needs both a start and an end date.",
                ValidationText.BudgetCustomNeedsDates()));
        }
        else if (request.Period == BudgetPeriod.BiWeekly && !hasBothDates)
        {
            errors.Add(new ValidationError(
                nameof(CreateBudgetCommand.Period),
                "A bi-weekly budget needs its dates: a fortnight has no calendar anchor, so its window could never be resolved.",
                ValidationText.BudgetBiWeeklyNeedsDates()));
        }

        if (hasBothDates && request.StartDate!.Value >= request.EndDate!.Value)
        {
            errors.Add(new ValidationError(
                nameof(CreateBudgetCommand.StartDate),
                "A budget's start date must be before its end date.",
                ValidationText.BudgetDatesOutOfOrder()));
        }

        // Only worth asking once the request is otherwise valid: a duplicate of an invalid budget is
        // not the problem to report.
        if (errors.Count == 0 && currency is not null)
        {
            var category = request.Category.Trim();
            var existing = await _budgets.GetAllAsync(cancellationToken).ConfigureAwait(false);

            // THE NATURAL KEY, NOT A SECOND COPY OF ITS RULE (TD-146). The tuple that stops one vault holding
            // two such budgets is the tuple a merge recognises one budget across two vaults by; written twice
            // they would drift, and a budget one rule calls new the other would call a duplicate.
            var key = NaturalKeys.BudgetKey(category, currency.Value, request.Period, request.StartDate, request.EndDate);

            if (existing.Any(b => string.Equals(NaturalKeys.For(b), key, StringComparison.Ordinal)))
            {
                errors.Add(new ValidationError(
                    nameof(CreateBudgetCommand.Category),
                    $"A {request.Period} budget for '{category}' in {currency.Value} already exists.",
                    ValidationText.BudgetAlreadyExists(category, currency.Value)));
            }
        }

        return errors;
    }
}
