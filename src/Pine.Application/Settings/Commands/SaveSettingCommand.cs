using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Pine.Application.Budgets;
using Pine.Application.Common;
using Pine.Core.Accounts;
using Pine.Core.Budgets;
using Pine.Core.Configuration;
using Pine.Core.ExchangeRates;
using Pine.Core.Settings;

namespace Pine.Application.Settings.Commands;

/// <summary>
/// Creates or updates a single vault-scoped setting.
/// </summary>
/// <remarks>
/// This exists so that no UI code has to hold an <c>ISettingRepository</c> (TD-01). Repositories
/// no longer flush anything themselves, so a direct <c>ISettingRepository.SaveAsync</c> from a
/// view model would stage a change on a <c>PineDbContext</c> that nothing ever commits — the write
/// would vanish silently. Going through MediatR gives the write its own DI scope, its own context
/// and, because of <see cref="ITransactionalRequest"/>, its own committed transaction.
/// <para>
/// CHANGING <see cref="BaseCurrency.SettingKey"/> CONVERTS THE BUDGETS, ONCE, IN THE SAME TRANSACTION (owner decision,
/// 2026-10-14): every budget not already in the new base currency is converted at the entered rate in force for its
/// currency to the new one (<see cref="BudgetCurrencyConversion"/>), rounded to the new currency's scale, with the
/// previous amount, currency, rate and time kept on the budget. The validator refuses the change, before anything
/// is written, when a needed rate was never entered; the handler repeats the check so a caller that bypasses the
/// pipeline still cannot leave a budget in the old currency. The setting and the conversions are staged on one
/// unit of work and commit or roll back together.
/// </para>
/// </remarks>
/// <param name="Key">The setting key, for example <c>Appearance.Theme</c>.</param>
/// <param name="Value">The value to persist. Never null; use an empty string to clear.</param>
public record SaveSettingCommand(string Key, string Value) : IRequest, ITransactionalRequest;

public sealed class SaveSettingCommandHandler : IRequestHandler<SaveSettingCommand>
{
    private readonly ISettingRepository _settingRepository;
    private readonly IBudgetRepository _budgets;
    private readonly IExchangeRateRepository _rates;
    private readonly IAccountRepository _accounts;
    private readonly TimeProvider _time;

    public SaveSettingCommandHandler(
        ISettingRepository settingRepository,
        IBudgetRepository budgets,
        IExchangeRateRepository rates,
        IAccountRepository accounts,
        TimeProvider time)
    {
        _settingRepository = settingRepository;
        _budgets = budgets;
        _rates = rates;
        _accounts = accounts;
        _time = time;
    }

    public async Task Handle(SaveSettingCommand request, CancellationToken cancellationToken)
    {
        System.ArgumentNullException.ThrowIfNull(request);

        // SqliteSettingRepository.SaveAsync upserts by Key: an existing row has its value updated
        // in place, so the Id generated here is only used when the key is new.
        // The base currency is stored in its canonical form ("eur " -> "EUR"); an empty value clears the choice
        // so the oldest account's currency applies again. The validator has already refused anything else.
        var value = request.Key == BaseCurrency.SettingKey && CurrencyCodeText.TryNormalise(request.Value, out var code)
            ? code
            : request.Value;
        if (request.Key == BaseCurrency.SettingKey)
        {
            await ConvertBudgetsAsync(value, cancellationToken).ConfigureAwait(false);
        }

        var setting = Setting.Create(request.Key, value);
        await _settingRepository.SaveAsync(setting, cancellationToken).ConfigureAwait(false);
    }

    private async Task ConvertBudgetsAsync(string newSettingValue, CancellationToken cancellationToken)
    {
        var target = await BaseCurrency.ResolveWithSettingAsync(newSettingValue, _accounts, cancellationToken).ConfigureAwait(false);
        if (target is null)
        {
            return;
        }

        var now = _time.GetUtcNow().UtcDateTime;
        var plan = await BudgetCurrencyConversion.PlanAsync(_budgets, _rates, target, now, cancellationToken).ConfigureAwait(false);
        if (!plan.CanProceed)
        {
            // The validator reports this in the user's language before anything is written; this is the backstop.
            throw new InvalidOperationException(
                "The base currency cannot change: no rate in force for " + string.Join(", ", plan.MissingSources)
                + " to " + target.Value + ", or a budget would convert to zero.");
        }

        foreach (var conversion in plan.Conversions)
        {
            conversion.Budget.ConvertTo(target, conversion.Rate.Rate, now);
            await _budgets.SaveAsync(conversion.Budget, cancellationToken).ConfigureAwait(false);
        }
    }
}

/// <summary>
/// Refuses a base-currency setting that is not an ISO 4217 code (TD-63 follow-up). Every other key is accepted as before.
/// </summary>
/// <remarks>
/// An empty value is allowed: it clears the explicit choice, and <see cref="BaseCurrency.ResolveAsync"/> then falls
/// back to the oldest account's currency. Without this check a typo would be stored and silently ignored by the
/// resolver, leaving the user believing a base currency was set.
/// </remarks>
public sealed class SaveSettingCommandValidator : IRequestValidator<SaveSettingCommand>
{
    private readonly IBudgetRepository? _budgets;
    private readonly IExchangeRateRepository? _rates;
    private readonly IAccountRepository? _accounts;
    private readonly TimeProvider? _time;

