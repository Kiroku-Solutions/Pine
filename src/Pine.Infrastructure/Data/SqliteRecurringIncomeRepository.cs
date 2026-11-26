using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pine.Core.Planning;

namespace Pine.Infrastructure.Data;

public sealed class SqliteRecurringIncomeRepository : IRecurringIncomeRepository
{
    private readonly PineDbContext _dbContext;

    public SqliteRecurringIncomeRepository(PineDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<RecurringIncome?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await _dbContext.RecurringIncomes
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<RecurringIncome>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await _dbContext.RecurringIncomes
            .OrderBy(r => r.DayOfMonth)
            .ThenBy(r => r.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task SaveAsync(RecurringIncome income, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(income);

        var existing = await _dbContext.RecurringIncomes
            .FindAsync(new object[] { income.Id }, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            await _dbContext.RecurringIncomes.AddAsync(income, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (ReferenceEquals(existing, income))
        {
            return;
        }

        _dbContext.Entry(existing).CurrentValues.SetValues(income);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var existing = await _dbContext.RecurringIncomes
            .FindAsync(new object[] { id }, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            _dbContext.RecurringIncomes.Remove(existing);
        }
    }
}
