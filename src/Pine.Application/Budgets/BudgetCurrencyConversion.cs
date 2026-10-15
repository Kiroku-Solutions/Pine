using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Pine.Core.Budgets;
using Pine.Core.Common.ValueObjects;
using Pine.Core.ExchangeRates;

namespace Pine.Application.Budgets;

/// <summary>A budget and the rate that will convert it.</summary>
/// <param name="Budget">The budget to convert.</param>
/// <param name="Rate">The rate in force for its currency to the target.</param>
public sealed record BudgetConversion(Budget Budget, ExchangeRate Rate);

/// <summary>A budget whose converted limit would round to nothing.</summary>
/// <param name="Category">The budget's category.</param>
/// <param name="FromCurrency">Its current currency.</param>
public sealed record BudgetWouldBeZero(string Category, string FromCurrency);

/// <summary>What a change of base currency would do to the budgets.</summary>
/// <param name="Conversions">The budgets that can be converted, each with its rate.</param>
/// <param name="MissingSources">Source currencies (distinct) of budgets for which no rate is in force.</param>
/// <param name="ZeroResults">Budgets that would convert to a limit of zero.</param>
public sealed record BudgetConversionPlan(
    IReadOnlyList<BudgetConversion> Conversions,
    IReadOnlyList<string> MissingSources,
    IReadOnlyList<BudgetWouldBeZero> ZeroResults)
{
    /// <summary>True when every budget outside the target currency can be converted.</summary>
    public bool CanProceed => MissingSources.Count == 0 && ZeroResults.Count == 0;
}

/// <summary>
/// The one-time conversion of every budget into a new base currency (owner decision, 2026-10-14).
/// </summary>
/// <remarks>
/// <para>
/// STRICT. A budget is converted at the rate in force for exactly its currency to the target (latest effective day
/// not after the as-of instant), or the change is refused: no inverse of another pair, no triangulation, no guess,
/// and no budget is ever left in the old currency by omission. The plan is computed for the validator (to refuse in
/// the user's language) and again by the handler (the backstop), so the two cannot disagree about what "possible"
/// means.
/// </para>
/// <para>
/// Budgets already in the target are untouched. Rounding is <see cref="Budget.ConvertTo"/>'s: half-to-even to the
/// target's scale.
/// </para>
/// </remarks>
public static class BudgetCurrencyConversion
{
    /// <summary>Plans the conversion of every budget not already in <paramref name="target"/>.</summary>
    public static async Task<BudgetConversionPlan> PlanAsync(
        IBudgetRepository budgets,
        IExchangeRateRepository rates,
        CurrencyCode target,
        DateTime asOfUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(budgets);
        ArgumentNullException.ThrowIfNull(rates);
        ArgumentNullException.ThrowIfNull(target);

        var toConvert = (await budgets.GetAllAsync(cancellationToken).ConfigureAwait(false))
            .Where(b => b.Limit.Currency != target)
            .OrderBy(b => b.Category, StringComparer.Ordinal)
            .ToList();

        var conversions = new List<BudgetConversion>();
        var missing = new List<string>();
        var zero = new List<BudgetWouldBeZero>();
        var found = new Dictionary<string, ExchangeRate?>(StringComparer.Ordinal);

        foreach (var budget in toConvert)
        {
            var from = budget.Limit.Currency;
            if (!found.TryGetValue(from.Value, out var rate))
            {
                rate = await rates.GetInForceAsync(from, target, asOfUtc, cancellationToken).ConfigureAwait(false);
                found[from.Value] = rate;
                if (rate is null)
                {
                    missing.Add(from.Value);
                }
            }

            if (rate is null)
            {
                continue;
            }

            if (Money.FromRounded(budget.Limit.Amount * rate.Rate, target).Amount <= 0m)
            {
                zero.Add(new BudgetWouldBeZero(budget.Category, from.Value));
                continue;
            }

            conversions.Add(new BudgetConversion(budget, rate));
        }

        return new BudgetConversionPlan(conversions, missing, zero);
    }
}
