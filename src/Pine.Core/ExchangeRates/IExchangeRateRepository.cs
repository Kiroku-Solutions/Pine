using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pine.Core.Common.ValueObjects;

namespace Pine.Core.ExchangeRates;

/// <summary>Stages exchange rates on the unit of work; committing is the request pipeline's job (TD-01).</summary>
public interface IExchangeRateRepository
{
    /// <summary>Every rate, newest effective date first.</summary>
    Task<IReadOnlyList<ExchangeRate>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>The rate of this pair on this UTC day, if one was entered.</summary>
    Task<ExchangeRate?> GetAsync(CurrencyCode from, CurrencyCode target, DateTime effectiveDateUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// The rate in force on <paramref name="asOfUtc"/>: the entered rate of exactly this pair with the latest
    /// effective day that is not after it. Never an inverse, never an average, never a guess.
    /// </summary>
    Task<ExchangeRate?> GetInForceAsync(CurrencyCode from, CurrencyCode target, DateTime asOfUtc, CancellationToken cancellationToken = default);

    /// <summary>Adds the rate, or stages the changes of a tracked one.</summary>
    Task SaveAsync(ExchangeRate rate, CancellationToken cancellationToken = default);
}
