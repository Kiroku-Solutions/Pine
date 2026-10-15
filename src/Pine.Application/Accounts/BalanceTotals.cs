using System;
using System.Collections.Generic;
using System.Linq;
using Pine.Core.Accounts;
using Pine.Core.Common.ValueObjects;

namespace Pine.Application.Accounts;

/// <summary>
/// The vault's account balances totalled for a global view (total balance / dashboard), without ever adding
/// amounts of different currencies (TD-63 follow-up).
/// </summary>
/// <param name="BaseCurrency">The vault's base currency, or null when none could be resolved (no setting, no account).</param>
/// <param name="BaseTotal">
/// The sum of the balances of the accounts ALREADY in <paramref name="BaseCurrency"/>; null when the base currency is
/// unknown or no account is in it. It never includes a converted amount.
/// </param>
/// <param name="OtherTotals">One total per remaining currency, ordered by code. Shown beside, never added to, the base total.</param>
/// <param name="AllTotals">One total per currency including the base one, ordered by code.</param>
/// <param name="AccountCount">How many accounts the figures cover.</param>
public sealed record BalanceTotals(
    CurrencyCode? BaseCurrency,
    Money? BaseTotal,
    IReadOnlyList<Money> OtherTotals,
    IReadOnlyList<Money> AllTotals,
    int AccountCount)
{
    /// <summary>
    /// Totals <paramref name="accounts"/> per currency. A single base-currency figure is produced only from accounts
    /// that are already in the base currency: Pine has no exchange rates and fetches none, so a balance in another
    /// currency is reported as its own line, never converted into the base.
    /// </summary>
    /// <param name="accounts">The accounts to total.</param>
    /// <param name="baseCurrency">The vault's base currency, or null.</param>
    public static BalanceTotals Compute(IEnumerable<Account> accounts, CurrencyCode? baseCurrency)
    {
        ArgumentNullException.ThrowIfNull(accounts);

        var list = accounts.ToList();
        var all = list
            .GroupBy(account => account.Balance.Currency)
            .Select(group => group.Aggregate(Money.Zero(group.Key), (running, account) => running + account.Balance))
            .OrderBy(total => total.Currency.Value, StringComparer.Ordinal)
            .ToList();

        Money? baseTotal = null;
        IReadOnlyList<Money> others = all;
        if (baseCurrency is not null)
        {
            baseTotal = all.FirstOrDefault(total => total.Currency == baseCurrency);
            if (baseTotal is not null)
            {
                others = all.Where(total => total.Currency != baseCurrency).ToList();
            }
        }

        return new BalanceTotals(baseCurrency, baseTotal, others, all, list.Count);
    }
}
