using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Pine.Application.Budgets;
using Pine.Application.Budgets.Queries;
using Pine.Core.Accounts;
using Pine.Core.Budgets;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Configuration;
using Pine.Core.Planning;

namespace Pine.Application.Planning;

/// <summary>One account's share of total base-currency balance plus any scoped monthly budget remaining.</summary>
public sealed record AccountAllocationSlice(
    Guid AccountId,
    string Name,
    AccountType Type,
    Money Balance,
    double ShareOfTotalPercentage,
    Money? BudgetLimit,
    Money? BudgetSpent,
    Money? BudgetRemaining);

/// <summary>
/// Monthly income-to-allocation summary: how recurring monthly income splits across budgets,
/// savings goals, and unallocated funds, plus each account's share of the total balance.
/// </summary>
public sealed record MonthlyPlanSummary(
    CurrencyCode? BaseCurrency,
    decimal MonthlyRecurringIncome,
    decimal MonthlyBudgetsAllocated,
    decimal MonthlyBudgetsSpent,
    decimal MonthlySavingsAllocated,
    IReadOnlyList<AccountAllocationSlice> AccountShares)
{
    public decimal TotalMonthlyAllocated => MonthlyBudgetsAllocated + MonthlySavingsAllocated;
    public decimal UnallocatedMonthlyIncome => MonthlyRecurringIncome - TotalMonthlyAllocated;
    public bool HasRecurringIncome => MonthlyRecurringIncome > 0m;
    public bool IsOverAllocated => HasRecurringIncome && UnallocatedMonthlyIncome < 0m;
    public double BudgetsShareOfIncome => HasRecurringIncome ? Math.Clamp((double)(MonthlyBudgetsAllocated / MonthlyRecurringIncome) * 100.0, 0, 100) : 0;
    public double SavingsShareOfIncome => HasRecurringIncome ? Math.Clamp((double)(MonthlySavingsAllocated / MonthlyRecurringIncome) * 100.0, 0, 100) : 0;
    public double FreeShareOfIncome => HasRecurringIncome && UnallocatedMonthlyIncome > 0m
        ? Math.Clamp((double)(UnallocatedMonthlyIncome / MonthlyRecurringIncome) * 100.0, 0, 100)
        : 0;
}

public sealed record GetMonthlyPlanSummaryQuery() : IRequest<MonthlyPlanSummary>;

public sealed class GetMonthlyPlanSummaryQueryHandler
    : IRequestHandler<GetMonthlyPlanSummaryQuery, MonthlyPlanSummary>
{
    private readonly IAccountRepository _accounts;
    private readonly ISettingRepository _settings;
    private readonly IRecurringIncomeRepository _incomes;
    private readonly ISavingsGoalRepository _goals;
    private readonly ISender _sender;

    public GetMonthlyPlanSummaryQueryHandler(
        IAccountRepository accounts,
        ISettingRepository settings,
        IRecurringIncomeRepository incomes,
        ISavingsGoalRepository goals,
        ISender sender)
    {
        _accounts = accounts;
        _settings = settings;
        _incomes = incomes;
        _goals = goals;
        _sender = sender;
    }

    public async Task<MonthlyPlanSummary> Handle(
        GetMonthlyPlanSummaryQuery request,
        CancellationToken cancellationToken)
    {
        var accounts = (await _accounts.GetAllAsync(cancellationToken).ConfigureAwait(false)).ToList();
        var baseCurrency = await BaseCurrency.ResolveAsync(_settings, _accounts, cancellationToken).ConfigureAwait(false);
        var incomes = await _incomes.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var goals = await _goals.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var budgets = await _sender.Send(new GetBudgetProgressQuery(), cancellationToken).ConfigureAwait(false);

        if (baseCurrency is null)
        {
            return new MonthlyPlanSummary(null, 0m, 0m, 0m, 0m, Array.Empty<AccountAllocationSlice>());
        }

        var currencyCode = baseCurrency.Value;

        var monthlyIncome = incomes
            .Where(i => i.IsActive && string.Equals(i.Amount.Currency.Value, currencyCode, StringComparison.Ordinal))
            .Sum(i => i.MonthlyEquivalentAmount);

        var baseBudgets = budgets
            .Where(b => string.Equals(b.Limit.Currency.Value, currencyCode, StringComparison.Ordinal))
            .ToList();

        var monthlyBudgetsAllocated = baseBudgets.Sum(b => MonthlyEquivalentLimit(b, baseCurrency));
        var monthlyBudgetsSpent = baseBudgets.Sum(b => b.Spent?.Amount ?? 0m);

        var monthlySavings = goals
            .Where(g => string.Equals(g.MonthlyTarget.Currency.Value, currencyCode, StringComparison.Ordinal))
            .Sum(g => g.MonthlyTarget.Amount);

        var baseAccounts = accounts
            .Where(a => string.Equals(a.Balance.Currency.Value, currencyCode, StringComparison.Ordinal))
            .OrderByDescending(a => a.Balance.Amount)
            .ToList();

        var positiveTotal = baseAccounts.Where(a => a.Balance.Amount > 0m).Sum(a => a.Balance.Amount);

        var slices = baseAccounts.Select(account =>
        {
            var share = positiveTotal > 0m && account.Balance.Amount > 0m
                ? Math.Round((double)(account.Balance.Amount / positiveTotal) * 100.0, 1)
                : 0.0;

            var scopedBudgets = baseBudgets.Where(b => b.AccountId == account.Id).ToList();
            Money? limitSum = null;
            Money? spentSum = null;
            Money? remainingSum = null;

            if (scopedBudgets.Count > 0)
            {
                var normalizedLimit = scopedBudgets.Sum(b => MonthlyEquivalentLimit(b, baseCurrency));
                limitSum = new Money(normalizedLimit, baseCurrency);
                if (scopedBudgets.All(b => b.Spent is not null))
                {
                    var spentTotal = scopedBudgets.Sum(b => b.Spent!.Amount);
                    spentSum = new Money(spentTotal, baseCurrency);
                    remainingSum = limitSum - spentSum;
                }
            }

            return new AccountAllocationSlice(
                account.Id,
                account.Name,
                account.Type,
                account.Balance,
                share,
                limitSum,
                spentSum,
                remainingSum);
        }).ToList();

        return new MonthlyPlanSummary(
            baseCurrency,
            monthlyIncome,
            monthlyBudgetsAllocated,
            monthlyBudgetsSpent,
            monthlySavings,
            slices);
    }

    private static decimal MonthlyEquivalentLimit(BudgetProgress budget, CurrencyCode currency) => budget.Period switch
    {
        BudgetPeriod.Weekly => Money.Round(budget.Limit.Amount * 52m / 12m, currency),
        BudgetPeriod.BiWeekly => Money.Round(budget.Limit.Amount * 2m, currency),
        BudgetPeriod.Yearly => Money.Round(budget.Limit.Amount / 12m, currency),
        _ => budget.Limit.Amount,
    };
}
