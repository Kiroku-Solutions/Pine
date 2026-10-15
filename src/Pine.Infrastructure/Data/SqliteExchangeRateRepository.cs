using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pine.Core.Common.ValueObjects;
using Pine.Core.ExchangeRates;

namespace Pine.Infrastructure.Data;

/// <remarks>
/// Stages changes on the scoped context; the request pipeline commits them (TD-01). The table holds a handful of
/// rows a person typed, so ordering and the "in force" pick are done in memory: SQLite would compare the dates as
/// text, which is correct for UTC but not worth being the thing that has to stay true.
/// </remarks>
public sealed class SqliteExchangeRateRepository : IExchangeRateRepository
{
    private readonly PineDbContext _context;

    public SqliteExchangeRateRepository(PineDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<ExchangeRate>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var all = await _context.ExchangeRates.ToListAsync(cancellationToken).ConfigureAwait(false);
        return all
            .OrderByDescending(r => r.EffectiveDateUtc)
            .ThenBy(r => r.FromCurrency.Value, StringComparer.Ordinal)
            .ThenBy(r => r.ToCurrency.Value, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<ExchangeRate?> GetAsync(CurrencyCode from, CurrencyCode target, DateTime effectiveDateUtc, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(target);

        var day = ExchangeRate.DayOf(effectiveDateUtc);
        var all = await _context.ExchangeRates.ToListAsync(cancellationToken).ConfigureAwait(false);
        return all.FirstOrDefault(r => r.FromCurrency == from && r.ToCurrency == target && r.EffectiveDateUtc == day);
    }

    public async Task<ExchangeRate?> GetInForceAsync(CurrencyCode from, CurrencyCode target, DateTime asOfUtc, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(target);

        var all = await _context.ExchangeRates.ToListAsync(cancellationToken).ConfigureAwait(false);
        return all
            .Where(r => r.FromCurrency == from && r.ToCurrency == target && r.EffectiveDateUtc <= asOfUtc)
            .OrderByDescending(r => r.EffectiveDateUtc)
            .FirstOrDefault();
    }

    public async Task SaveAsync(ExchangeRate rate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rate);

        var existing = await _context.ExchangeRates.FindAsync(new object[] { rate.Id }, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            await _context.ExchangeRates.AddAsync(rate, cancellationToken).ConfigureAwait(false);
        }
    }
}
