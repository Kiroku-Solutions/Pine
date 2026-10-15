using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Pine.Application.Common;
using Pine.Core.Common.ValueObjects;
using Pine.Core.ExchangeRates;

namespace Pine.Application.ExchangeRates.Commands;

/// <summary>
/// Enters, or corrects, the conversion rate of one currency pair from a UTC day. THE ONLY WAY A RATE GETS INTO
/// THE VAULT: Pine is offline-first and never asks anything on the network for one.
/// </summary>
/// <remarks>
/// A rate for a pair and day that already has one REPLACES it (a correction), so the pair-and-day is unique.
/// Nothing else is converted by this command; the rate is data for a base-currency change to read.
/// </remarks>
/// <param name="FromCurrency">The currency converted from (ISO 4217).</param>
/// <param name="ToCurrency">The currency converted to (ISO 4217).</param>
/// <param name="Rate">Units of <paramref name="ToCurrency"/> per one unit of <paramref name="FromCurrency"/>.</param>
/// <param name="EffectiveDateUtc">The UTC day it takes effect; only the day counts.</param>
public record SetExchangeRateCommand(string FromCurrency, string ToCurrency, decimal Rate, DateTime EffectiveDateUtc)
    : IRequest<Guid>, ITransactionalRequest;

/// <summary>Stores a manually entered rate.</summary>
public sealed class SetExchangeRateCommandHandler : IRequestHandler<SetExchangeRateCommand, Guid>
{
    private readonly IExchangeRateRepository _rates;

    /// <summary>Creates the handler.</summary>
    public SetExchangeRateCommandHandler(IExchangeRateRepository rates)
    {
        _rates = rates;
    }

    /// <inheritdoc />
    public async Task<Guid> Handle(SetExchangeRateCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var from = new CurrencyCode(request.FromCurrency);
        var to = new CurrencyCode(request.ToCurrency);

        var existing = await _rates.GetAsync(from, to, request.EffectiveDateUtc, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            existing.ChangeRate(request.Rate);
            await _rates.SaveAsync(existing, cancellationToken).ConfigureAwait(false);
            return existing.Id;
        }

        var rate = ExchangeRate.Create(from, to, request.Rate, request.EffectiveDateUtc);
        await _rates.SaveAsync(rate, cancellationToken).ConfigureAwait(false);
        return rate.Id;
    }
}

/// <summary>
/// Refuses a rate that is not two different ISO 4217 currencies, a positive rate within the scale policy, and a date.
/// </summary>
public sealed class SetExchangeRateCommandValidator : IRequestValidator<SetExchangeRateCommand>
{
    /// <inheritdoc />
    public Task<IReadOnlyList<ValidationError>> ValidateAsync(SetExchangeRateCommand request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new List<ValidationError>();

        var fromOk = CurrencyCodeText.TryNormalise(request.FromCurrency, out var from);
        if (!fromOk)
        {
            errors.Add(new ValidationError(
                nameof(SetExchangeRateCommand.FromCurrency),
                $"'{request.FromCurrency}' is not a valid ISO 4217 currency code.",
                ValidationText.CurrencyCodeInvalid(request.FromCurrency)));
        }

        var toOk = CurrencyCodeText.TryNormalise(request.ToCurrency, out var to);
        if (!toOk)
        {
            errors.Add(new ValidationError(
                nameof(SetExchangeRateCommand.ToCurrency),
                $"'{request.ToCurrency}' is not a valid ISO 4217 currency code.",
                ValidationText.CurrencyCodeInvalid(request.ToCurrency)));
        }

        if (fromOk && toOk && string.Equals(from, to, StringComparison.Ordinal))
        {
            errors.Add(new ValidationError(
                nameof(SetExchangeRateCommand.ToCurrency),
                "A rate converts between two different currencies.",
                ValidationText.ExchangeRateSameCurrency(to)));
        }

        switch (ExchangeRate.Check(request.Rate))
        {
            case ExchangeRateProblem.NotPositive:
                errors.Add(new ValidationError(
                    nameof(SetExchangeRateCommand.Rate),
                    $"A rate must be greater than zero; it was {request.Rate}.",
                    ValidationText.ExchangeRateNotPositive()));
                break;
            case ExchangeRateProblem.TooLarge:
                errors.Add(new ValidationError(
                    nameof(SetExchangeRateCommand.Rate),
                    $"A rate above {ExchangeRate.MaximumRate} is refused as a likely typing error.",
                    ValidationText.ExchangeRateTooLarge(ExchangeRate.MaximumRate)));
                break;
            case ExchangeRateProblem.TooPrecise:
                errors.Add(new ValidationError(
                    nameof(SetExchangeRateCommand.Rate),
                    $"{request.Rate} has more than {ExchangeRate.RateScale} decimal places.",
                    ValidationText.ExchangeRateTooPrecise(ExchangeRate.RateScale)));
                break;
        }

        if (request.EffectiveDateUtc.Year < 1900)
        {
            errors.Add(new ValidationError(
                nameof(SetExchangeRateCommand.EffectiveDateUtc),
                "A rate needs the date it takes effect.",
                ValidationText.ExchangeRateDateRequired()));
        }

        return Task.FromResult<IReadOnlyList<ValidationError>>(errors);
    }
}
