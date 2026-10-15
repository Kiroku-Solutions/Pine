using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Pine.Core.ExchangeRates;

namespace Pine.Application.ExchangeRates.Queries;

/// <summary>One entered rate as the Settings card lists it.</summary>
/// <param name="Id">Identity.</param>
/// <param name="FromCurrency">The currency converted from.</param>
/// <param name="ToCurrency">The currency converted to.</param>
/// <param name="Rate">Units of <paramref name="ToCurrency"/> per unit of <paramref name="FromCurrency"/>.</param>
/// <param name="EffectiveDateUtc">The UTC day it takes effect.</param>
/// <param name="Source">Always <c>manual</c>.</param>
/// <param name="InForce">The rate a conversion would use for this pair today.</param>
public sealed record ExchangeRateListItem(
    Guid Id,
    string FromCurrency,
    string ToCurrency,
    decimal Rate,
    DateTime EffectiveDateUtc,
    string Source,
    bool InForce);

/// <summary>Lists every entered rate, newest effective day first. Not transactional: a query.</summary>
public record GetExchangeRatesQuery : IRequest<IReadOnlyList<ExchangeRateListItem>>;

/// <summary>Reads the rates.</summary>
public sealed class GetExchangeRatesQueryHandler : IRequestHandler<GetExchangeRatesQuery, IReadOnlyList<ExchangeRateListItem>>
{
    private readonly IExchangeRateRepository _rates;
    private readonly TimeProvider _time;

    /// <summary>Creates the handler.</summary>
    public GetExchangeRatesQueryHandler(IExchangeRateRepository rates, TimeProvider time)
    {
        _rates = rates;
        _time = time;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ExchangeRateListItem>> Handle(GetExchangeRatesQuery request, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var all = await _rates.GetAllAsync(cancellationToken).ConfigureAwait(false);

        var inForce = all
            .Where(r => r.EffectiveDateUtc <= now)
            .GroupBy(r => (r.FromCurrency.Value, r.ToCurrency.Value))
            .Select(g => g.OrderByDescending(r => r.EffectiveDateUtc).First().Id)
            .ToHashSet();

        return all
            .Select(r => new ExchangeRateListItem(
                r.Id, r.FromCurrency.Value, r.ToCurrency.Value, r.Rate, r.EffectiveDateUtc, r.Source, inForce.Contains(r.Id)))
            .ToList();
    }
}