    /// <summary>
    /// Creates a validator that checks a value's SHAPE only (currency code, preference range). It cannot know whether
    /// a base-currency change is convertible, so it never reports that; the pipeline's validator, built by the
    /// container with the repositories, is the one that does.
    /// </summary>
    public SaveSettingCommandValidator()
    {
    }

    /// <summary>Creates the validator the pipeline uses, able to refuse a base-currency change with no rate.</summary>
    public SaveSettingCommandValidator(
        IBudgetRepository budgets,
        IExchangeRateRepository rates,
        IAccountRepository accounts,
        TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(budgets);
        ArgumentNullException.ThrowIfNull(rates);
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(time);
        _budgets = budgets;
        _rates = rates;
        _accounts = accounts;
        _time = time;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ValidationError>> ValidateAsync(
        SaveSettingCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new List<ValidationError>();
        if (request.Key == BaseCurrency.SettingKey
            && !string.IsNullOrEmpty(request.Value)
            && !CurrencyCodeText.TryNormalise(request.Value, out _))
        {
            errors.Add(new ValidationError(
                nameof(SaveSettingCommand.Value),
                $"'{request.Value}' is not a valid ISO 4217 currency code.",
                ValidationText.CurrencyCodeInvalid(request.Value)));
        }

        ValidatePreference(request, errors);

        // THE BUDGETS MUST BE CONVERTIBLE, OR THE CHANGE IS REFUSED (owner decision, 2026-10-14). Never a guessed rate,
        // never a budget left in the old currency: the user is told which rate to enter first.
        if (request.Key == BaseCurrency.SettingKey && errors.Count == 0 && _budgets is not null)
        {
            var target = await BaseCurrency.ResolveWithSettingAsync(request.Value, _accounts!, cancellationToken).ConfigureAwait(false);
            if (target is not null)
            {
                var plan = await BudgetCurrencyConversion.PlanAsync(
                    _budgets, _rates!, target, _time!.GetUtcNow().UtcDateTime, cancellationToken).ConfigureAwait(false);

                foreach (var source in plan.MissingSources)
                {
                    errors.Add(new ValidationError(
                        nameof(SaveSettingCommand.Value),
                        $"No exchange rate from {source} to {target.Value} is in force; enter it before changing the base currency.",
                        ValidationText.BaseCurrencyRateMissing(source, target.Value)));
                }

                foreach (var zero in plan.ZeroResults)
                {
                    errors.Add(new ValidationError(
                        nameof(SaveSettingCommand.Value),
                        $"The '{zero.Category}' budget would convert to zero {target.Value}.",
                        ValidationText.BaseCurrencyBudgetWouldBeZero(zero.Category, zero.FromCurrency, target.Value)));
                }
            }
        }

        return errors;
    }

    // TD-119: the three operative preferences are refused when out of range, so a typo is never stored and
    // then silently ignored by UserPreferences (which would fall back to configuration unseen).
    private static void ValidatePreference(SaveSettingCommand request, List<ValidationError> errors)
    {
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        var styles = System.Globalization.NumberStyles.Integer;
        switch (request.Key)
        {
            case UserPreferenceKeys.AutoLockIdleMinutes:
                if (!int.TryParse(request.Value, styles, culture, out var minutes)
                    || minutes < UserPreferenceLimits.AutoLockMinMinutes
                    || minutes > UserPreferenceLimits.AutoLockMaxMinutes)
                {
                    errors.Add(new ValidationError(
                        nameof(SaveSettingCommand.Value),
                        "The idle auto-lock must be a whole number of minutes in range.",
                        ValidationText.PreferenceAutoLockRange(UserPreferenceLimits.AutoLockMinMinutes, UserPreferenceLimits.AutoLockMaxMinutes)));
                }

                break;
            case UserPreferenceKeys.CategorisationConfidenceThreshold:
                if (!double.TryParse(request.Value, System.Globalization.NumberStyles.Float, culture, out var gate)
                    || double.IsNaN(gate) || gate < 0 || gate > 1)
                {
                    errors.Add(new ValidationError(
                        nameof(SaveSettingCommand.Value),
                        "The confidence threshold must be a number from 0 to 1.",
                        ValidationText.PreferenceConfidenceRange()));
                }

                break;
            case UserPreferenceKeys.TransactionsPageSize:
                if (!int.TryParse(request.Value, styles, culture, out var size)
                    || size < UserPreferenceLimits.PageSizeMin
                    || size > UserPreferenceLimits.PageSizeMax)
                {
                    errors.Add(new ValidationError(
                        nameof(SaveSettingCommand.Value),
                        "The page size must be a whole number in range.",
                        ValidationText.PreferencePageSizeRange(UserPreferenceLimits.PageSizeMin, UserPreferenceLimits.PageSizeMax)));
                }

                break;
        }
    }
}
