using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Pine.Core.Accounts;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Configuration;

namespace Pine.Application.Budgets;

/// <summary>
/// The vault's base currency: the one currency a budget is measured in (TD-63).
/// </summary>
/// <remarks>
/// <para>
/// DECIDED 2026-10-07 (docs/62 section 7.4): a budget is a whole-of-vault concept, so its currency is the
/// vault's own, not something a caller picks per budget.
/// </para>
/// <para>
/// THE VAULT HAS NO STORED BASE CURRENCY, SO IT IS RESOLVED, in this order: the vault Setting
/// <see cref="SettingKey"/> if it holds a valid ISO 4217 code (an explicit choice always wins), otherwise
/// the currency of the oldest account (by creation time, then id, so the answer is stable). With neither
/// there is no base currency and a budget cannot be created. No schema change and no data is rewritten.
/// </para>
/// </remarks>
public static class BaseCurrency
{
    /// <summary>The vault Setting that, when present, names the base currency explicitly.</summary>
    public const string SettingKey = "Vault.BaseCurrency";

    /// <summary>Resolves the vault's base currency, or null when the vault has neither a setting nor an account.</summary>
    /// <param name="settings">Read for the explicit choice.</param>
    /// <param name="accounts">Read for the fallback.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public static async Task<CurrencyCode?> ResolveAsync(
        ISettingRepository settings,
        IAccountRepository accounts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(accounts);

        var setting = await settings.GetAsync(SettingKey, cancellationToken).ConfigureAwait(false);
        if (setting is not null && CurrencyCode.TryCreate(setting.Value, out var chosen))
        {
            return chosen;
        }

        return await FromOldestAccountAsync(accounts, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The base currency the vault WOULD have if the setting held <paramref name="settingValue"/>: that code when it is
    /// valid, otherwise (an empty value clears the choice) the oldest account's currency. Used to know the target of
    /// a change before it is saved.
    /// </summary>
    /// <param name="settingValue">The value about to be stored.</param>
    /// <param name="accounts">Read for the fallback.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public static async Task<CurrencyCode?> ResolveWithSettingAsync(
        string? settingValue,
        IAccountRepository accounts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(accounts);

        return CurrencyCode.TryCreate(settingValue, out var chosen)
            ? chosen
            : await FromOldestAccountAsync(accounts, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<CurrencyCode?> FromOldestAccountAsync(IAccountRepository accounts, CancellationToken cancellationToken)
    {
        var all = await accounts.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var oldest = all
            .Where(a => CurrencyCode.TryCreate(a.Balance.Currency.Value, out _))
            .OrderBy(a => a.CreatedAt)
            .ThenBy(a => a.Id)
            .FirstOrDefault();

        return oldest is null ? null : new CurrencyCode(oldest.Balance.Currency.Value);
    }
}
